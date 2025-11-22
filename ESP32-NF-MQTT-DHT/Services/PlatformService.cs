namespace ESP32_NF_MQTT_DHT.Services
{
    using System;

    using ESP32_NF_MQTT_DHT.Configuration;
    using ESP32_NF_MQTT_DHT.Services.Contracts;
    using nanoFramework.Runtime.Native;

    /// <summary>
    /// Provides platform-specific capabilities and checks for ESP32 devices.
    /// </summary>
    public class PlatformService : IPlatformService
    {
        private readonly object _memoryLock = new object();
        private long _lastSampledMemory = -1;
        private DateTime _lastSampleTimestamp = DateTime.MinValue;

        /// <summary>
        /// Gets the target platform name.
        /// </summary>
        public string PlatformName => SystemInfo.TargetName;

        /// <summary>
        /// Checks if the current platform supports web server functionality.
        /// </summary>
        /// <returns>True if web server is supported, otherwise false.</returns>
        public bool SupportsWebServer()
        {
            var platformName = this.PlatformName;
            var requiredMemory = AppConfiguration.Platform.WebServerRequiredMemory;
            var availableMemory = this.GetAvailableMemory();
            
            Helpers.LogHelper.LogInformation($"[WebServer Check] Platform: '{platformName}'");
            Helpers.LogHelper.LogInformation($"[WebServer Check] Memory: {availableMemory} bytes (Required: {requiredMemory})");
            
            bool platformSupported = IsSupportedPlatform(platformName);
            bool memorySupported = this.HasSufficientMemory(requiredMemory);
            
            Helpers.LogHelper.LogInformation($"[WebServer Check] Result - Platform: {platformSupported}, Memory: {memorySupported}");
            
            return platformSupported && memorySupported;
        }
        
        /// <summary>
        /// Checks if the given platform name is in the list of supported platforms.
        /// </summary>
        /// <param name="platformName">The platform name to check.</param>
        /// <returns>True if platform is supported, otherwise false.</returns>
        private bool IsSupportedPlatform(string platformName)
        {
            if (string.IsNullOrEmpty(platformName))
                return false;
            
            if (platformName == AppConfiguration.Platform.SupportedWebServerPlatform)
                return true;
            
            var alternatives = AppConfiguration.Platform.AlternativePlatformNames;
            for (int i = 0; i < alternatives.Length; i++)
            {
                if (platformName == alternatives[i])
                {
                    Helpers.LogHelper.LogInformation($"[WebServer Check] Platform '{platformName}' matched alternative '{alternatives[i]}'");
                    return true;
                }
            }
            
            var upperPlatform = platformName.ToUpper();
            if (upperPlatform.Contains("ESP32") && (upperPlatform.Contains("S3") || upperPlatform.Contains("_S3") || upperPlatform.Contains("-S3")))
            {
                Helpers.LogHelper.LogInformation($"[WebServer Check] Platform '{platformName}' matched via pattern (ESP32*S3)");
                return true;
            }
            
            Helpers.LogHelper.LogWarning($"[WebServer Check] Platform '{platformName}' not recognized as WebServer-capable");
            return false;
        }

        /// <summary>
        /// Gets the available memory on the platform.
        /// </summary>
        /// <returns>Available memory in bytes.</returns>
        public long GetAvailableMemory()
        {
            lock (_memoryLock)
            {
                var age = DateTime.UtcNow - _lastSampleTimestamp;
                if (age.TotalSeconds < 30 && _lastSampledMemory >= 0)
                {
                    return _lastSampledMemory;
                }

                long snapshot = GC.Run(false);
                if (snapshot < AppConfiguration.Platform.StartupRequiredMemory)
                {
                    snapshot = GC.Run(true);
                }

                _lastSampledMemory = snapshot;
                _lastSampleTimestamp = DateTime.UtcNow;
                return _lastSampledMemory;
            }
        }

        /// <summary>
        /// Checks if the platform has sufficient memory for a specific feature.
        /// </summary>
        /// <param name="requiredMemory">Required memory in bytes.</param>
        /// <returns>True if sufficient memory is available, otherwise false.</returns>
        public bool HasSufficientMemory(long requiredMemory)
        {
            return this.GetAvailableMemory() >= requiredMemory;
        }
    }
}