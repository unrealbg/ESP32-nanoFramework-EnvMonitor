namespace ESP32_NF_MQTT_DHT.Settings
{
    using ESP32_NF_MQTT_DHT.Helpers;

    public static class WifiSettings
    {
        // Loaded from I:\config\device.config
        // wifi.ssid=...
        // wifi.password=...
        public static string SSID => DeviceConfig.GetString("wifi.ssid", string.Empty);
        public static string Password => DeviceConfig.GetString("wifi.password", string.Empty);
    }
}