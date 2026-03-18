namespace ESP32_NF_MQTT_DHT.Services.Contracts
{
    /// <summary>
    /// Identifiers for services that participate in the startup sequence.
    /// </summary>
    public enum StartupService
    {
        SensorManager = 0,
        MqttClient = 1,
        IrcBot = 2,
        TcpListener = 3,
        WebServer = 4,
    }
}
