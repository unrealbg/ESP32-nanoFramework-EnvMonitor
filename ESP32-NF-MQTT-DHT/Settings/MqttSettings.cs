namespace ESP32_NF_MQTT_DHT.Settings
{
    using ESP32_NF_MQTT_DHT.Helpers;

    public static class MqttSettings
    {
        // Loaded from I:\config\device.config
        // mqtt.broker=...
        // mqtt.port=1883
        // mqtt.tls=false
        // mqtt.user=...
        // mqtt.pass=...
        public static string Broker => DeviceConfig.GetString("mqtt.broker", "mqtt.broker.com");
        public static int Port => DeviceConfig.GetInt32("mqtt.port", 1883); // set to 8883 when tls=true
        public static bool UseTls => DeviceConfig.GetBoolean("mqtt.tls", false);
        public static string ClientUsername => DeviceConfig.GetString("mqtt.user", "user");
        public static string ClientPassword => DeviceConfig.GetString("mqtt.pass", "pass");

        public static string ClientId => DeviceConfig.GetString("mqtt.clientId", DeviceSettings.DeviceName + "-mqtt");
    }
}
