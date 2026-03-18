namespace ESP32_NF_MQTT_DHT.Services.MQTT
{
    using System;
    using System.Text;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    using nanoFramework.M2Mqtt;
    using nanoFramework.M2Mqtt.Messages;
    using nanoFramework.Runtime.Native;

    /// <summary>
    /// Handles incoming MQTT messages and performs actions based on the message content and topic.
    /// </summary>
    public class MqttMessageHandler
    {
        private readonly IRelayService _relayService;
        private readonly IUptimeService _uptimeService;
        private readonly IConnectionService _connectionService;
        private readonly IOtaService _otaService;
        private MqttClient _mqttClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="MqttMessageHandler"/> class.
        /// </summary>
        /// <param name="relayService">The relay service for controlling relays.</param>
        /// <param name="uptimeService">The uptime service for retrieving system uptime.</param>
        /// <param name="logHelper">The log helper for logging messages.</param>
        /// <param name="connectionService">The connection service for retrieving network information.</param>
        public MqttMessageHandler(IRelayService relayService, IUptimeService uptimeService, IConnectionService connectionService, IOtaService otaService)
        {
            this._relayService = relayService;
            this._uptimeService = uptimeService;
            this._connectionService = connectionService;
            this._otaService = otaService;
        }

        /// <summary>
        /// Sets the MQTT client to be used for handling messages.
        /// </summary>
        /// <param name="mqttClient">The MQTT client instance.</param>
        public void SetMqttClient(MqttClient mqttClient)
        {
            _mqttClient = mqttClient;
            _otaService.SetMqttClient(mqttClient);
        }

        /// <summary>
        /// Handles incoming MQTT messages and performs actions based on the message content and topic.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="MqttMsgPublishEventArgs"/> instance containing the event data.</param>
        public void HandleIncomingMessage(object sender, MqttMsgPublishEventArgs e)
        {
            try
            {
                var message = Encoding.UTF8.GetString(e.Message, 0, e.Message.Length);
                var command = NormalizeMessage(message);

                if (e.Topic == MqttConstants.RelayTopic)
                {
                    if (command == "on")
                    {
                        _relayService.TurnOn();
                        this.Publish(MqttConstants.RelayTopic + "/relay", "ON");
                        LogHelper.LogInformation("Relay turned ON and message published");
                    }
                    else if (command == "off")
                    {
                        _relayService.TurnOff();
                        this.Publish(MqttConstants.RelayTopic + "/relay", "OFF");
                        LogHelper.LogInformation("Relay turned OFF and message published");
                    }
                    else if (command == "status")
                    {
                        string status = _relayService.IsRelayOn() ? "ON" : "OFF";
                        this.Publish(MqttConstants.RelayTopic + "/status", status);
                        LogHelper.LogInformation($"Relay status requested, published: {status}");
                    }
                }
                else if (e.Topic == MqttConstants.SystemTopic)
                {
                    if (command == "uptime")
                    {
                        string uptime = this._uptimeService.GetUptime();
                        this.Publish(MqttConstants.UptimeTopic, uptime);
                        LogHelper.LogInformation($"Uptime requested, published: {uptime}");
                    }
                    else if (command == "reboot")
                    {
                        this.Publish("home/" + Settings.DeviceSettings.DeviceName + "/maintenance", "Manual reboot at: " + DateTime.UtcNow.ToString("HH:mm:ss"));
                        LogHelper.LogInformation("Rebooting system...");
                        Thread.Sleep(2000);
                        Power.RebootDevice();
                    }
                    else if (command == "getip")
                    {
                        string ipAddress = this._connectionService.GetIpAddress();
                        this.Publish(MqttConstants.SystemTopic + "/ip", ipAddress);
                        LogHelper.LogInformation($"IP address requested, published: {ipAddress}");
                    }
                    else if (command == "firmware")
                    {
                        Version firmwareVersion = SystemInfo.Version;
                        string versionString = $"{firmwareVersion.Major}.{firmwareVersion.Minor}.{firmwareVersion.Build}.{firmwareVersion.Revision}";
                        this.Publish(MqttConstants.SystemTopic + "/firmware", versionString);
                        LogHelper.LogInformation($"Firmware version requested, published: {versionString}");
                    }
                    else if (command == "platform")
                    {
                        string platform = SystemInfo.Platform;
                        this.Publish(MqttConstants.SystemTopic + "/platform", platform);
                        LogHelper.LogInformation($"Platform information requested, published: {platform}");
                    }
                    else if (command == "target")
                    {
                        string target = SystemInfo.TargetName;
                        this.Publish(MqttConstants.SystemTopic + "/target", target);
                        LogHelper.LogInformation($"Target information requested, published: {target}");
                    }
                    else if (command == "getlogs")
                    {
                        string logs = LogService.ReadLatestLogs();
                        this.Publish(MqttConstants.SystemTopic + "/logs", logs);
                        LogHelper.LogInformation("Logs requested, published");
                    }
                    else if (command == "clearlogs")
                    {
                        LogService.ClearLogs();
                        this.Publish(MqttConstants.SystemTopic + "/logs", "Logs cleared");
                        LogHelper.LogInformation("Logs cleared");
                    }
                }
                else if (e.Topic == MqttConstants.ErrorTopic)
                {
                    LogHelper.LogError(message);
                }
                else if (e.Topic == ESP32_NF_MQTT_DHT.OTA.Config.TopicCmd)
                {
                    _otaService.HandleOtaCommand(message);
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error handling incoming message: {ex.Message}");
            }
        }

        private void Publish(string topic, string payload)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected || string.IsNullOrEmpty(topic) || payload == null)
            {
                return;
            }

            _mqttClient.Publish(topic, Encoding.UTF8.GetBytes(payload));
        }

        private static string NormalizeMessage(string message)
        {
            return string.IsNullOrEmpty(message) ? string.Empty : message.Trim().ToLower();
        }
    }
}
