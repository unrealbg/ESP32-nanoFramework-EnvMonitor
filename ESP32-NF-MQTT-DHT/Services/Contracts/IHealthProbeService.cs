namespace ESP32_NF_MQTT_DHT.Services.Contracts
{
    /// <summary>
    /// Provides a lightweight non-MQTT health probe endpoint.
    /// </summary>
    public interface IHealthProbeService
    {
        void Start();

        void Stop();
    }
}
