namespace ESP32_NF_MQTT_DHT.Services.NoOp
{
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    using nanoFramework.M2Mqtt;

    /// <summary>
    /// Stub OTA service used when OTA over MQTT is disabled.
    /// </summary>
    internal sealed class NoOpOtaService : IOtaService
    {
        public void SetMqttClient(MqttClient client)
        {
        }

        public void HandleOtaCommand(string payload)
        {
            LogHelper.LogInformation("[FeatureGate] OTA command ignored (feature disabled).");
        }
    }
}
