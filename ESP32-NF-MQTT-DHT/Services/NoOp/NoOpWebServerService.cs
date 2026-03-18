namespace ESP32_NF_MQTT_DHT.Services.NoOp
{
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    /// <summary>
    /// No-op web server used when the feature flag is disabled or platform lacks resources.
    /// </summary>
    internal sealed class NoOpWebServerService : IWebServerService
    {
        public void Start()
        {
            LogHelper.LogInformation("[FeatureGate] Web server disabled.");
        }
    }
}
