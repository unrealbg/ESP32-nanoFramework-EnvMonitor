namespace ESP32_NF_MQTT_DHT
{
    using System;
    using System.Threading;

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
            new Thread(MemoryMonitor).Start();
#endif
            CredentialCache.Load();

            try
            {
                // Set the sensor type to use.
                SensorType sensorType = SensorType.SHTC3;

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
                long totalMemory = GC.Run(false);
                long afterGC = GC.Run(true);
                
                LogHelper.LogInformation($"[MemoryMonitor] Free: {totalMemory} bytes, After GC: {afterGC} bytes, Recovered: {totalMemory - afterGC} bytes");
                
                // Warning if memory is low
                if (afterGC < 30000)
                {
                    LogHelper.LogWarning($"[MemoryMonitor] LOW MEMORY: {afterGC} bytes remaining!");
                }
                else if (afterGC < 50000)
                {
                    LogHelper.LogWarning($"[MemoryMonitor] Memory getting low: {afterGC} bytes");
                }

                Thread.Sleep(60000);
            }
        }
    }
}
