namespace ESP32_NF_MQTT_DHT.Services
{
    using System;
    using System.Diagnostics;

    using Helpers;

    using Services.Contracts;

    /// <summary>
    /// Service for managing and retrieving system uptime information.
    /// </summary>
    public class UptimeService : IUptimeService
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UptimeService"/> class.
        /// </summary>
        public UptimeService()
        {
            Stopwatch = new Stopwatch();
            Stopwatch.Start();
        }

        /// <summary>
        /// Gets the stopwatch used to measure the system uptime.
        /// </summary>
        public Stopwatch Stopwatch { get; private set; }

        /// <summary>
        /// Retrieves the current uptime of the system.
        /// </summary>
        /// <returns>
        /// A string representing the duration for which the system has been running.
        /// This duration is typically presented in a human-readable format, such as 
        /// days, hours, minutes, and seconds.
        /// </returns>
        public string GetUptime()
        {
            var elapsed = Stopwatch.Elapsed;
            return $"{elapsed.Days} days, {elapsed.Hours} hours, {elapsed.Minutes} minutes, {elapsed.Seconds} seconds";
        }
    }
}