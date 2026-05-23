namespace ESP32_NF_MQTT_DHT.Services
{
    using System;
    using ESP32_NF_MQTT_DHT.OTA;
    using ESP32_NF_MQTT_DHT.Services.Contracts;
    using nanoFramework.M2Mqtt;

    /// <summary>
    /// Bridges OTA manager into the app services, using the existing MQTT client for status.
    /// </summary>
    internal sealed class OtaService : IOtaService
    {
        private readonly OtaManager _manager = new OtaManager();
        private MqttClient _mqttClient;

        public void SetMqttClient(MqttClient client)
        {
            _mqttClient = client;
        }

        public void HandleOtaCommand(string payload)
        {
            try
            {
                var url = OtaUtil.ExtractUrl(payload);
                if (string.IsNullOrEmpty(url))
                {
                    this.PublishStatus("ERROR", "No URL in payload");
                    return;
                }

                this.PublishStatus("CHECKING", url);
                _manager.CheckAndUpdateFromUrl(url);
            }
            catch (Exception ex)
            {
                this.PublishStatus("ERROR", ex.Message);
            }
        }

        private void PublishStatus(string state, string msg)
        {
            try
            {
                lock (Helpers.MqttConstants.ClientSyncRoot)
                {
                    if (_mqttClient != null && _mqttClient.IsConnected)
                    {
                        var json = OtaUtil.StatusJson(state, msg);
                        _mqttClient.Publish(ESP32_NF_MQTT_DHT.OTA.Config.TopicStatus, System.Text.Encoding.UTF8.GetBytes(json));
                        return;
                    }
                }

                OtaUtil.SafeStatus(state, msg);
            }
            catch
            {
                // ignore
            }
        }
    }
}
