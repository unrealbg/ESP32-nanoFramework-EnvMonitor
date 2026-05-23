namespace ESP32_NF_MQTT_DHT.Services.MQTT
{
    using System;
    using System.Text;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Managers.Contracts;
    using ESP32_NF_MQTT_DHT.Services.Contracts;
    using ESP32_NF_MQTT_DHT.Services.MQTT.Contracts;
    using ESP32_NF_MQTT_DHT.Settings;

    using nanoFramework.Json;
    using nanoFramework.M2Mqtt;
    using nanoFramework.M2Mqtt.Messages;
    using GC = nanoFramework.Runtime.Native.GC;

    using static ESP32_NF_MQTT_DHT.Helpers.Constants;

    /// <summary>
    /// Service responsible for publishing sensor data and error messages to an MQTT broker.
    /// </summary>
    public class MqttPublishService : IMqttPublishService, IDisposable
    {
        private const int HeartbeatInterval = 30000;
        private const int InitialHeartbeatDelayMs = 5000;
        private readonly ManualResetEvent _heartbeatStopSignal = new ManualResetEvent(false);
        private readonly object _heartbeatLock = new object();
        private readonly object _clientLock = new object();
        private readonly object _runningLock = new object();
        private readonly object _publishFailureLock = new object();
        
        private readonly ISensorManager _sensorManager;
        private readonly IInternetConnectionService _internetConnectionService;
        private readonly IConnectionService _connectionService;
        private readonly IUptimeService _uptimeService;
        private readonly ISensorService _sensorService;
        
        private MqttClient _mqttClient;
        private Thread _heartbeatThread;
        
        private bool _isHeartbeatRunning = false;
        private bool _disposed = false;
        private bool _publishFailureReported;

        public event EventHandler PublishFailed;

        /// <summary>
        /// Initializes a new instance of the <see cref="MqttPublishService"/> class.
        /// </summary>
        /// <param name="internetConnectionService">The internet connection service for checking internet availability.</param>
        /// <param name="sensorManager">The sensor manager for retrieving sensor data.</param>
        public MqttPublishService(
            IInternetConnectionService internetConnectionService,
            ISensorManager sensorManager,
            IConnectionService connectionService,
            IUptimeService uptimeService,
            ISensorService sensorService)
        {
            _internetConnectionService = internetConnectionService ?? throw new ArgumentNullException(nameof(internetConnectionService));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _uptimeService = uptimeService ?? throw new ArgumentNullException(nameof(uptimeService));
            _sensorService = sensorService ?? throw new ArgumentNullException(nameof(sensorService));
        }

        /// <summary>
        /// Sets the MQTT client to be used for publishing messages.
        /// </summary>
        /// <param name="mqttClient">The MQTT client instance.</param>
        public void SetMqttClient(MqttClient mqttClient)
        {
            ThrowIfDisposed();
            
            lock (_clientLock)
            {
                _mqttClient = mqttClient;
            }

            lock (_publishFailureLock)
            {
                _publishFailureReported = false;
            }
        }

        /// <summary>
        /// Publishes the device status to the MQTT broker.
        /// </summary>
        public void StartHeartbeat()
        {
            ThrowIfDisposed();
            
            lock (_heartbeatLock)
            {
                if (_isHeartbeatRunning)
                {
                    LogHelper.LogInformation("Heartbeat already running.");
                    return;
                }
                
                _isHeartbeatRunning = true;
                _heartbeatStopSignal.Reset();

                _heartbeatThread = new Thread(() =>
                {
                    try
                    {
                        RuntimeStateTracker.MarkProgress("mqtt:heartbeat-thread-start");

                        if (_heartbeatStopSignal.WaitOne(InitialHeartbeatDelayMs, false))
                        {
                            return;
                        }

                        while (_isHeartbeatRunning && !_disposed)
                        {
                            RuntimeStateTracker.MarkProgress("mqtt:heartbeat-publish-start");
                            this.PublishPeriodicStatus();
                            RuntimeStateTracker.MarkProgress("mqtt:heartbeat-publish-complete");
                            
                            if (_heartbeatStopSignal.WaitOne(HeartbeatInterval, false))
                            {
                                break; // Stop signal received
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.LogError($"Heartbeat thread error: {ex.Message}");
                    }
                    finally
                    {
                        lock (_heartbeatLock)
                        {
                            _isHeartbeatRunning = false;
                        }
                        LogHelper.LogInformation("Heartbeat thread stopped.");
                    }
                });
                
                _heartbeatThread.Start();
                LogHelper.LogInformation("Heartbeat started.");
            }
        }

        /// <summary>
        /// Stops the heartbeat thread.
        /// </summary>
        public void StopHeartbeat()
        {
            if (_disposed) return;
            
            lock (_heartbeatLock)
            {
                if (!_isHeartbeatRunning)
                {
                    return;
                }
                
                _isHeartbeatRunning = false;
                _heartbeatStopSignal.Set();
                
                if (_heartbeatThread != null && _heartbeatThread.IsAlive)
                {
                    if (!_heartbeatThread.Join(5000))
                    {
                        LogHelper.LogWarning("Heartbeat thread did not terminate within timeout.");
                    }
                }
                
                _heartbeatThread = null;
                LogHelper.LogInformation("Heartbeat stopped.");
            }
        }

        /// <summary>
        /// Publishes sensor data to the MQTT broker.
        /// </summary>
        public void PublishSensorData()
        {
            ThrowIfDisposed();
            
            try
            {
                var data = _sensorManager.CollectAndCreateSensorData();

                if (data != null)
                {
                    var message = JsonSerializer.SerializeObject(data);
                    this.TryPublishMessage(MqttConstants.DataTopic, message, MqttQoSLevel.AtLeastOnce, false, true, "sensor data");
                }
                else
                {
                    this.PublishError(LogMessages.GetTimeStamp() + " Unable to read sensor data");
                    LogHelper.LogWarning("Unable to read sensor data");
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error publishing sensor data: {ex.Message}");
                try
                {
                    this.PublishError($"Sensor publish error: {ex.Message}");
                }
                catch (Exception innerEx)
                {
                    LogHelper.LogError($"Error publishing error message: {innerEx.Message}");
                }
            }
        }

        /// <summary>
        /// Publishes an error message to the MQTT broker.
        /// </summary>
        /// <param name="errorMessage">The error message to be published.</param>
        public void PublishError(string errorMessage)
        {
            lock (_runningLock)
            {
                if (_disposed) return;
            }
            
            try
            {
                if (string.IsNullOrEmpty(errorMessage))
                {
                    return;
                }

                this.TryPublishMessage(MqttConstants.ErrorTopic, errorMessage, MqttQoSLevel.AtLeastOnce, false, true, "error message");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error publishing error message: {ex.Message}");
            }
        }

        /// <summary>
        /// Publishes the state that survived the previous boot so short reconnect windows remain diagnosable.
        /// </summary>
        public void PublishRuntimeDiagnostics()
        {
            lock (_runningLock)
            {
                if (_disposed) return;
            }

            try
            {
                string payload = "boot; uptime=" + _uptimeService.GetUptime() +
                                 "; freeMemory=" + GC.Run(false) +
                                 "; previousState=" + RuntimeStateTracker.GetPreviousState() +
                                 "; currentState=" + RuntimeStateTracker.GetLastState();

                if (this.TryPublishMessage(
                    MqttConstants.RuntimeStatusTopic,
                    payload,
                    MqttConstants.StatusQoS,
                    true,
                    false,
                    "runtime diagnostics"))
                {
                    LogHelper.LogInformation("Runtime diagnostics published.");
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error publishing runtime diagnostics: " + ex.Message);
            }
        }

        /// <summary>
        /// Disposes resources used by the service.
        /// </summary>
        public void Dispose()
        {
            lock (_runningLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            try
            {
                StopHeartbeat();

                if (_heartbeatStopSignal != null)
                {
                    _heartbeatStopSignal.Set();
                }
                
                lock (_clientLock)
                {
                    _mqttClient = null;
                }

                lock (_publishFailureLock)
                {
                    PublishFailed = null;
                }
                
                LogHelper.LogInformation("MqttPublishService disposed.");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error disposing MqttPublishService: {ex.Message}");
            }
        }

        /// <summary>
        /// Publishes device health and subsystem statuses to the MQTT broker.
        /// </summary>
        private void PublishPeriodicStatus()
        {
            lock (_runningLock)
            {
                if (_disposed) return;
            }
            
            try
            {
                if (!this.TryPublishMessage(MqttConstants.SystemStatusTopic, MqttConstants.OnlineStatusPayload, MqttConstants.StatusQoS, true, true, "system status"))
                {
                    return;
                }

                if (!this.TryPublishMessage(MqttConstants.MqttStatusTopic, this.BuildMqttStatusPayload(), MqttConstants.StatusQoS, true, true, "mqtt status"))
                {
                    return;
                }

                if (!this.TryPublishMessage(MqttConstants.WifiStatusTopic, this.BuildWifiStatusPayload(), MqttConstants.StatusQoS, true, true, "wifi status"))
                {
                    return;
                }

                if (!this.TryPublishMessage(MqttConstants.SensorStatusTopic, this.BuildSensorStatusPayload(), MqttConstants.StatusQoS, true, true, "sensor status"))
                {
                    return;
                }

                if (this.TryPublishMessage(MqttConstants.HeartbeatTopic, this.BuildHeartbeatPayload(), MqttConstants.StatusQoS, false, true, "heartbeat"))
                {
                    LogHelper.LogInformation("Heartbeat sent.");
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error publishing heartbeat: {ex.Message}");
            }
        }

        /// <summary>
        /// Publishes a message to the specified topic and reports failures as MQTT health issues.
        /// </summary>
        private bool TryPublishMessage(string topic, string message, MqttQoSLevel qosLevel, bool retain, bool raiseFailureOnError, string context)
        {
            lock (_runningLock)
            {
                if (_disposed) return false;
            }
            
            try
            {
                MqttClient client;
                lock (_clientLock)
                {
                    client = _mqttClient;
                }

                lock (MqttConstants.ClientSyncRoot)
                {
                    if (client == null || !client.IsConnected)
                    {
                        LogHelper.LogWarning("MQTT publish skipped because the client is not connected. Context: " + context);
                        if (raiseFailureOnError)
                        {
                            this.ReportPublishFailure();
                        }

                        return false;
                    }

                    client.Publish(topic, Encoding.UTF8.GetBytes(message), null, null, qosLevel, retain);
                }

                lock (_publishFailureLock)
                {
                    _publishFailureReported = false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error publishing {context}: {ex.Message}");
                if (raiseFailureOnError)
                {
                    this.ReportPublishFailure();
                }

                return false;
            }
        }

        private string BuildHeartbeatPayload()
        {
            string wifiState = _connectionService.IsConnected ? "connected" : "disconnected";
            string sensorState = this.IsSensorReadingValid(_sensorService.GetTemp(), _sensorService.GetHumidity()) ? "ok" : "invalid";

            return "alive; uptime=" + _uptimeService.GetUptime() +
                   "; freeMemory=" + GC.Run(false) +
                   "; wifi=" + wifiState +
                   "; mqtt=connected" +
                   "; sensor=" + sensorState;
        }

        private string BuildWifiStatusPayload()
        {
            if (!_connectionService.IsConnected)
            {
                return "disconnected";
            }

            return "connected; ip=" + _connectionService.GetIpAddress();
        }

        private string BuildMqttStatusPayload()
        {
            return "connected; clientId=" + Settings.MqttSettings.ClientId;
        }

        private string BuildSensorStatusPayload()
        {
            double temperature = _sensorService.GetTemp();
            double humidity = _sensorService.GetHumidity();
            string sensorType = _sensorService.GetSensorType();

            if (!this.IsSensorReadingValid(temperature, humidity))
            {
                return "invalid; type=" + sensorType;
            }

            double roundedTemp = Math.Round(temperature * 100) / 100;
            double roundedHumidity = Math.Round(humidity * 100) / 100;
            return "ok; type=" + sensorType + "; temp=" + roundedTemp + "; humidity=" + roundedHumidity;
        }

        private bool IsSensorReadingValid(double temperature, double humidity)
        {
            return !double.IsNaN(temperature) &&
                   !double.IsNaN(humidity) &&
                   temperature != InvalidTemperature &&
                   humidity != InvalidHumidity;
        }

        private void ReportPublishFailure()
        {
            EventHandler handler = null;

            lock (_publishFailureLock)
            {
                if (_publishFailureReported || _disposed)
                {
                    return;
                }

                _publishFailureReported = true;
                handler = PublishFailed;
            }

            try
            {
                handler?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error raising PublishFailed event: " + ex.Message);
            }
        }

        private void ThrowIfDisposed()
        {
            lock (_runningLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(MqttPublishService));
                }
            }
        }
    }
}
