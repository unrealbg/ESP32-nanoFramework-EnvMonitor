namespace ESP32_NF_MQTT_DHT.Services
{
    using ESP32_NF_MQTT_DHT.Configuration;
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Managers.Contracts;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    using System;

    using GC = nanoFramework.Runtime.Native.GC;

    /// <summary>
    /// Manages the startup sequence of application services.
    /// </summary>
    public class ServiceStartupManager : IServiceStartupManager
    {
        private readonly ISensorManager _sensorManager;
        private readonly IMqttClientService _mqttClient;
        private readonly IIrcBotService _ircBotService;
        private readonly ITcpListenerService _tcpListenerService;
        private readonly IWebServerService _webServerService;
        private readonly IPlatformService _platformService;

        public ServiceStartupManager(
            ISensorManager sensorManager,
            IMqttClientService mqttClient,
            IIrcBotService ircBotService,
            ITcpListenerService tcpListenerService,
            IWebServerService webServerService,
            IPlatformService platformService)
        {
            _sensorManager = sensorManager;
            _mqttClient = mqttClient;
            _ircBotService = ircBotService;
            _tcpListenerService = tcpListenerService;
            _webServerService = webServerService;
            _platformService = platformService;
        }

        /// <summary>
        /// Starts all core services in the correct order.
        /// </summary>
        public void StartAllServices()
        {
            LogStartupMemory("StartAllServices(begin)");

            if (AppConfiguration.Features.EnableSensorManager)
            {
                this.StartService(Contracts.StartupService.SensorManager);
            }
            else
            {
                LogHelper.LogInformation("Sensor manager disabled – skipping startup.");
            }

            if (AppConfiguration.Features.EnableWebServer)
            {
                this.StartService(Contracts.StartupService.WebServer);
            }
            else
            {
                LogHelper.LogInformation("WebServer disabled – skipping startup.");
            }

            if (AppConfiguration.Features.EnableMqttClient)
            {
                this.StartService(Contracts.StartupService.MqttClient);
            }
            else
            {
                LogHelper.LogInformation("MQTT client disabled – skipping startup.");
            }

            if (AppConfiguration.Features.EnableIrcBot)
            {
                this.StartService(Contracts.StartupService.IrcBot);
            }
            else
            {
                LogHelper.LogInformation("IRC bot disabled – skipping startup.");
            }

            if (AppConfiguration.Features.EnableTcpConsole)
            {
                this.StartService(Contracts.StartupService.TcpListener);
            }
            else
            {
                LogHelper.LogInformation("TCP listener disabled – skipping startup.");
            }

            LogStartupMemory("StartAllServices(end)");
        }

        /// <summary>
        /// Starts a specific service.
        /// </summary>
        /// <param name="service">The service to start.</param>
        public void StartService(ESP32_NF_MQTT_DHT.Services.Contracts.StartupService service)
        {
            switch (service)
            {
                case ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.SensorManager:
                    this.StartSensorManager();
                    break;
                case ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.MqttClient:
                    this.StartMqttClient();
                    break;
                case ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.IrcBot:
                    this.StartIrcBot();
                    break;
                case ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.TcpListener:
                    this.StartTcpListener();
                    break;
                case ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.WebServer:
                    this.StartWebServerIfSupported();
                    break;
                default:
                    LogHelper.LogWarning("Unknown service: " + service);
                    break;
            }
        }

        /// <summary>
        /// Starts a specific service by name (legacy overload).
        /// Prefer <see cref="StartService(ESP32_NF_MQTT_DHT.Services.Contracts.StartupService)"/>.
        /// </summary>
        public void StartService(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
            {
                LogHelper.LogWarning("Unknown service: (null/empty)");
                return;
            }

            if (serviceName == "SensorManager")
            {
                this.StartService(ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.SensorManager);
                return;
            }

            if (serviceName == "MqttClient")
            {
                this.StartService(ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.MqttClient);
                return;
            }

            if (serviceName == "TcpListener")
            {
                this.StartService(ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.TcpListener);
                return;
            }

            if (serviceName == "IrcBot")
            {
                this.StartService(ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.IrcBot);
                return;
            }

            if (serviceName == "WebServer")
            {
                this.StartService(ESP32_NF_MQTT_DHT.Services.Contracts.StartupService.WebServer);
                return;
            }

            LogHelper.LogWarning("Unknown service: " + serviceName);
        }

        /// <summary>
        /// Stops all running services.
        /// </summary>
        public void StopAllServices()
        {
            LogHelper.LogInformation("Stopping all services...");
            _ircBotService.Stop();
            _sensorManager.StopSensor();
        }

        private void StartSensorManager()
        {
            LogStartupMemory("Before SensorManager");
            LogHelper.LogInformation("Starting sensor manager...");
            _sensorManager.StartSensor();
            LogHelper.LogInformation("Sensor manager started.");
            LogStartupMemory("After SensorManager");
        }

        private void StartMqttClient()
        {
            if (!AppConfiguration.Features.EnableMqttClient)
            {
                return;
            }

            LogStartupMemory("Before MQTT");
            LogHelper.LogInformation("Starting MQTT client...");
            _mqttClient.Start();
            LogHelper.LogInformation("MQTT client started.");
            LogStartupMemory("After MQTT");
        }

        private void StartIrcBot()
        {
            if (!AppConfiguration.Features.EnableIrcBot)
            {
                return;
            }

            var ircRequired = AppConfiguration.Platform.IrcBotRequiredMemory;
            var available = GetStartupFreeMemory(ircRequired);
            if (available < ircRequired)
            {
                LogHelper.LogWarning($"Skipping IRC bot startup due to low memory. Required: {ircRequired}, Available: {available}.");
                LogHelper.LogError("[IRC] Startup skipped due to low memory. Required: " + ircRequired + ", Available: " + available + ".");
                return;
            }

            LogStartupMemory("Before IrcBot");
            LogHelper.LogInformation("Starting IRC bot service...");
            LogHelper.LogError("[IRC] Startup requested. Free memory snapshot: " + available + " bytes.");
            _ircBotService.Start();
            LogHelper.LogInformation("IRC bot service start requested.");
            LogHelper.LogError("[IRC] Start() called.");
            LogStartupMemory("After IrcBot");
        }

        private void StartTcpListener()
        {
            if (!AppConfiguration.Features.EnableTcpConsole)
            {
                return;
            }

            var tcpRequired = AppConfiguration.Platform.TcpConsoleRequiredMemory;
            var available = GetStartupFreeMemory(tcpRequired);
            if (available < tcpRequired)
            {
                LogHelper.LogWarning($"Skipping TCPListener startup due to low memory. Required: {tcpRequired}, Available: {available}.");
                return;
            }

            LogStartupMemory("Before TCPListener");
            LogHelper.LogInformation("Starting TCPListener service...");
            _tcpListenerService.Start();
            LogHelper.LogInformation("TCPListener service started.");
            LogStartupMemory("After TCPListener");
        }

        private void StartWebServerIfSupported()
        {
            if (!AppConfiguration.Features.EnableWebServer)
            {
                return;
            }

            LogStartupMemory("Before WebServer checks");
            bool platformSupported = _platformService.SupportsWebServer();
            if (!platformSupported)
            {
                LogHelper.LogWarning($"WebServer service not started. Platform: {_platformService.PlatformName}, PlatformSupported: False");
                return;
            }

            var requiredMemory = ESP32_NF_MQTT_DHT.Configuration.AppConfiguration.Platform.WebServerRequiredMemory;
            var hardMinimumMemory = ESP32_NF_MQTT_DHT.Configuration.AppConfiguration.Platform.StartupRequiredMemory;
            var availableMemory = GetStartupFreeMemory(hardMinimumMemory);
            if (availableMemory < hardMinimumMemory)
            {
                LogHelper.LogWarning($"Insufficient memory for WebServer. Hard minimum: {hardMinimumMemory}, Available: {availableMemory}. Skipping web server startup.");
                return;
            }

            if (availableMemory < requiredMemory)
            {
                LogHelper.LogWarning($"Low memory for WebServer. Required: {requiredMemory}, Available: {availableMemory}. Attempting best-effort startup.");
            }

            LogStartupMemory("Before WebServer.Start()");
            LogHelper.LogInformation("Starting WebServer service...");
            try
            {
                _webServerService.Start();
                LogHelper.LogInformation("WebServer start requested.");
            }
            catch (Exception ex)
            {
                LogHelper.LogError("WebServer failed to start: " + ex.Message);
            }

            LogStartupMemory("After WebServer.Start()");
        }

        private static long GetStartupFreeMemory(long aggressiveThreshold)
        {
            long snapshot = GC.Run(false);
            if (snapshot < aggressiveThreshold)
            {
                snapshot = GC.Run(true);
            }

            return snapshot;
        }

        private static void LogStartupMemory(string stage)
        {
#if DEBUG
            if (!AppConfiguration.Features.EnableStartupMemoryTrace)
            {
                return;
            }

            try
            {
                long free = GC.Run(false);
                LogHelper.LogInformation("[StartupMemory] " + stage + ": " + free + " bytes free");
            }
            catch
            {
                // ignore logging failures
            }
#endif
        }
    }
}
