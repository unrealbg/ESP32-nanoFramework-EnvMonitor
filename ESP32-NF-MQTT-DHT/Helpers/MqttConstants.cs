namespace ESP32_NF_MQTT_DHT.Helpers
{
    using System;
    using nanoFramework.M2Mqtt.Messages;

    public static class MqttConstants
    {
        public static readonly object ClientSyncRoot = new object();

        public static readonly string UptimeTopic = $"home/{Settings.DeviceSettings.DeviceName}/uptime";
        public static readonly string RelayTopic = $"home/{Settings.DeviceSettings.DeviceName}/switch";
        public static readonly string SystemTopic = $"home/{Settings.DeviceSettings.DeviceName}/system";
        public static readonly string SystemStatusTopic = $"home/{Settings.DeviceSettings.DeviceName}/system/status";
        public static readonly string HeartbeatTopic = $"home/{Settings.DeviceSettings.DeviceName}/system/status/heartbeat";
        public static readonly string WifiStatusTopic = $"home/{Settings.DeviceSettings.DeviceName}/system/status/wifi";
        public static readonly string MqttStatusTopic = $"home/{Settings.DeviceSettings.DeviceName}/system/status/mqtt";
        public static readonly string SensorStatusTopic = $"home/{Settings.DeviceSettings.DeviceName}/system/status/sensor";
        public static readonly string RuntimeStatusTopic = $"home/{Settings.DeviceSettings.DeviceName}/system/status/runtime";
        public static readonly string DataTopic = $"home/{Settings.DeviceSettings.DeviceName}/messages";
        public static readonly string ErrorTopic = $"home/{Settings.DeviceSettings.DeviceName}/errors";

        public const string OnlineStatusPayload = "online";
        public const string OfflineStatusPayload = "offline";
        public const MqttQoSLevel StatusQoS = MqttQoSLevel.AtLeastOnce;
    }
}
