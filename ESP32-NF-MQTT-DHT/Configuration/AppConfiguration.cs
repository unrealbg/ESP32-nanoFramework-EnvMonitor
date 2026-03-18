namespace ESP32_NF_MQTT_DHT.Configuration
{
    using ESP32_NF_MQTT_DHT.Helpers;

    /// <summary>
    /// Centralized configuration provider for all application settings.
    /// </summary>
    public static class AppConfiguration
    {
        /// <summary>
        /// Device configuration settings.
        /// </summary>
        public static class Device
        {
            public const string Name = "ESP32-S3";
            public const string Location = "Test room";
        }

        /// <summary>
        /// Platform-specific configuration.
        /// </summary>
        public static class Platform
        {
            public const long WebServerRequiredMemory = 45000;
            public const long StartupRequiredMemory = 40000;

            // TCP console costs a thread + socket buffers; keep a slightly higher threshold to avoid starving the heap.
            // If you must run TCP console alongside WebServer + MQTT, you may need a board/firmware with more free RAM.
            public const long TcpConsoleRequiredMemory = 38000;

            // IRC bot keeps one additional worker thread plus a persistent outbound socket.
            public const long IrcBotRequiredMemory = 32000;

            public const string SupportedWebServerPlatform = "ESP32_S3";

            public static readonly string[] AlternativePlatformNames = new string[]
            {
                "ESP32-S3",      // With dash
                "ESP32_S3",      // With underscore (primary)
                "ESP32S3",       // No separator
                "KALUGA_1",      // ESP32-S3 dev board
                "ESP32_S3_ALL"   // Possible variant
            };
        }

        /// <summary>
        /// Network configuration settings.
        /// </summary>
        public static class Network
        {
            public const int DefaultHttpPort = 80;
            public const int DefaultTcpPort = 8080;
            public const int ConnectionTimeoutMs = 30000;
            public const int MaxRetryAttempts = 3;
        }

        /// <summary>
        /// Sensor configuration settings.
        /// </summary>
        public static class Sensors
        {
            public const int ReadIntervalMs = 30000;
            public const double InvalidTemperature = double.NaN;
            public const double InvalidHumidity = double.NaN;
            public const double InvalidPressure = double.NaN;
        }

        /// <summary>
        /// Logging configuration settings.
        /// </summary>
        public static class Logging
        {
            public const int MaxLogEntries = 100;
            public const bool EnableDebugLogging = true;
        }

        /// <summary>
        /// Feature toggles for optional subsystems.
        /// All values are runtime-configurable from <c>I:\config\device.config</c>.
        /// </summary>
        public static class Features
        {
            /// <summary>
            /// Enables the sensor manager background sampling loop.
            /// </summary>
            public static bool EnableSensorManager => DeviceConfig.GetBoolean("feature.sensor.enabled", true);

            /// <summary>
            /// Enables the MQTT client worker and broker connectivity.
            /// </summary>
            public static bool EnableMqttClient => DeviceConfig.GetBoolean("feature.mqtt.enabled", true);

            /// <summary>
            /// Enables the IRC bot client. Runtime connection details still come from <c>device.config</c>
            /// and the bot does not start unless at least <c>irc.server</c> is configured.
            /// </summary>
            public static bool EnableIrcBot => DeviceConfig.GetBoolean("feature.irc.enabled", false);

            /// <summary>
            /// Enables the TCP console listener (command shell).
            /// </summary>
            public static bool EnableTcpConsole => DeviceConfig.GetBoolean("feature.tcp.enabled", false);

            /// <summary>
            /// Enables the embedded web server.
            /// </summary>
            public static bool EnableWebServer => DeviceConfig.GetBoolean("feature.web.enabled", false);

            /// <summary>
            /// Enables verbose runtime memory monitor (DEBUG builds only).
            /// </summary>
            public static bool EnableMemoryMonitor => DeviceConfig.GetBoolean("feature.memoryMonitor.enabled", false);

            /// <summary>
            /// Logs free memory checkpoints during startup (DEBUG builds only).
            /// </summary>
            public static bool EnableStartupMemoryTrace => DeviceConfig.GetBoolean("feature.startupMemoryTrace.enabled", false);

            /// <summary>
            /// Enables OTA handling routed through MQTT.
            /// </summary>
            public static bool EnableOtaOverMqtt => DeviceConfig.GetBoolean("feature.otaMqtt.enabled", false);

            /// <summary>
            /// Enables loading OTA-delivered external modules from storage.
            /// Disabled by default to keep boot time, RAM use, and reflection overhead low.
            /// </summary>
            public static bool EnableDynamicModuleLoading => DeviceConfig.GetBoolean("feature.modules.enabled", false);
        }
    }
}
