namespace ESP32_NF_MQTT_DHT.Services.Sensors
{
    using System;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    using static ESP32_NF_MQTT_DHT.Helpers.Constants;

    /// <summary>
    /// Abstract base class for sensor services.
    /// </summary>
    public abstract class BaseSensorService : ISensorService
    {
        protected double _temperature = InvalidTemperature;
        protected double _humidity = InvalidHumidity;
        protected bool _running;
        protected Timer _readTimer;
        protected int _consecutiveReadFailures;
        protected DateTime _lastSuccessfulReadUtc = DateTime.MinValue;

        /// <summary>
        /// Retrieves the sensor data.
        /// </summary>
        /// <returns>An array of doubles containing the temperature and humidity values.</returns>
        public virtual double[] GetData() => new[] { _temperature, _humidity };

        /// <summary>
        /// Retrieves the temperature reading from the sensor.
        /// </summary>
        /// <returns>The temperature value recorded by the sensor.</returns>
        public virtual double GetTemp() => _temperature;

        /// <summary>
        /// Retrieves the humidity reading from the sensor.
        /// </summary>
        /// <returns>The humidity value recorded by the sensor.</returns>
        public virtual double GetHumidity() => _humidity;

        /// <summary>
        /// Retrieves the pressure reading from the sensor.
        /// </summary>
        /// <returns> The pressure value recorded by the sensor.</returns>
        public virtual double GetPressure()
        {
            // Default implementation returns an invalid value. If the sensor supports pressure readings, this method should be overridden.
            return InvalidPressure;
        }

        /// <summary>
        /// Retrieves the type of the sensor.
        /// </summary>
        /// <returns>A string representing the type of the sensor.</returns>
        public abstract string GetSensorType();

        /// <summary>
        /// Starts the sensor service.
        /// </summary>
        public virtual void Start()
        {
            _running = true;
            _readTimer = new Timer(this.ReadCallback, null, 0, ReadIntervalMs);
        }

        /// <summary>
        /// Stops the sensor service.
        /// </summary>
        public virtual void Stop()
        {
            _running = false;
            _readTimer?.Dispose();
        }

        /// <summary>
        /// Reads the sensor data and updates the temperature and humidity values.
        /// </summary>
        protected abstract void ReadSensorData();

        /// <summary>
        /// Sets error values for temperature and humidity.
        /// </summary>
        protected virtual void SetErrorValues()
        {
            _temperature = InvalidTemperature;
            _humidity = InvalidHumidity;
        }

        /// <summary>
        /// Callback method for reading sensor data.
        /// </summary>
        /// <param name="state">The state object.</param>
        private void ReadCallback(object state)
        {
            if (!_running)
            {
                return;
            }

            try
            {
                this.ReadSensorData();

                bool invalidReading = _temperature == InvalidTemperature ||
                                      _humidity == InvalidHumidity ||
                                      double.IsNaN(_temperature) ||
                                      double.IsNaN(_humidity);

                if (invalidReading)
                {
                    this.RegisterReadFailure("invalid sensor values");
                    return;
                }

                this.RegisterReadSuccess();
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error reading sensor data in {this.GetSensorType()}: {ex.Message}");
                this.SetErrorValues();
                this.RegisterReadFailure(ex.Message);

                _readTimer.Change(ErrorIntervalMs, ReadIntervalMs);
            }
        }

        private void RegisterReadSuccess()
        {
            if (_consecutiveReadFailures > 0)
            {
                LogHelper.LogInformation($"{this.GetSensorType()} recovered after {_consecutiveReadFailures} failed read(s). Last success at {DateTime.UtcNow}.");
            }

            _consecutiveReadFailures = 0;
            _lastSuccessfulReadUtc = DateTime.UtcNow;
        }

        private void RegisterReadFailure(string reason)
        {
            _consecutiveReadFailures++;

            if (_consecutiveReadFailures == 1 || _consecutiveReadFailures % 5 == 0)
            {
                LogHelper.LogWarning($"{this.GetSensorType()} read failure #{_consecutiveReadFailures}. Reason: {reason}. Last successful read: {_lastSuccessfulReadUtc}");
            }
        }
    }
}
