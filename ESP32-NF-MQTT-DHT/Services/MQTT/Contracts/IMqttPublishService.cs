namespace ESP32_NF_MQTT_DHT.Services.MQTT.Contracts
{
    using System;

    using nanoFramework.M2Mqtt;

    /// <summary>
    /// Interface for a service that publishes messages to an MQTT broker.
    /// </summary>
    public interface IMqttPublishService
    {
        /// <summary>
        /// Raised when a publish attempt indicates that the MQTT connection is unhealthy.
        /// </summary>
        event EventHandler PublishFailed;

        /// <summary>
        /// Publishes sensor data to the MQTT broker.
        /// </summary>
        void PublishSensorData();

        /// <summary>
        /// Publishes an error message to the MQTT broker.
        /// </summary>
        /// <param name="errorMessage">The error message to be published.</param>
        void PublishError(string errorMessage);

        /// <summary>
        /// Publishes boot and runtime diagnostics after an MQTT connection is initialized.
        /// </summary>
        void PublishRuntimeDiagnostics();

        /// <summary>
        /// Sets the MQTT client to be used for publishing messages.
        /// </summary>
        /// <param name="mqttClient">The MQTT client instance.</param>
        void SetMqttClient(MqttClient mqttClient);

        /// <summary>
        ///  Starts the heartbeat process to publish the device status to the MQTT broker.
        /// </summary>
        void StartHeartbeat();

        /// <summary>
        /// Stops the heartbeat process.
        /// </summary>
        void StopHeartbeat();
    }
}
