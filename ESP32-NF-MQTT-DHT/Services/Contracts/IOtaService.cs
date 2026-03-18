namespace ESP32_NF_MQTT_DHT.Services.Contracts
{
    using nanoFramework.M2Mqtt;

    /// <summary>
    /// OTA service contract for handling OTA commands and optional background checks.
    /// </summary>
    public interface IOtaService
    {
        /// <summary>
        /// Provides the active MQTT client for status publishing when OTA is enabled.
        /// </summary>
        void SetMqttClient(MqttClient client);

        /// <summary>
        /// Handle an OTA command payload. Supports raw URL or JSON with {"url": "..."}.
        /// </summary>
        /// <param name="payload">MQTT message payload.</param>
        void HandleOtaCommand(string payload);
    }
}
