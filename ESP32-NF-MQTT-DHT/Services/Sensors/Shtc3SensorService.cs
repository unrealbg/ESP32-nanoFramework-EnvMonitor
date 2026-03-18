namespace ESP32_NF_MQTT_DHT.Services.Sensors
{
    using System.Device.I2c;

    using Iot.Device.Shtc3;

    using nanoFramework.Hardware.Esp32;

    /// <summary>
    /// Service for managing the SHTC3 sensor.
    /// </summary>
    internal class Shtc3SensorService : BaseSensorService
    {
        private const int DataPin = 17;
        private const int ClockPin = 18;
        private I2cDevice _device;
        private Shtc3 _sensor;

        /// <summary>
        /// Gets the type of the sensor.
        /// </summary>
        /// <returns>A string representing the sensor type.</returns>
        public override string GetSensorType() => "SHTC3";

        /// <summary>
        /// Starts the sensor service.
        /// </summary>
        public override void Start()
        {
            Configuration.SetPinFunction(DataPin, DeviceFunction.I2C1_DATA);
            Configuration.SetPinFunction(ClockPin, DeviceFunction.I2C1_CLOCK);

            var settings = new I2cConnectionSettings(1, Shtc3.DefaultI2cAddress);
            _device = I2cDevice.Create(settings);
            _sensor = new Shtc3(_device);
            base.Start();
        }

        /// <summary>
        /// Stops the sensor service.
        /// </summary>
        public override void Stop()
        {
            base.Stop();
            _device?.Dispose();
        }

        /// <summary>
        /// Reads the sensor data and updates the temperature and humidity values.
        /// </summary>
        protected override void ReadSensorData()
        {
            if (_sensor.TryGetTemperatureAndHumidity(out var temp, out var humid))
            {
                _temperature = temp.DegreesCelsius;
                _humidity = humid.Percent;
            }
            else
            {
                this.SetErrorValues();
            }
        }
    }
}