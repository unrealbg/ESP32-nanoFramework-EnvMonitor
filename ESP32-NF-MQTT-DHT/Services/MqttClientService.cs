namespace ESP32_NF_MQTT_DHT.Services
{
    using System;
    using System.Net.Sockets;
    using System.Threading;

    using Contracts;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.MQTT;
    using ESP32_NF_MQTT_DHT.Services.MQTT.Contracts;

    using nanoFramework.Hardware.Esp32;
    using nanoFramework.M2Mqtt;
    using nanoFramework.M2Mqtt.Messages;

    using static Helpers.Constants;
    using static Settings.MqttSettings;

    /// <summary>
    /// Service that manages MQTT client functionalities, including connecting to the broker,
    /// handling messages, managing sensor data, and reconnecting in case of errors.
    /// </summary>
    internal class MqttClientService : IMqttClientService, IDisposable
    {
        private const int ConnectionThreadJoinTimeoutMs = 1000;
        private const int DeepSleepWaitMs = 2000;
        private const string WifiSource = "WiFi";
        private const string InternetSource = "Internet";

        private readonly IConnectionService _connectionService;
        private readonly IInternetConnectionService _internetConnectionService;
        private readonly MqttMessageHandler _mqttMessageHandler;
        private readonly IMqttPublishService _mqttPublishService;
        private readonly IMqttConnectionManager _connectionManager;

        private readonly CircuitBreaker _circuitBreaker = new CircuitBreaker();
        private readonly ReconnectStrategy _reconnectStrategy = new ReconnectStrategy(InitialReconnectDelayMs, MaxReconnectDelayMs);

        private readonly ManualResetEvent _stopSignal = new ManualResetEvent(false);
        private readonly ManualResetEvent _wakeSignal = new ManualResetEvent(false);
        private readonly WaitHandle[] _workerSignals;
        private readonly object _connectionLock = new object();
        private readonly object _stateLock = new object();
        private readonly object _randomLock = new object();
        private readonly object _heartbeatLock = new object();

        private bool _isRunning;
        private bool _isHeartbeatRunning;
        private bool _isDisposed;
        private bool _isWifiConnected = true;

        private EventHandler _internetLostHandler;
        private EventHandler _internetRestoredHandler;
        private EventHandler _connectionLostHandler;
        private EventHandler _connectionRestoredHandler;

        private Thread _workerThread;

        private readonly Random _random = new Random();

        /// <summary>
        /// Initializes a new instance of the MqttClientService class.
        /// </summary>
        public MqttClientService(
            IConnectionService connectionService,
            IInternetConnectionService internetConnectionService,
            MqttMessageHandler mqttMessageHandler,
            IMqttPublishService mqttPublishService,
            IMqttConnectionManager connectionManager)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _internetConnectionService = internetConnectionService ?? throw new ArgumentNullException(nameof(internetConnectionService));
            _mqttMessageHandler = mqttMessageHandler ?? throw new ArgumentNullException(nameof(mqttMessageHandler));
            _mqttPublishService = mqttPublishService ?? throw new ArgumentNullException(nameof(mqttPublishService));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));

            _internetLostHandler = (s, e) => this.OnConnectivityChanged(s, false, InternetSource);
            _internetRestoredHandler = (s, e) => this.OnConnectivityChanged(s, true, InternetSource);
            _connectionLostHandler = (s, e) => this.OnConnectivityChanged(s, false, WifiSource);
            _connectionRestoredHandler = (s, e) => this.OnConnectivityChanged(s, true, WifiSource);

            _internetConnectionService.InternetLost += _internetLostHandler;
            _internetConnectionService.InternetRestored += _internetRestoredHandler;
            _connectionService.ConnectionLost += _connectionLostHandler;
            _connectionService.ConnectionRestored += _connectionRestoredHandler;

            this.SetIsRunning(true);

            _workerSignals = new WaitHandle[] { _stopSignal, _wakeSignal };
        }

        /// <summary>
        /// Gets the current instance of the MQTT client.
        /// </summary>
        public MqttClient MqttClient
        {
            get
            {
                ThrowIfDisposed();
                return _connectionManager.MqttClient;
            }
        }

        /// <summary>
        /// Starts the MQTT service by establishing a connection to the broker.
        /// </summary>
        public void Start()
        {
            ThrowIfDisposed();

            lock (_connectionLock)
            {
                if (_workerThread != null && _workerThread.IsAlive)
                {
                    LogHelper.LogInformation("MQTT worker already running");
                    _wakeSignal.Set();
                    return;
                }

                _stopSignal.Reset();
                _wakeSignal.Reset();
                _circuitBreaker.Close();
                this.SetIsRunning(true);

                _workerThread = new Thread(this.ConnectivityLoop);
                _workerThread.Start();
            }

            _wakeSignal.Set();
        }

        /// <summary>
        /// Stops the MQTT service.
        /// </summary>
        public void Stop()
        {
            ThrowIfDisposed();

            this.SetIsRunning(false);

            this.SafeDisconnect();

            _stopSignal.Set();
            _wakeSignal.Set();

            lock (_connectionLock)
            {
                if (_workerThread != null && _workerThread.IsAlive)
                {
                    _workerThread.Join(ConnectionThreadJoinTimeoutMs);
                    if (_workerThread.IsAlive)
                    {
                        LogHelper.LogWarning("MQTT worker did not terminate within timeout.");
                    }
                }
                _workerThread = null;
            }
        }

        /// <summary>
        /// Disposes the used resources.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            try
            {
                Stop();

                if (_internetConnectionService != null)
                {
                    _internetConnectionService.InternetLost -= _internetLostHandler;
                    _internetConnectionService.InternetRestored -= _internetRestoredHandler;
                }

                if (_connectionService != null)
                {
                    _connectionService.ConnectionLost -= _connectionLostHandler;
                    _connectionService.ConnectionRestored -= _connectionRestoredHandler;
                }

                if (MqttClient != null)
                {
                    MqttClient.ConnectionClosed -= ConnectionClosed;
                    MqttClient.MqttMsgPublishReceived -= _mqttMessageHandler.HandleIncomingMessage;
                }

                _internetLostHandler = null;
                _internetRestoredHandler = null;
                _connectionLostHandler = null;
                _connectionRestoredHandler = null;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Exception in Dispose: {ex.Message}\n{ex}");
            }
            finally
            {
                _isDisposed = true;
            }
        }

        /// <summary>
        /// Single worker loop coordinating Wi-Fi, internet, MQTT connection, and sensor publishing.
        /// Keeps retries, deep sleep logic, and publishing cadence inside one thread to minimize memory usage.
        /// </summary>
        private void ConnectivityLoop()
        {
            int attemptCount = 0;
            int reconnectDelay = InitialReconnectDelayMs;
            DateTime nextSensorPublish = DateTime.UtcNow;

            while (this.GetIsRunning())
            {
                if (_circuitBreaker.IsOpen)
                {
                    LogHelper.LogWarning("Circuit breaker open. Waiting before retrying MQTT connection.");
                    if (this.WaitForWake(reconnectDelay))
                    {
                        break;
                    }

                    continue;
                }

                bool wifiReady = this.EnsureWifiConnected();
                bool internetReady = wifiReady && this.EnsureInternetAvailable();

                if (!wifiReady || !internetReady)
                {
                    this.SafeDisconnect();
                    int wait = internetReady ? InternetCheckIntervalMs : 5000;
                    if (this.WaitForWake(wait))
                    {
                        break;
                    }

                    continue;
                }

                if (!this.IsMqttReady())
                {
                    if (this.AttemptBrokerConnection())
                    {
                        LogHelper.LogInformation("Connected to MQTT broker.");
                        attemptCount = 0;
                        reconnectDelay = InitialReconnectDelayMs;
                        nextSensorPublish = DateTime.UtcNow;
                        continue;
                    }

                    attemptCount++;
                    LogHelper.LogWarning($"MQTT connection failed (attempt {attemptCount}/{MaxTotalAttempts}).");

                    if (attemptCount >= MaxTotalAttempts)
                    {
                        _circuitBreaker.Open(TimeSpan.FromMinutes(CircuitBreakerTimeoutMinutes));
                        attemptCount = 0;
                        reconnectDelay = InitialReconnectDelayMs;
                        this.HandleMaxAttemptsReached();
                        continue;
                    }

                    reconnectDelay = _reconnectStrategy.GetNextDelay(reconnectDelay);
                    if (this.WaitForWake(reconnectDelay + this.GetJitter()))
                    {
                        break;
                    }

                    continue;
                }

                if (DateTime.UtcNow >= nextSensorPublish)
                {
                    this.PublishSensorData();
                    nextSensorPublish = DateTime.UtcNow.AddMilliseconds(SensorDataIntervalMs);
                }

                int waitForNextCycle = (int)Math.Max(500, (nextSensorPublish - DateTime.UtcNow).TotalMilliseconds);
                if (this.WaitForWake(waitForNextCycle))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Checks if the process is currently running by accessing the _isRunning variable in a thread-safe manner.
        /// </summary>
        /// <returns>Returns true if the process is running, otherwise false.</returns>
        private bool GetIsRunning()
        {
            lock (_stateLock)
            {
                return _isRunning;
            }
        }

        /// <summary>
        /// Sets the running state of the process in a thread-safe manner.
        /// </summary>
        /// <param name="value">Indicates whether the process should be in a running state or not.</param>
        private void SetIsRunning(bool value)
        {
            lock (_stateLock)
            {
                _isRunning = value;
            }
        }

        /// <summary>
        /// Handles actions when maximum connection attempts are reached – enters deep sleep.
        /// </summary>
        private void HandleMaxAttemptsReached()
        {
            LogHelper.LogWarning("Entering deep sleep to conserve power");

            this.SafeDisconnect();

            _stopSignal.WaitOne(DeepSleepWaitMs, false);

            TimeSpan deepSleepDuration = new TimeSpan(0, DeepSleepMinutes, 0);

            Sleep.EnableWakeupByTimer(deepSleepDuration);
            Sleep.StartDeepSleep();
        }

        /// <summary>
        /// Attempts to connect to the MQTT broker while checking internet connectivity
        /// and handling exceptions.
        /// </summary>
        private bool AttemptBrokerConnection()
        {
            if (!_internetConnectionService.IsInternetAvailable())
            {
                return false;
            }

            this.SafeDisconnect();

            try
            {
                bool isConnected = _connectionManager.Connect(Broker, ClientId, ClientUsername, ClientPassword);

                if (isConnected && this.MqttClient != null && this.MqttClient.IsConnected)
                {
                    this.InitializeMqttClient();
                    return true;
                }
            }
            catch (SocketException ex)
            {
                LogHelper.LogError($"Socket error: {ex.Message}\n{ex}");
                LogService.LogCritical($"Socket error: {ex.Message}\n{ex}");
            }
            catch (NullReferenceException ex)
            {
                LogHelper.LogError($"Null reference encountered: {ex.Message}\n{ex}");
                LogService.LogCritical($"Null reference encountered: {ex.Message}\n{ex}");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"MQTT connect error: {ex.Message}\n{ex}");
                LogService.LogCritical($"MQTT connect error: {ex.Message}\n{ex}");
            }

            this.SafeDisconnect();
            return false;
        }

        /// <summary>
        /// Initializes the MQTT client by subscribing to topics and setting event handlers.
        /// </summary>
        private void InitializeMqttClient()
        {
            if (this.MqttClient == null)
            {
                LogHelper.LogError("Cannot initialize null MQTT client");
                return;
            }

            this.MqttClient.ConnectionClosed -= this.ConnectionClosed;
            this.MqttClient.MqttMsgPublishReceived -= _mqttMessageHandler.HandleIncomingMessage;

            this.MqttClient.ConnectionClosed += this.ConnectionClosed;
            this.MqttClient.Subscribe(new[] { "#", OTA.Config.TopicCmd }, new[] { MqttQoSLevel.AtLeastOnce, MqttQoSLevel.AtLeastOnce });
            this.MqttClient.MqttMsgPublishReceived += _mqttMessageHandler.HandleIncomingMessage;

            _mqttMessageHandler.SetMqttClient(this.MqttClient);
            _mqttPublishService.SetMqttClient(this.MqttClient);

            lock (_heartbeatLock)
            {
                if (!_isHeartbeatRunning)
                {
                    _mqttPublishService.StartHeartbeat();
                    _isHeartbeatRunning = true;
                }
            }

            LogHelper.LogInformation("MQTT client setup complete");
        }

        /// <summary>
        /// Handles changes in connectivity, logging the status and managing the connection state accordingly.
        /// </summary>
        /// <param name="sender">Indicates the origin of the connectivity change event.</param>
        /// <param name="isRestored">Indicates whether the connectivity has been restored.</param>
        /// <param name="source">Specifies the source of the connectivity change.</param>
        private void OnConnectivityChanged(object sender, bool isRestored, string source)
        {
            if (_isDisposed)
            {
                return;
            }

            if (source == WifiSource)
            {
                _isWifiConnected = isRestored;

                if (isRestored)
                {
                    LogHelper.LogInformation("WiFi restored.");
                    _wakeSignal.Set();
                    this.Start();
                }
                else
                {
                    LogHelper.LogWarning("WiFi lost.");
                    this.SafeDisconnect();
                    _wakeSignal.Set();
                }
            }
            else if (source == InternetSource)
            {
                if (!_connectionService.IsConnected)
                {
                    LogHelper.LogInformation("WiFi not connected, checking WiFi state explicitly...");
                    _connectionService.CheckConnection();

                    if (_connectionService.IsConnected)
                    {
                        _isWifiConnected = true;
                        LogHelper.LogInformation("WiFi is now detected as connected, starting MQTT...");
                        _wakeSignal.Set();
                        this.Start();
                    }
                    else
                    {
                        LogHelper.LogWarning("WiFi still disconnected after explicit check.");
                    }

                    return;
                }

                if (isRestored)
                {
                    LogHelper.LogInformation("Internet restored.");
                    _wakeSignal.Set();
                    this.Start();
                }
                else
                {
                    LogHelper.LogWarning("Internet lost.");
                    this.SafeDisconnect();
                    _wakeSignal.Set();
                }
            }
        }

        /// <summary>
        /// Handles the loss of connection to the MQTT broker and attempts reconnection.
        /// </summary>
        private void ConnectionClosed(object sender, EventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            LogHelper.LogWarning("Lost connection to MQTT broker, attempting to reconnect...");

            this.SafeDisconnect();
            _wakeSignal.Set();
        }

        private bool EnsureWifiConnected()
        {
            try
            {
                if (_connectionService.IsConnected)
                {
                    return true;
                }

                _connectionService.CheckConnection();
                return _connectionService.IsConnected;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"WiFi check failed: {ex.Message}");
                return false;
            }
        }

        private bool EnsureInternetAvailable()
        {
            try
            {
                return _internetConnectionService.IsInternetAvailable();
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Internet availability check failed: {ex.Message}");
                return false;
            }
        }

        private bool IsMqttReady()
        {
            if (_isDisposed)
            {
                return false;
            }

            var client = _connectionManager.MqttClient;
            return client != null && client.IsConnected;
        }

        private int GetJitter()
        {
            lock (_randomLock)
            {
                return _random.Next(JitterRangeMs) + JitterBaseMs;
            }
        }

        private bool WaitForWake(int timeoutMs)
        {
            if (timeoutMs < 500)
            {
                timeoutMs = 500;
            }

            int signal = WaitHandle.WaitAny(_workerSignals, timeoutMs, false);
            if (signal == 0)
            {
                return true;
            }

            if (signal == 1)
            {
                _wakeSignal.Reset();
            }

            return false;
        }

        private void PublishSensorData()
        {
            if (_isDisposed || !this.GetIsRunning())
            {
                return;
            }

            try
            {
                _mqttPublishService.PublishSensorData();
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Sensor publish exception: {ex.Message}\n{ex}");

                try
                {
                    _mqttPublishService.PublishError($"SensorData Exception: {ex.Message}");
                }
                catch (Exception innerEx)
                {
                    LogHelper.LogError($"Error publishing sensor data error: {innerEx.Message}\n{innerEx}");
                }

                LogService.LogCritical($"SensorData Exception: {ex.Message}\n{ex}");
            }
        }

        /// <summary>
        /// Safely disconnects and disposes of the MQTT client.
        /// </summary>
        private void SafeDisconnect()
        {
            var client = this.MqttClient;
            if (client == null)
            {
                return;
            }

            try
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error while disconnecting MQTT client: {ex.Message}\n{ex}");
                LogService.LogCritical($"Error while disconnecting MQTT client: {ex.Message}\n{ex}");
            }
            finally
            {
                try
                {
                    client.Dispose();
                    LogHelper.LogInformation("MQTT client disconnected and disposed");
                }
                catch (ObjectDisposedException)
                {
                    LogHelper.LogWarning("MQTT client already disposed.");
                }
                catch (Exception ex)
                {
                    LogHelper.LogError($"Error while disposing MQTT client: {ex.Message}\n{ex}");
                    LogService.LogCritical($"Error while disposing MQTT client: {ex.Message}\n{ex}");
                }
                finally
                {
                    lock (_heartbeatLock)
                    {
                        _isHeartbeatRunning = false;
                    }
                    _mqttPublishService.StopHeartbeat();
                    _connectionManager.Disconnect();
                }
            }
        }

        /// <summary>
        /// Throws ObjectDisposedException if the service is disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(MqttClientService));
            }
        }
    }
}