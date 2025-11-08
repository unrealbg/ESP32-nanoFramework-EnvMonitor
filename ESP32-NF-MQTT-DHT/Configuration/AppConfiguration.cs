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
    }
}