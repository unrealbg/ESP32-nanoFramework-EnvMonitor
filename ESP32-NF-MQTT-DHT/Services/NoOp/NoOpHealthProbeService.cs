namespace ESP32_NF_MQTT_DHT.Services.NoOp
{
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    internal sealed class NoOpHealthProbeService : IHealthProbeService
    {
        public void Start()
        {
            LogHelper.LogInformation("[FeatureGate] Health probe disabled.");
        }

        public void Stop()
        {
        }
    }
}
