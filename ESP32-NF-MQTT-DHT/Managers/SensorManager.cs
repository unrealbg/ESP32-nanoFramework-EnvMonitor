namespace ESP32_NF_MQTT_DHT.Managers
{
    using System;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Managers.Contracts;
    using ESP32_NF_MQTT_DHT.Models;
    using ESP32_NF_MQTT_DHT.Services.Contracts;
    using ESP32_NF_MQTT_DHT.Settings;

    using nanoFramework.Runtime.Native;

    /// <summary>
    /// Manages sensor operations including data collection and validation.
    /// </summary>
    public class SensorManager : ISensorManager
    {
        private static string _firmwareVersion;
        private readonly ISensorService _sensorService;

        /// <summary>
        /// Initializes a new instance of the <see cref="SensorManager"/> class.
        /// </summary>
        /// <param name="sensorService">The active sensor service.</param>
        public SensorManager(ISensorService sensorService)
        {
            _sensorService = sensorService;
        }

        /// <summary>
        /// Collects sensor data and creates a <see cref="Device"/> object with the collected data.
        /// </summary>
        /// <returns>A <see cref="Device"/> object containing the sensor data, or <c>null</c> if the data is invalid.</returns>
        public Device CollectAndCreateSensorData()
        {
            var temperature = _sensorService.GetTemp();
            var humidity = _sensorService.GetHumidity();
            var sensorType = _sensorService.GetSensorType();

            if (!double.IsNaN(temperature) && !double.IsNaN(humidity))
            {
                LogHelper.LogInformation($"{sensorType} - Temp: {temperature}°C, Humidity: {humidity}%");

                return new Device
                {
                    DeviceName = DeviceSettings.DeviceName,
                    Location = DeviceSettings.Location,
                    SensorType = sensorType,
                    DateTime = DateTime.UtcNow,
                    Temp = Math.Round(temperature * 100) / 100,
                    Humid = (int)humidity,
                    Firmware = GetFirmwareVersion()
                };
            }

            LogHelper.LogWarning("Invalid data from sensors.");
            return null;
        }

        /// <summary>
        /// Starts all sensor services.
        /// </summary>
        public void StartSensor()
        {
            _sensorService.Start();
        }

        /// <summary>
        /// Stops all sensor services.
        /// </summary>
        public void StopSensor()
        {
            _sensorService.Stop();
        }

        private static string GetFirmwareVersion()
        {
            if (!string.IsNullOrEmpty(_firmwareVersion))
            {
                return _firmwareVersion;
            }

            Version firmwareVersion = SystemInfo.Version;
            if (firmwareVersion == null)
            {
                return "unknown";
            }

            _firmwareVersion =
                firmwareVersion.Major + "." +
                firmwareVersion.Minor + "." +
                firmwareVersion.Build + "." +
                firmwareVersion.Revision;

            return _firmwareVersion;
        }
    }
}
