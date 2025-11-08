namespace ESP32_NF_MQTT_DHT.Helpers
{
    /// <summary>
    /// Application-wide constants for configuration and thresholds.
    /// </summary>
    public static class Constants
    {
        // MQTT Reconnection Settings
        public const int InitialReconnectDelayMs = 5000;
        public const int MaxReconnectDelayMs = 120000;
        public const int MaxTotalAttempts = 50;
        
        // Timing Intervals
        public const int SensorDataIntervalMs = 300000; // 5 minutes
        public const int InternetCheckIntervalMs = 30000; // 30 seconds
        public const int ReadIntervalMs = 60000; // 1 minute
        public const int ErrorIntervalMs = 30000; // 30 seconds
        
        // Jitter for connection attempts (prevents thundering herd)
        public const int JitterBaseMs = 500;
        public const int JitterRangeMs = 1500;
        
        // Power Management
        public const int DeepSleepMinutes = 5;
        
        // Invalid Sensor Values (used to detect sensor failures)
        public const double InvalidTemperature = -50.0;
        public const double InvalidHumidity = -100.0;
        public const double InvalidPressure = -1.0;
        
        // Circuit Breaker Settings
        public const int CircuitBreakerTimeoutMinutes = 2; // Reduced from 5 for faster recovery
        
        // MQTT QoS
        public const byte MqttQoSAtMostOnce = 0;
        public const byte MqttQoSAtLeastOnce = 1;
        public const byte MqttQoSExactlyOnce = 2;
    }
}
