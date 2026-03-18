namespace ESP32_NF_MQTT_DHT.Services.NoOp
{
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    /// <summary>
    /// Lightweight placeholder for the IRC bot when the feature is disabled.
    /// </summary>
    internal sealed class NoOpIrcBotService : IIrcBotService
    {
        public void Start()
        {
            LogHelper.LogInformation("[FeatureGate] IRC bot disabled.");
        }

        public void Stop()
        {
        }
    }
}
