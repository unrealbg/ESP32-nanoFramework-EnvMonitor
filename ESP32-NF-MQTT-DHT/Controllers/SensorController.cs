namespace ESP32_NF_MQTT_DHT.Controllers
{
    using System;
    using System.Net;
    using System.Text;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.HTML;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    using Models;
    using nanoFramework.WebServer;

    /// <summary>
    /// Controller for handling sensor-related HTTP requests.
    /// </summary>
    public class SensorController : BaseController
    {
        private readonly ISensorService _sensorService;
        private readonly IRelayService _relayService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SensorController"/> class.
        /// </summary>
        /// <param name="sensorService">The sensor service for retrieving sensor data.</param>
        /// <param name="relayService">The relay service for controlling relays.</param>
        public SensorController(
            ISensorService sensorService,
            IRelayService relayService)
        {
            _sensorService = sensorService ?? throw new ArgumentNullException(nameof(sensorService));
            _relayService = relayService ?? throw new ArgumentNullException(nameof(relayService));
        }

        /// <summary>
        /// Handles the request for the index page.
        /// </summary>
        /// <param name="e">The web server event arguments.</param>
        [Route("/")]
        [Method("GET")]
        public void Index(WebServerEventArgs e)
        {
            if (!this.IsAuthenticated(e))
            {
                this.SendUnauthorizedResponse(e);
                return;
            }

            try
            {
                string htmlContent = Html.GetIndexContent();
                this.SendResponse(e, htmlContent, "text/html");
            }
            catch (Exception ex)
            {
                this.SendErrorResponse(e, "Unable to load index page.", HttpStatusCode.InternalServerError);
                LogHelper.LogWarning("Failed to load index page.");
            }
        }

        /// <summary>
        /// Handles the request for getting the temperature.
        /// </summary>
        /// <param name="e">The web server event arguments.</param>
        [Route("api/temperature")]
        [Method("GET")]
        public void GetTemperature(WebServerEventArgs e)
        {
            this.HandleRequest(
                e,
                () =>
                    {
                        try
                        {
                            var temperature = this.FetchTemperature();
                            if (this.IsValidTemperature(temperature))
                            {
                                var rounded = (Math.Round(temperature * 100) / 100);
                                var sb = new StringBuilder(32);
                                sb.Append("{\"temperature\":");
                                sb.Append(rounded);
                                sb.Append('}');
                                this.SendResponse(e, sb.ToString(), "application/json");
                            }
                            else
                            {
                                this.SendErrorResponse(e, "Temperature data is out of expected range.", HttpStatusCode.InternalServerError);
                                LogHelper.LogWarning("Temperature data is out of expected range.");
                            }
                        }
                        catch (Exception ex)
                        {
                            this.SendErrorResponse(e, "An unexpected error occurred.", HttpStatusCode.InternalServerError);
                            LogHelper.LogError("An unexpected error occurred.", ex);
                        }
                    },
                "api/temperature");
        }

        /// <summary>
        /// Handles the request for getting the humidity.
        /// </summary>
        /// <param name="e">The web server event arguments.</param>
        [Route("api/humidity")]
        [Method("GET")]
        public void GetHumidity(WebServerEventArgs e)
        {
            this.HandleRequest(
                e,
                () =>
                    {
                        try
                        {
                            var humidity = this.FetchHumidity();
                            if (!double.IsNaN(humidity))
                            {
                                var rounded = (Math.Round(humidity * 10) / 10);
                                var sb = new StringBuilder(28);
                                sb.Append("{\"humidity\":");
                                sb.Append(rounded);
                                sb.Append('}');
                                this.SendResponse(e, sb.ToString(), "application/json");
                            }
                            else
                            {
                                this.SendErrorResponse(e, "Humidity data is unavailable.", HttpStatusCode.InternalServerError);
                                LogHelper.LogWarning("Humidity data is unavailable.");
                            }
                        }
                        catch (Exception ex)
                        {
                            this.SendErrorResponse(e, $"An unexpected error occurred: {ex.Message}", HttpStatusCode.InternalServerError);
                            LogHelper.LogError($"An unexpected error occurred: {ex.Message}");
                        }
                    },
                "api/humidity");
        }

        /// <summary>
        /// Handles the request for getting the sensor data.
        /// </summary>
        /// <param name="e">The web server event arguments.</param>
        [Route("api/data")]
        [Method("GET")]
        public void GetData(WebServerEventArgs e)
        {
            this.HandleRequest(
                e,
                () =>
                    {
                        try
                        {
                            var rawTemperature = this.FetchTemperature();
                            var temperature = (Math.Round(rawTemperature * 100) / 100);
                            var humidity = this.FetchHumidity();
                            var sensorType = _sensorService.GetSensorType();

                            if (!double.IsNaN(temperature) && !double.IsNaN(humidity))
                            {
                                var utcNow = DateTime.UtcNow;
                                var jsonResponse = BuildSensorDataJson(
                                    utcNow,
                                    temperature,
                                    (int)humidity,
                                    sensorType);
                                this.SendResponse(e, jsonResponse, "application/json");
                            }
                            else
                            {
                                this.SendErrorResponse(e, "Sensor data is unavailable.", HttpStatusCode.InternalServerError);
                                LogHelper.LogWarning("Sensor data is unavailable.");
                            }
                        }
                        catch (Exception ex)
                        {
                            this.SendErrorResponse(e, $"An unexpected error occurred: {ex.Message}", HttpStatusCode.InternalServerError);
                            LogHelper.LogError($"An unexpected error occurred: {ex.Message}");
                        }
                    },
                "api/data");
        }

        private static string BuildSensorDataJson(DateTime utcNow, double temperature, int humidity, string sensorType)
        {
            var sb = new StringBuilder(128);

            sb.Append("{\"Data\":{");

            sb.Append("\"Temp\":");
            sb.Append(temperature);

            sb.Append(",\"Humid\":");
            sb.Append(humidity);

            sb.Append(",\"DateTime\":\"");
            AppendIso8601Utc(sb, utcNow);
            sb.Append('"');

            sb.Append(",\"SensorType\":");
            AppendJsonString(sb, sensorType);

            sb.Append("}}");
            return sb.ToString();
        }

        private static void AppendIso8601Utc(StringBuilder sb, DateTime utc)
        {
            Append4(sb, utc.Year);
            sb.Append('-');
            Append2(sb, utc.Month);
            sb.Append('-');
            Append2(sb, utc.Day);
            sb.Append('T');
            Append2(sb, utc.Hour);
            sb.Append(':');
            Append2(sb, utc.Minute);
            sb.Append(':');
            Append2(sb, utc.Second);
            sb.Append('Z');
        }

        private static void Append2(StringBuilder sb, int value)
        {
            sb.Append((char)('0' + ((value / 10) % 10)));
            sb.Append((char)('0' + (value % 10)));
        }

        private static void Append4(StringBuilder sb, int value)
        {
            sb.Append((char)('0' + ((value / 1000) % 10)));
            sb.Append((char)('0' + ((value / 100) % 10)));
            sb.Append((char)('0' + ((value / 10) % 10)));
            sb.Append((char)('0' + (value % 10)));
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"')
                {
                    sb.Append("\\\"");
                }
                else if (c == '\\')
                {
                    sb.Append("\\\\");
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else
                {
                    sb.Append(c);
                }
            }

            sb.Append('"');
        }

        /// <summary>
        /// Handles the request for getting the relay status.
        /// </summary>
        /// <param name="e">The web server event arguments.</param>
        [Route("api/relay-status")]
        [Method("GET")]
        public void GetRelayStatus(WebServerEventArgs e)
        {
            this.HandleRequest(
                e,
                () =>
                    {
                        bool isRelayOn = _relayService.IsRelayOn();
                        var jsonResponse = "{\"isRelayOn\": " + (isRelayOn ? "true" : "false") + "}";
                        this.SendResponse(e, jsonResponse, "application/json");
                    },
                "api/relay-status");
        }

        /// <summary>
        /// Handles the request for toggling the relay.
        /// </summary>
        /// <param name="e">The web server event arguments.</param>
        [Route("api/toggle-relay")]
        [Method("POST")]
        public void ToggleRelay(WebServerEventArgs e)
        {
            this.HandleRequest(
                e,
                () =>
                    {
                        bool isRelayOn = _relayService.IsRelayOn();

                        if (isRelayOn)
                        {
                            _relayService.TurnOff();
                            isRelayOn = false;
                        }
                        else
                        {
                            _relayService.TurnOn();
                            isRelayOn = true;
                        }

                        string jsonResponse = "{\"isRelayOn\": " + (isRelayOn ? "true" : "false") + "}";
                        this.SendResponse(e, jsonResponse, "application/json");
                    },
                "api/toggle-relay");
        }

        /// <summary>
        /// Fetches the temperature from the sensor service.
        /// </summary>
        /// <returns>The temperature value.</returns>
        private double FetchTemperature()
        {
            try
            {
                return _sensorService.GetTemp();
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Failed to fetch temperature.", ex);
                return double.NaN;
            }
        }

        /// <summary>
        /// Fetches the humidity from the sensor service.
        /// </summary>
        /// <returns>The humidity value.</returns>
        private double FetchHumidity()
        {
            try
            {
                return _sensorService.GetHumidity();
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Failed to fetch humidity.", ex);
                return double.NaN;
            }
        }

        /// <summary>
        /// Validates the temperature value.
        /// </summary>
        /// <param name="temperature">The temperature value to validate.</param>
        /// <returns><c>true</c> if the temperature is within the valid range; otherwise, <c>false</c>.</returns>
        private bool IsValidTemperature(double temperature)
        {
            return temperature >= -40 && temperature <= 85;
        }
    }
}