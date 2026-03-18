namespace ESP32_NF_MQTT_DHT.Services.Sensors
{
    using System.Device.I2c;

    using Iot.Device.Ahtxx;

    using nanoFramework.Hardware.Esp32;

    /// <summary>
    /// Service for managing the AHT10 sensor.
    /// </summary>
    public class AhtSensorService : BaseSensorService
    {
        private const int DataPin = 22;
        private const int ClockPin = 23;
        private I2cDevice _device;
        private Aht10 _sensor;

        /// <summary>
        /// Gets the type of the sensor.
        /// </summary>
        /// <returns>A string representing the sensor type.</returns>
        public override string GetSensorType() => "AHT10";

        /// <summary>
        /// Starts the sensor service.
        /// </summary>
        public override void Start()
        {
            Configuration.SetPinFunction(DataPin, DeviceFunction.I2C1_DATA);
            Configuration.SetPinFunction(ClockPin, DeviceFunction.I2C1_CLOCK);
            var settings = new I2cConnectionSettings(1, AhtBase.DefaultI2cAddress);
            _device = I2cDevice.Create(settings);
            _sensor = new Aht10(_device);
            base.Start();
        }

        public override void Stop()
        {
            base.Stop();
            _sensor?.Dispose();
            _sensor = null;
            _device?.Dispose();
            _device = null;
        }

        /// <summary>
        /// Reads the sensor data and updates the temperature and humidity values.
        /// </summary>
        protected override void ReadSensorData()
        {
            if (_sensor == null)
            {
                this.SetErrorValues();
                return;
            }

            double temp = _sensor.GetTemperature().DegreesCelsius;
            double humid = _sensor.GetHumidity().Percent;

            if (temp < 45 && temp != -50)
            {
                _temperature = temp;
                _humidity = humid;
            }
            else
            {
                this.SetErrorValues();
            }
        }
    }
}
