namespace ESP32_NF_MQTT_DHT.Services.NoOp
{
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    using nanoFramework.M2Mqtt;

    /// <summary>
    /// Lightweight placeholder for the MQTT client when the feature is disabled.
    /// </summary>
    internal sealed class NoOpMqttClientService : IMqttClientService
    {
        public MqttClient MqttClient => null;

        public void Start()
        {
            LogHelper.LogInformation("[FeatureGate] MQTT client disabled.");
        }
    }
}
