namespace ESP32_NF_MQTT_DHT
{
    using System;

    using ESP32_NF_MQTT_DHT.Configuration;
    using ESP32_NF_MQTT_DHT.Exceptions;
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Modules;
    using ESP32_NF_MQTT_DHT.Modules.Contracts;
    using ESP32_NF_MQTT_DHT.OTA;
    using ESP32_NF_MQTT_DHT.Services;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    /// <summary>
    /// Represents the startup process of the application.
    /// </summary>
    public class Startup
    {
        private static System.Security.Cryptography.X509Certificates.X509Certificate _otaRootCaCert;
        private readonly IConnectionService _connectionService;
        private readonly IServiceStartupManager _serviceStartupManager;
        private readonly IPlatformService _platformService;
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Loads the OTA server root CA certificate for TLS.
        /// </summary>
        private static void LoadOtaRootCa()
        {
            try
            {
                if (TryLoadCertificateFromDevice())
                {
                    return;
                }

                if (TryLoadCertificateFromPem(Settings.OtaCertificates.RootCaPem, "embedded PEM", out _otaRootCaCert))
                {
                    return;
                }

                LogHelper.LogWarning("Root CA PEM file not found on device and no embedded PEM present. Place it at I:\\ota_root_ca.pem.");
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Failed to load OTA root CA: " + ex.Message);
            }
        }

        private static bool TryLoadCertificateFromDevice()
        {
            string[] candidates = new string[]
            {
                @"I:\\ota_root_ca.pem",
                @"I:\\Settings\\ota_root_ca.pem"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                string path = candidates[i];
                try
                {
                    if (!System.IO.File.Exists(path))
                    {
                        continue;
                    }

                    string pem = System.IO.File.ReadAllText(path);
                    if (TryLoadCertificateFromPem(pem, path, out _otaRootCaCert))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogWarning($"Failed to read '{path}': {ex.Message}");
                }
            }

            return false;
        }

        private static bool TryLoadCertificateFromPem(string pemText, string sourceLabel, out System.Security.Cryptography.X509Certificates.X509Certificate selected)
        {
            selected = null;

            if (string.IsNullOrEmpty(pemText))
            {
                return false;
            }

            const string begin = "-----BEGIN CERTIFICATE-----";
            const string end = "-----END CERTIFICATE-----";

            int idx = 0;
            int total = 0;

            while (true)
            {
                int start = pemText.IndexOf(begin, idx);
                if (start < 0)
                {
                    break;
                }

                int stop = pemText.IndexOf(end, start);
                if (stop < 0)
                {
                    break;
                }

                stop += end.Length;
                string block = pemText.Substring(start, stop - start);
                total++;

                try
                {
                    var candidate = new System.Security.Cryptography.X509Certificates.X509Certificate(block);
                    if (IsSelfSigned(candidate))
                    {
                        selected = candidate;
                        break;
                    }

                    if (selected == null)
                    {
                        selected = candidate;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogWarning($"Failed to parse certificate #{total} from {sourceLabel}: {ex.Message}");
                }

                idx = stop;
            }

            if (selected != null)
            {
                LogHelper.LogInformation($"TLS CA prepared from '{sourceLabel}'. Found {total} cert(s); using: '{selected.Subject}' issued by '{selected.Issuer}'.");
            }
            else
            {
                LogHelper.LogWarning($"No usable certificate found in {sourceLabel}. HTTPS may fail.");
            }

            return selected != null;
        }

        private static bool IsSelfSigned(System.Security.Cryptography.X509Certificates.X509Certificate certificate)
        {
            try
            {
                return certificate.Issuer == certificate.Subject;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the prepared OTA root CA certificate (may be null).
        /// </summary>
        public static System.Security.Cryptography.X509Certificates.X509Certificate OtaRootCaCert => _otaRootCaCert;

        public Startup(
            IConnectionService connectionService,
            IServiceStartupManager serviceStartupManager,
            IPlatformService platformService,
            IServiceProvider serviceProvider)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _serviceStartupManager = serviceStartupManager ?? throw new ArgumentNullException(nameof(serviceStartupManager));
            _platformService = platformService ?? throw new ArgumentNullException(nameof(platformService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            LogHelper.LogInformation("Initializing application...");
            this.LogPlatformInfo();
        }

        public void Run()
        {
            try
            {
                if (AppConfiguration.Features.EnableOtaOverMqtt || AppConfiguration.Features.EnableDynamicModuleLoading)
                {
                    // Load OTA root CA for TLS only when OTA/module loading is enabled.
                    LoadOtaRootCa();
                }

                this.ValidateSystemRequirements();
                this.EstablishConnection();
                this.StartServices();
                this.StartOptionalModules();
                
                LogHelper.LogInformation("Application startup completed successfully.");
            }
            catch (InsufficientMemoryException memEx)
            {
                LogHelper.LogError($"Memory validation failed: {memEx.Message}");
                LogService.LogCritical("Insufficient memory for startup", memEx);
                throw;
            }
            catch (ServiceStartupException serviceEx)
            {
                LogHelper.LogError($"Service startup failed: {serviceEx.Message}");
                LogService.LogCritical($"Failed to start service: {serviceEx.ServiceName}", serviceEx);
                throw;
            }
            catch (System.Exception ex)
            {
                LogHelper.LogError($"Unexpected startup failure: {ex.Message}");
                LogService.LogCritical("Critical error during startup", ex);
                throw;
            }
        }

        private void ValidateSystemRequirements()
        {
            LogHelper.LogInformation("Validating system requirements...");
            
            var availableMemory = _platformService.GetAvailableMemory();
            var requiredMemory = StartupConfiguration.RequiredMemory;
            
            if (!_platformService.HasSufficientMemory(requiredMemory))
            {
                if (availableMemory < 30000)
                {
                    throw new InsufficientMemoryException(requiredMemory, availableMemory);
                }
                else
                {
                    LogHelper.LogWarning($"Memory below recommended level but sufficient to continue. Memory: {availableMemory}/{requiredMemory} bytes");
                }
            }
            else
            {
                LogHelper.LogInformation($"System requirements validated. Memory: {availableMemory}/{requiredMemory} bytes");
            }
        }

        private void EstablishConnection()
        {
            LogHelper.LogInformation("Establishing connection...");
            
            try
            {
                _connectionService.Connect();
                if (!_connectionService.IsConnected)
                {
                    throw new ServiceStartupException(
                        "ConnectionService",
                        "Network connection was not established. Ensure Wi-Fi is configured (wifi.ssid/wifi.password) and reachable.");
                }

                LogHelper.LogInformation("Connection established successfully.");
            }
            catch (Exception ex)
            {
                throw new ServiceStartupException("ConnectionService", $"Failed to establish connection: {ex.Message}", ex);
            }
        }

        private void StartServices()
        {
            LogHelper.LogInformation("Starting application services...");
            
            try
            {
                _serviceStartupManager.StartAllServices();
                LogHelper.LogInformation("All application services started successfully.");
            }
            catch (Exception ex)
            {
                throw new ServiceStartupException("ServiceStartupManager", $"Failed to start services: {ex.Message}", ex);
            }
        }

        private void StartOptionalModules()
        {
            bool enableOtaModule = AppConfiguration.Features.EnableOtaOverMqtt;
            bool enableDynamicModules = AppConfiguration.Features.EnableDynamicModuleLoading;

            if (!enableOtaModule && !enableDynamicModules)
            {
                LogHelper.LogInformation("Optional module loading disabled - skipping module startup.");
                return;
            }

            var moduleManager = _serviceProvider.GetService(typeof(IModuleManager)) as IModuleManager;
            if (moduleManager == null)
            {
                LogHelper.LogWarning("ModuleManager is unavailable - skipping optional modules.");
                return;
            }

            if (enableOtaModule)
            {
                var otaModule = _serviceProvider.GetService(typeof(OtaModule)) as OtaModule;
                if (otaModule != null)
                {
                    moduleManager.Register(otaModule);
                }
            }

            if (enableDynamicModules)
            {
                try
                {
                    int count = moduleManager.LoadFromDirectory(Config.ModulesDir);
                    LogHelper.LogInformation("Discovered and registered OTA modules: " + count);
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("Module discovery failed: " + ex.Message);
                }
            }

            moduleManager.StartAll();
        }

        private void LogPlatformInfo()
        {
            var platformName = _platformService.PlatformName;
            var availableMemory = _platformService.GetAvailableMemory();
            var supportsWebServer = _platformService.SupportsWebServer();
            
            LogHelper.LogInformation("=== Platform Information ===");
            LogHelper.LogInformation($"Platform Name: '{platformName}'");
            LogHelper.LogInformation($"Platform Version: {nanoFramework.Runtime.Native.SystemInfo.Version}");
            LogHelper.LogInformation($"Available Memory: {availableMemory} bytes ({availableMemory / 1024} KB)");
            LogHelper.LogInformation($"WebServer Support: {supportsWebServer}");
            LogHelper.LogInformation($"Expected Platform for WebServer: '{AppConfiguration.Platform.SupportedWebServerPlatform}'");
            LogHelper.LogInformation($"Required Memory for WebServer: {AppConfiguration.Platform.WebServerRequiredMemory} bytes");
            LogHelper.LogInformation("===========================");
        }
    }
}
