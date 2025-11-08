namespace ESP32_NF_MQTT_DHT.Services
{
    using Contracts;

    using Helpers;

    using System;
    using System.Device.Wifi;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Threading;

    using static Settings.WifiSettings;

    /// <summary>
    /// Service for managing network connections, including connecting to Wi-Fi and checking connection status.
    /// </summary>
    public class ConnectionService : IConnectionService, IDisposable
    {
        private const int MAX_CONNECTION_ATTEMPTS = 10;
        private const int RECONNECT_DELAY_MS = 10000;
        private const int CONNECTION_CHECK_INTERVAL_MS = 200;
        private const int SNTP_SYNC_TIMEOUT_MS = 20000;
        private const int SNTP_CHECK_INTERVAL_MS = 1000;

        private readonly WifiAdapter _wifiAdapter;
        private readonly object _connectionLock = new object();
        private readonly object _eventLock = new object();
        
        private bool _hasConnectedSuccessfully = false;
        private bool _isConnectionInProgress = false;
        private bool _disposed = false;
        private string _ipAddress;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionService"/> class.
        /// </summary>
        public ConnectionService()
        {
            try
            {
                _wifiAdapter = WifiAdapter.FindAllAdapters()[0];
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Failed to initialize WiFi adapter: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Event triggered when the connection is restored.
        /// </summary>
        public event EventHandler ConnectionRestored;

        /// <summary>
        /// Event triggered when the connection is lost.
        /// </summary>
        public event EventHandler ConnectionLost;

        /// <summary>
        /// Gets a value indicating whether the device is currently connected to the network.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                ThrowIfDisposed();
                return this.IsAlreadyConnected(out _);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the connection is in progress.
        /// </summary>
        public bool IsConnectionInProgress
        {
            get
            {
                lock (_connectionLock)
                {
                    return _isConnectionInProgress;
                }
            }
        }

        /// <summary>
        /// Initiates a connection to the network.
        /// </summary>
        public void Connect()
        {
            ThrowIfDisposed();
            
            if (_wifiAdapter == null)
            {
                LogHelper.LogError("Cannot connect: WiFi adapter not available");
                return;
            }

            if (this.IsAlreadyConnected(out var currentIp))
            {
                LogHelper.LogInformation($"Already connected. IP: {currentIp}");
                return;
            }

            lock (_connectionLock)
            {
                _isConnectionInProgress = true;
            }

            try
            {
                while (!_disposed && !this.IsAlreadyConnected(out _))
                {
                    int attemptCount = 0;
                    bool connected = false;

                    while (!_disposed && !this.IsAlreadyConnected(out string ipAddress) && attemptCount < MAX_CONNECTION_ATTEMPTS)
                    {
                        attemptCount++;
                        LogHelper.LogInformation($"Connecting... [Attempt {attemptCount}/{MAX_CONNECTION_ATTEMPTS}]");

                        try
                        {
                            var result = _wifiAdapter.Connect(SSID, WifiReconnectionKind.Automatic, Password);

                            if (this.TryWaitForConnection(result, out ipAddress))
                            {
                                this.HandleSuccessfulConnection(ipAddress);
                                connected = true;
                                break;
                            }

                            LogHelper.LogWarning($"{this.GetErrorMessage(result.ConnectionStatus)}. Retrying in {RECONNECT_DELAY_MS / 1000} seconds...");
                        }
                        catch (Exception ex)
                        {
                            LogHelper.LogError($"Connection error: {ex.Message}");
                            LogService.LogCritical($"Connection error: {ex.Message}", ex);
                        }

                        if (!_disposed)
                        {
                            Thread.Sleep(RECONNECT_DELAY_MS);
                        }
                    }

                    if (connected || _disposed)
                    {
                        break;
                    }

                    if (this.IsAlreadyConnected(out string ip))
                    {
                        lock (_connectionLock)
                        {
                            _ipAddress = ip;
                            _isConnectionInProgress = false;
                        }

                        if (_hasConnectedSuccessfully)
                        {
                            LogHelper.LogInformation($"Connection restored. IP Address: {ip}");
                            this.RaiseConnectionRestored();
                        }
                        else
                        {
                            LogHelper.LogInformation($"Connection established after retry. IP Address: {ip}");
                            _hasConnectedSuccessfully = true;
                        }

                        break;
                    }

                    LogHelper.LogError($"Failed to connect after {MAX_CONNECTION_ATTEMPTS} attempts. Sleeping for 1 minute before retrying...");
                    
                    if (!_disposed)
                    {
                        Thread.Sleep(60000);
                    }

                    if (!_disposed && this.IsAlreadyConnected(out ip))
                    {
                        _ipAddress = ip;
                        LogHelper.LogInformation($"Connection restored. IP Address: {ip}");
                        this.RaiseConnectionRestored();
                        lock (_connectionLock)
                        {
                            _isConnectionInProgress = false;
                        }
                    }
                }
            }
            finally
            {
                lock (_connectionLock)
                {
                    _isConnectionInProgress = false;
                }
            }
        }

        /// <summary>
        /// Checks the network connection and attempts to reconnect if it is lost.
        /// </summary>
        public void CheckConnection()
        {
            ThrowIfDisposed();
            
            if (_isConnectionInProgress)
            {
                return; // Don't start another connection attempt if one is already in progress
            }

            if (!this.IsAlreadyConnected(out _))
            {
                lock (_connectionLock)
                {
                    _isConnectionInProgress = true;
                }
                
                this.RaiseConnectionLost();
                LogHelper.LogWarning("Lost network connection. Attempting to reconnect...");
                this.Connect();
            }
        }

        /// <summary>
        /// Gets the IP address of the device.
        /// </summary>
        /// <returns>The IP address of the device.</returns>
        public string GetIpAddress()
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(_ipAddress) || _ipAddress == "0.0.0.0")
            {
                if (this.IsAlreadyConnected(out string currentIp))
                {
                    _ipAddress = currentIp;
                }
                else
                {
                    return "IP address not available";
                }
            }

            return _ipAddress;
        }

        /// <summary>
        /// Disposes the connection service and releases resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _disposed = true;
                
                // Clear event handlers to prevent memory leaks
                lock (_eventLock)
                {
                    ConnectionRestored = null;
                    ConnectionLost = null;
                }
                
                // Note: WifiAdapter doesn't implement IDisposable in nanoFramework
                // but we set it to null to allow GC
                
                LogHelper.LogInformation("ConnectionService disposed.");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error disposing ConnectionService: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the device is already connected to the network.
        /// </summary>
        /// <param name="ipAddress">The IP address of the device if connected.</param>
        /// <returns><c>true</c> if the device is connected; otherwise, <c>false</c>.</returns>
        private bool IsAlreadyConnected(out string ipAddress)
        {
            ipAddress = null;

            if (_disposed) return false;

            try
            {
                var networkInterface = NetworkInterface.GetAllNetworkInterfaces()[0];
                ipAddress = networkInterface.IPv4Address;
                return !(string.IsNullOrEmpty(ipAddress) || ipAddress == "0.0.0.0");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Network interface error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tries to wait for a successful connection.
        /// </summary>
        /// <param name="result">The WiFi connection result.</param>
        /// <param name="ipAddress">The resulting IP address.</param>
        /// <returns>True if connection successful, false otherwise.</returns>
        private bool TryWaitForConnection(WifiConnectionResult result, out string ipAddress)
        {
            ipAddress = null;

            if (result.ConnectionStatus != WifiConnectionStatus.Success)
            {
                return false;
            }

            for (int i = 0; i < MAX_CONNECTION_ATTEMPTS && !_disposed; i++)
            {
                if (this.IsAlreadyConnected(out string currentIp))
                {
                    ipAddress = currentIp;
                    return true;
                }

                Thread.Sleep(CONNECTION_CHECK_INTERVAL_MS);
            }

            return false;
        }

        /// <summary>
        /// Handles logic for a successful connection.
        /// </summary>
        /// <param name="ipAddress">The assigned IP address.</param>
        private void HandleSuccessfulConnection(string ipAddress)
        {
            _ipAddress = ipAddress;

            if (!_hasConnectedSuccessfully)
            {
                LogHelper.LogInformation($"Connection established. IP address: {ipAddress}");
                _hasConnectedSuccessfully = true;
            }
            else
            {
                LogHelper.LogInformation($"Connection restored. IP Address: {ipAddress}");
                this.RaiseConnectionRestored();
            }

            // Sync time via SNTP
            this.SyncTimeViaSNTP();

            lock (_connectionLock)
            {
                _isConnectionInProgress = false;
            }
        }

        /// <summary>
        /// Synchronizes time via SNTP.
        /// </summary>
        private void SyncTimeViaSNTP()
        {
            try
            {
                if (DateTime.UtcNow.Year < 2020)
                {
                    LogHelper.LogInformation("Syncing time via SNTP...");
                    
                    nanoFramework.Networking.Sntp.Server1 = ESP32_NF_MQTT_DHT.Settings.TimeSettings.NtpServer;
                    
                    // Optional fallback server to improve reliability
                    try 
                    { 
                        nanoFramework.Networking.Sntp.Server2 = "time.google.com"; 
                    } 
                    catch (Exception ex)
                    {
                        LogHelper.LogWarning($"Failed to set SNTP Server2: {ex.Message}");
                    }

                    nanoFramework.Networking.Sntp.Start();

                    // Wait for SNTP to set the clock
                    int attempts = 0;
                    int maxAttempts = SNTP_SYNC_TIMEOUT_MS / SNTP_CHECK_INTERVAL_MS;
                    
                    while (attempts < maxAttempts && DateTime.UtcNow.Year < 2020 && !_disposed)
                    {
                        Thread.Sleep(SNTP_CHECK_INTERVAL_MS);
                        attempts++;
                    }

                    if (DateTime.UtcNow.Year < 2020)
                    {
                        LogHelper.LogWarning($"SNTP failed to set time within {SNTP_SYNC_TIMEOUT_MS}ms timeout.");
                    }
                    else
                    {
                        LogHelper.LogInformation($"SNTP synced. UTC now: {DateTime.UtcNow}");
                    }

                    // Stop the SNTP client after initial sync
                    nanoFramework.Networking.Sntp.Stop();
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogWarning($"SNTP sync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the error message corresponding to the Wi-Fi connection status.
        /// </summary>
        /// <param name="status">The Wi-Fi connection status.</param>
        /// <returns>The error message.</returns>
        private string GetErrorMessage(WifiConnectionStatus status)
        {
            switch (status)
            {
                case WifiConnectionStatus.AccessRevoked: return "Access to the network has been revoked";
                case WifiConnectionStatus.InvalidCredential: return "Invalid credential was presented";
                case WifiConnectionStatus.NetworkNotAvailable: return "Network is not available";
                case WifiConnectionStatus.Timeout: return "Connection attempt timed out";
                case WifiConnectionStatus.UnspecifiedFailure: return "Unspecified error [connection refused]";
                case WifiConnectionStatus.UnsupportedAuthenticationProtocol: return "Authentication protocol is not supported";
                default: return "Unknown error";
            }
        }

        private void RaiseConnectionRestored()
        {
            try
            {
                EventHandler handler;
                lock (_eventLock)
                {
                    handler = ConnectionRestored;
                }
                handler?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error in ConnectionRestored event handler: {ex.Message}");
            }
        }

        private void RaiseConnectionLost()
        {
            try
            {
                EventHandler handler;
                lock (_eventLock)
                {
                    handler = ConnectionLost;
                }
                handler?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error in ConnectionLost event handler: {ex.Message}");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ConnectionService));
            }
        }
    }
}