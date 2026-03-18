namespace ESP32_NF_MQTT_DHT.Services.Sensors
{
    using System.Device.I2c;

    using ESP32_NF_MQTT_DHT.Helpers;

    using Iot.Device.Ahtxx;
    using Iot.Device.Bmxx80;
    using Iot.Device.Bmxx80.FilteringMode;

    using nanoFramework.Hardware.Esp32;

    using static ESP32_NF_MQTT_DHT.Helpers.Constants;

    /// <summary>
    /// Service for managing the AHT20 and BMP280 sensors.
    /// </summary>
    public class Aht20Bmp280SensorService : BaseSensorService
    {
        private const int DataPin = 17;
        private const int ClockPin = 18;
        private double _pressure = InvalidPressure;
        private I2cDevice _aht20Device;
        private I2cDevice _bmp280Device;
        private Aht20 _aht20;
        private Bmp280 _bmp280;

        /// <summary>
        /// Gets the sensor data including temperature, humidity, and pressure.
        /// </summary>
        /// <returns>An array of doubles containing the temperature, humidity, and pressure values.</returns>
        public override double[] GetData() => new[] { _temperature, _humidity, _pressure };

        /// <summary>
        /// Gets the pressure reading from the sensor.
        /// </summary>
        /// <returns>The pressure value recorded by the sensor.</returns>
        public override double GetPressure() => _pressure;

        /// <summary>
        /// Gets the type of the sensor.
        /// </summary>
        /// <returns>A string representing the sensor type.</returns>
        public override string GetSensorType() => "AHT20 + BMP280";

        /// <summary>
        /// Starts the sensor service.
        /// </summary>
        public override void Start()
        {
            Configuration.SetPinFunction(DataPin, DeviceFunction.I2C1_DATA);
            Configuration.SetPinFunction(ClockPin, DeviceFunction.I2C1_CLOCK);

            I2cConnectionSettings settingsAht20 = new I2cConnectionSettings(1, AhtBase.DefaultI2cAddress);
            I2cConnectionSettings settingsBmp280 = new I2cConnectionSettings(1, Bmx280Base.DefaultI2cAddress);

            _aht20Device = I2cDevice.Create(settingsAht20);
            _bmp280Device = I2cDevice.Create(settingsBmp280);
            _aht20 = new Aht20(_aht20Device);
            _bmp280 = new Bmp280(_bmp280Device);
            _bmp280.TemperatureSampling = Sampling.UltraHighResolution;
            _bmp280.PressureSampling = Sampling.UltraHighResolution;
            _bmp280.FilterMode = Bmx280FilteringMode.X4;
            _bmp280.Reset();

            base.Start();
        }

        public override void Stop()
        {
            base.Stop();
            _bmp280?.Dispose();
            _bmp280 = null;
            _aht20?.Dispose();
            _aht20 = null;
            _bmp280Device?.Dispose();
            _bmp280Device = null;
            _aht20Device?.Dispose();
            _aht20Device = null;
        }

        /// <summary>
        /// Reads the sensor data and updates the temperature, humidity, and pressure values.
        /// </summary>
        protected override void ReadSensorData()
        {
            if (_aht20 == null || _bmp280 == null)
            {
                this.SetErrorValues();
                _pressure = InvalidPressure;
                return;
            }

            _temperature = _aht20.GetTemperature().DegreesCelsius;
            _humidity = _aht20.GetHumidity().Percent;

            var bmpReadResult = _bmp280.Read();
            if (bmpReadResult.TemperatureIsValid && bmpReadResult.PressureIsValid)
            {
                _pressure = bmpReadResult.Pressure.Hectopascals;
                LogHelper.LogDebug($"Pressure: {_pressure} hPa");
            }
            else
            {
                _pressure = InvalidPressure;
            }
        }
    }
}
