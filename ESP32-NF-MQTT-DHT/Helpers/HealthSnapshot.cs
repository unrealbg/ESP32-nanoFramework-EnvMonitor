namespace ESP32_NF_MQTT_DHT.Helpers
{
    using System;
    using System.Text;

    using ESP32_NF_MQTT_DHT.Services.Contracts;

    using nanoFramework.M2Mqtt;

    using static Settings.DeviceSettings;

    using GC = nanoFramework.Runtime.Native.GC;

    public static class HealthSnapshot
    {
        public static string BuildLine(
            IUptimeService uptimeService,
            IConnectionService connectionService,
            IMqttClientService mqttClientService)
        {
            var sb = new StringBuilder(384);

            sb.Append("ok");
            sb.Append("; device=");
            sb.Append(DeviceName);
            sb.Append("; utc=");
            sb.Append(DateTime.UtcNow.ToString("u"));
            sb.Append("; uptime=");
            sb.Append(SafeUptime(uptimeService));
            sb.Append("; ip=");
            sb.Append(SafeIp(connectionService));
            sb.Append("; wifi=");
            sb.Append(SafeWifiState(connectionService));
            sb.Append("; mqtt=");
            sb.Append(SafeMqttState(mqttClientService));
            sb.Append("; freeMemory=");
            sb.Append(GC.Run(false));
            sb.Append("; state=");
            sb.Append(RuntimeStateTracker.GetLastState());
            sb.Append("; watchdog=");
            sb.Append(RuntimeStateTracker.GetWatchdogState());
            sb.Append("; mqttPublish=");
            sb.Append(RuntimeStateTracker.GetMqttPublishState());

            return sb.ToString();
        }

        private static string SafeUptime(IUptimeService uptimeService)
        {
            try
            {
                return uptimeService == null ? "unknown" : uptimeService.GetUptime();
            }
            catch (Exception ex)
            {
                return "error:" + ex.Message;
            }
        }

        private static string SafeIp(IConnectionService connectionService)
        {
            try
            {
                return connectionService == null ? "unknown" : connectionService.GetIpAddress();
            }
            catch (Exception ex)
            {
                return "error:" + ex.Message;
            }
        }

        private static string SafeWifiState(IConnectionService connectionService)
        {
            try
            {
                return connectionService != null && connectionService.IsConnected ? "connected" : "disconnected";
            }
            catch (Exception ex)
            {
                return "error:" + ex.Message;
            }
        }

        private static string SafeMqttState(IMqttClientService mqttClientService)
        {
            try
            {
                if (mqttClientService == null)
                {
                    return "not-configured";
                }

                MqttClient client = mqttClientService.MqttClient;
                if (client == null)
                {
                    return "not-initialized";
                }

                return client.IsConnected ? "connected" : "disconnected";
            }
            catch (Exception ex)
            {
                return "error:" + ex.Message;
            }
        }
    }
}
