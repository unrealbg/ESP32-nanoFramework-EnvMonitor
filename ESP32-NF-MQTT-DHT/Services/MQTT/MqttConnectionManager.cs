namespace ESP32_NF_MQTT_DHT.Services.MQTT
{
    using System;
    using System.Net.Sockets;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.MQTT.Contracts;

    using nanoFramework.M2Mqtt;

    internal class MqttConnectionManager : IMqttConnectionManager
    {
        /// <summary>
        /// Gets the MQTT client.
        /// </summary>
        public MqttClient MqttClient { get; private set; }

        /// <summary>
        /// Connects to the MQTT broker.
        /// </summary>
        public bool Connect(string broker, int port, bool useTls, string clientId, string user, string pass)
        {
            try
            {
                LogHelper.LogInformation($"Connecting to MQTT broker: {broker}:{port} (TLS: {useTls})...");
                var sslProtocol = useTls ? MqttSslProtocols.TLSv1_2 : MqttSslProtocols.None;
                this.MqttClient = new MqttClient(broker, port, useTls, null, null, sslProtocol);
                this.MqttClient.Connect(clientId, user, pass);

                if (this.MqttClient.IsConnected)
                {
                    LogHelper.LogInformation("MQTT client connected successfully!");
                    return true;
                }
            }
            catch (SocketException ex)
            {
                LogHelper.LogError($"SocketException while connecting: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"{LogMessages.ErrorConnectingToBroker} {ex.Message}");
                LogService.LogCritical($"{LogMessages.ErrorConnectingToBroker} {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Disconnects the MQTT client.
        /// </summary>
        public void Disconnect()
        {
            if (this.MqttClient != null)
            {
                try
                {
                    if (this.MqttClient.IsConnected)
                    {
                        this.MqttClient.Disconnect();
                    }

                    this.MqttClient.Dispose();
                }
                catch (Exception ex)
                {
                    LogHelper.LogError($"Error while disposing MQTT client: {ex.Message}");
                    LogService.LogCritical($"Error while disposing MQTT client: {ex.Message}", ex);
                }
                finally
                {
                    this.MqttClient = null;
                }
            }
        }
    }
}
