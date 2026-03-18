namespace ESP32_NF_MQTT_DHT.Services.Contracts
{
    /// <summary>
    /// Defines a contract for a background IRC bot service.
    /// </summary>
    public interface IIrcBotService
    {
        /// <summary>
        /// Starts the IRC bot service.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the IRC bot service.
        /// </summary>
        void Stop();
    }
}
