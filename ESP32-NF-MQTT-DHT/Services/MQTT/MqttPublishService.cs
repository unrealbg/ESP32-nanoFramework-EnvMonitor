namespace ESP32_NF_MQTT_DHT.Services.MQTT
{
    using System;
    using System.Text;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Managers.Contracts;
    using ESP32_NF_MQTT_DHT.Services.Contracts;
    using ESP32_NF_MQTT_DHT.Services.MQTT.Contracts;

    using nanoFramework.Json;
    using nanoFramework.M2Mqtt;

    /// <summary>
    /// Service responsible for publishing sensor data and error messages to an MQTT broker.
    /// </summary>
    public class MqttPublishService : IMqttPublishService, IDisposable
    {
        private const int HeartbeatInterval = 30000;
        private readonly ManualResetEvent _heartbeatStopSignal = new ManualResetEvent(false);
        private readonly object _heartbeatLock = new object();
        private readonly object _clientLock = new object();
        private readonly object _runningLock = new object();
        
        private readonly ISensorManager _sensorManager;
        private readonly IInternetConnectionService _internetConnectionService;
        
        private MqttClient _mqttClient;
        private Thread _heartbeatThread;
        
        private bool _isHeartbeatRunning = false;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="MqttPublishService"/> class.
        /// </summary>
        /// <param name="internetConnectionService">The internet connection service for checking internet availability.</param>
        /// <param name="sensorManager">The sensor manager for retrieving sensor data.</param>
        public MqttPublishService(IInternetConnectionService internetConnectionService, ISensorManager sensorManager)
        {
            _internetConnectionService = internetConnectionService ?? throw new ArgumentNullException(nameof(internetConnectionService));
            _sensorManager = sensorManager ?? throw new ArgumentNullException(nameof(sensorManager));
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
                        while (_isHeartbeatRunning && !_disposed)
                        {
                            PublishDeviceStatus();
                            
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
                    this.CheckInternetAndPublish(MqttConstants.DataTopic, message);
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

                this.CheckInternetAndPublish(MqttConstants.ErrorTopic, errorMessage);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error publishing error message: {ex.Message}");
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
                
                LogHelper.LogInformation("MqttPublishService disposed.");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error disposing MqttPublishService: {ex.Message}");
            }
        }

        /// <summary>
        /// Publishes the device status to the MQTT broker.
        /// </summary>
        private void PublishDeviceStatus()
        {
            lock (_runningLock)
            {
                if (_disposed) return;
            }
            
            try
            {
                MqttClient client;
                lock (_clientLock)
                {
                    client = _mqttClient;
                }

                if (client == null)
                {
                    LogHelper.LogWarning("Heartbeat skipped: MQTT client is null.");
                    return;
                }

                if (!client.IsConnected)
                {
                    LogHelper.LogWarning("Heartbeat skipped: MQTT client is not connected.");
                    return;
                }

                string message = "online";
                client.Publish(MqttConstants.SystemStatusTopic, Encoding.UTF8.GetBytes(message));
                LogHelper.LogInformation($"Heartbeat sent: {message}");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error publishing heartbeat: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks internet availability and publishes a message to the specified topic.
        /// </summary>
        /// <param name="topic">The topic to publish the message to.</param>
        /// <param name="message">The message to be published.</param>
        private void CheckInternetAndPublish(string topic, string message)
        {
            lock (_runningLock)
            {
                if (_disposed) return;
            }
            
            try
            {
                MqttClient client;
                lock (_clientLock)
                {
                    client = _mqttClient;
                }

                if (client == null || !client.IsConnected)
                {
                    LogHelper.LogWarning("MQTT client is not connected.");
                    return;
                }

                client.Publish(topic, Encoding.UTF8.GetBytes(message));
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error in CheckInternetAndPublish: {ex.Message}");
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
