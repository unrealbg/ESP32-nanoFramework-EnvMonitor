namespace ESP32_NF_MQTT_DHT
{
    using System;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Configuration;
    using ESP32_NF_MQTT_DHT.Extensions;
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services;

    using Microsoft.Extensions.DependencyInjection;

    using GC = nanoFramework.Runtime.Native.GC;

    /// <summary>
    /// Main program class.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main entry point for the application.
        /// </summary>
        public static void Main()
        {
#if DEBUG
            if (AppConfiguration.Features.EnableMemoryMonitor)
            {
                new Thread(MemoryMonitor).Start();
            }
#endif
            CredentialCache.Load();
            DeviceConfig.Load();

            try
            {
                // Select sensor type from device config to avoid firmware rebuilds.
                SensorType sensorType = GetConfiguredSensorType();

                var services = ConfigureServices(sensorType);
                var application = services.GetService(typeof(Startup)) as Startup;

                application?.Run();
            }
            catch (Exception ex)
            {
                LogHelper.LogError("An error occurred: " + ex.Message);
                LogService.LogCritical("Critical error in Main", ex);
            }
        }

        private static SensorType GetConfiguredSensorType()
        {
            // device.config: sensor.type=DHT21|AHT10|SHTC3
            // Default is SHTC3 (matches current project usage)
            string configured = DeviceConfig.GetString("sensor.type", "SHTC3");

            if (string.IsNullOrEmpty(configured))
            {
                return SensorType.SHTC3;
            }

            // Keep parsing simple/cheap for nanoFramework
            if (configured == "DHT21" || configured == "Dht21" || configured == "dht21")
            {
                return SensorType.DHT21;
            }

            if (configured == "AHT10" || configured == "Aht10" || configured == "aht10")
            {
                return SensorType.AHT10;
            }

            if (configured == "SHTC3" || configured == "Shtc3" || configured == "shtc3")
            {
                return SensorType.SHTC3;
            }

            LogHelper.LogWarning("Unknown sensor.type in device.config: '" + configured + "'. Falling back to SHTC3.");
            return SensorType.SHTC3;
        }

        /// <summary>
        /// Configures services for the application.
        /// </summary>
        /// <returns>Configured service provider.</returns>
        private static ServiceProvider ConfigureServices(SensorType sensorType)
        {
            var services = new ServiceCollection();

            services.AddSingleton(typeof(Startup));
            services.AddCoreServices();
            services.AddSensorServices(sensorType);

            var serviceProvider = services.BuildServiceProvider();
            return serviceProvider;
        }

        private static void MemoryMonitor()
        {
            while (true)
            {
                long snapshot = GC.Run(false);
                long reclaimed = 0;

                if (snapshot < AppConfiguration.Platform.StartupRequiredMemory)
                {
                    long afterGc = GC.Run(true);
                    reclaimed = snapshot - afterGc;
                    snapshot = afterGc;
                }

                LogHelper.LogInformation($"[MemoryMonitor] Free: {snapshot} bytes, Reclaimed: {reclaimed} bytes");

                if (snapshot < 30000)
                {
                    LogHelper.LogWarning($"[MemoryMonitor] LOW MEMORY: {snapshot} bytes remaining!");
                }
                else if (snapshot < 50000)
                {
                    LogHelper.LogWarning($"[MemoryMonitor] Memory getting low: {snapshot} bytes");
                }

                Thread.Sleep(90000);
            }
        }
    }
}
