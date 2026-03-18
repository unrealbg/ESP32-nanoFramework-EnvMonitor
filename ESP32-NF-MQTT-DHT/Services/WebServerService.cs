namespace ESP32_NF_MQTT_DHT.Services
{
    using System;

    using Contracts;

    using Controllers;

    using Helpers;

    using nanoFramework.Runtime.Native;
    using nanoFramework.WebServer;

    /// <summary>
    /// Provides services for managing the web server.
    /// </summary>
    public class WebServerService : IWebServerService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnectionService _connectionService;
        private WebServer _server;
        private bool _isServerRunning = false;
        private readonly object _syncLock = new object();
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebServerService"/> class.
        /// </summary>
        /// <param name="connectionService">The connection service.</param>
        /// <param name="serviceProvider">The service provider.</param>
        public WebServerService(IConnectionService connectionService, IServiceProvider serviceProvider)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _serviceProvider = serviceProvider;

            _connectionService.ConnectionRestored += this.ConnectionRestored;
            _connectionService.ConnectionLost += this.ConnectionLost;
        }

        /// <summary>
        /// Starts the web server.
        /// </summary>
        public void Start()
        {
            if (_disposed)
            {
                LogHelper.LogWarning("Cannot start disposed WebServerService");
                return;
            }

            lock (_syncLock)
            {
                if (_isServerRunning)
                {
                    LogHelper.LogInformation("Web server is already running.");
                    return;
                }

                if (!_connectionService.IsConnected)
                {
                    LogHelper.LogWarning("Web server start skipped: no active network connection.");
                    return;
                }

                // Platform check
                if (SystemInfo.TargetName != "ESP32_S3")
                {
                    LogHelper.LogWarning("Web server is supported only on ESP32_S3. Current platform: " + SystemInfo.TargetName);
                    return;
                }

                long availableMemory = nanoFramework.Runtime.Native.GC.Run(true);
                const long MinMemoryForWebServer = 40000;
                
                if (availableMemory < MinMemoryForWebServer)
                {
                    LogHelper.LogWarning($"Insufficient memory to safely start WebServer. Available: {availableMemory} bytes, Minimum: {MinMemoryForWebServer} bytes");
                    return;
                }
                
                LogHelper.LogInformation($"Starting WebServer with {availableMemory} bytes available memory");

                try
                {
                    this.InitializeWebServer();
                    _server.Start();
                    _isServerRunning = true;
                    
                    long afterStart = nanoFramework.Runtime.Native.GC.Run(false);
                    LogHelper.LogInformation($"Web server started successfully. Memory after start: {afterStart} bytes");
                }
                catch (OutOfMemoryException memEx)
                {
                    LogHelper.LogError($"Out of memory starting web server: {memEx.Message}");
                    LogService.LogCritical("WebServer failed to start due to insufficient memory", memEx);
                    
                    if (_server != null)
                    {
                        try
                        {
                            _server.Dispose();
                            _server = null;
                        }
                        catch
                        {
                            // Ignore cleanup errors
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("Error starting web server: " + ex.Message);
                    LogService.LogCritical("Error starting web server: " + ex.Message, ex);
                }
            }
        }

        /// <summary>
        /// Stops the web server.
        /// </summary>
        public void Stop()
        {
            if (_isServerRunning)
            {
                _server.Stop();
                _server.Dispose();
                _server = null;
                _isServerRunning = false;
                LogHelper.LogInformation("Web server stopped.");
            }
        }

        /// <summary>
        /// Restarts the web server.
        /// </summary>
        public void Restart()
        {
            this.Stop();
            this.Start();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_syncLock)
            {
                this.Stop();
                _connectionService.ConnectionRestored -= this.ConnectionRestored;
                _connectionService.ConnectionLost -= this.ConnectionLost;
                _disposed = true;
            }
        }

        private void ConnectionLost(object sender, EventArgs e)
        {
            LogHelper.LogWarning("Connection lost. Stopping the web server.");
            this.Stop();
        }

        private void ConnectionRestored(object sender, EventArgs e)
        {
            if (!_isServerRunning)
            {
                LogHelper.LogInformation("Connection restored. Starting the web server.");
                this.Start();
            }
        }

        /// <summary>
        /// Initializes the web server.
        /// </summary>
        private void InitializeWebServer()
        {
            if (_server == null)
            {
                // Register both SensorController and AuthController
                _server = new WebServerDi(80, HttpProtocol.Http, new Type[] { 
                    typeof(SensorController),
                    typeof(AuthController)
                }, _serviceProvider);
                LogHelper.LogInformation("Web server initialized with SensorController and AuthController.");
            }
        }
    }
}