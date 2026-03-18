namespace ESP32_NF_MQTT_DHT.Services.NoOp
{
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    /// <summary>
    /// Lightweight placeholder for the TCP listener. Keeps DI graph intact when the feature is disabled.
    /// </summary>
    internal sealed class NoOpTcpListenerService : ITcpListenerService
    {
        public void Start()
        {
            LogHelper.LogInformation("[FeatureGate] TCP listener disabled.");
        }
    }
}
