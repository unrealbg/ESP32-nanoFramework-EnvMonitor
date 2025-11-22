namespace ESP32_NF_MQTT_DHT.Configuration
{
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
            
            public const string SupportedWebServerPlatform = "ESP32_S3";
            
            public static readonly string[] AlternativePlatformNames = new string[]
            {
                "ESP32-S3",      // With dash
                "ESP32_S3",      // With underscore (primary) ✅ CONFIRMED from logs
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
        /// Feature toggles for optional subsystems. Tune these per deployment profile before flashing the firmware.
        /// Small ESP32 variants (C3, original WROOM) should keep most of them disabled to conserve RAM and threads.
        /// </summary>
        public static class Features
        {
            /// <summary>
            /// Enables the TCP console listener (command shell). Each active sesssion consumes ~8 KB and one thread,
            /// so keep this false on unattended or battery powered nodes.
            /// </summary>
            public const bool EnableTcpConsole = false;

            /// <summary>
            /// Enables the embedded web server. Requires sufficient RAM and platform support; only enable when
            /// `PlatformService.SupportsWebServer()` reports true during boot logs.
            /// </summary>
            public const bool EnableWebServer = true;

            /// <summary>
            /// Enables verbose runtime memory monitor (DEBUG builds only). Useful while tuning memory pressure, but
            /// should remain disabled for production firmware to avoid extra GC churn and log noise.
            /// </summary>
            public const bool EnableMemoryMonitor = false;

            /// <summary>
            /// Enables OTA handling routed through MQTT (modules stay registered but commands are ignored when false).
            /// If OTA is delivered exclusively over TCP or USB, you can disable this to reduce attack surface.
            /// </summary>
            public const bool EnableOtaOverMqtt = true;
        }
    }
}