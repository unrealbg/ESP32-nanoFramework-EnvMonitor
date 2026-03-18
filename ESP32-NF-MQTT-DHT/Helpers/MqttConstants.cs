namespace ESP32_NF_MQTT_DHT.Helpers
{
    using System;

    public static class MqttConstants
    {
        public static readonly string UptimeTopic = $"home/{Settings.DeviceSettings.DeviceName}/uptime";
        public static readonly string RelayTopic = $"home/{Settings.DeviceSettings.DeviceName}/switch";
        public static readonly string SystemTopic = $"home/{Settings.DeviceSettings.DeviceName}/system";
        public static readonly string SystemStatusTopic = $"home/{Settings.DeviceSettings.DeviceName}/system/status";
        public static readonly string DataTopic = $"home/{Settings.DeviceSettings.DeviceName}/messages";
        public static readonly string ErrorTopic = $"home/{Settings.DeviceSettings.DeviceName}/errors";
    }
}
