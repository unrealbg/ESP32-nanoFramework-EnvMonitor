namespace ESP32_NF_MQTT_DHT.Services
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    public class InternetConnectionService : IInternetConnectionService, IDisposable
    {
        private static readonly IPAddress GoogleDns = IPAddress.Parse("8.8.8.8");
        private static readonly IPAddress CloudflareDns = IPAddress.Parse("1.1.1.1");
        
        private const int ConnectionTimeoutMs = 3000;
        private const int CheckIntervalMs = 10000;
        
        private readonly ManualResetEvent _stopSignal = new ManualResetEvent(false);
        private readonly object _stateLock = new object();
        private readonly object _disposeLock = new object();
        
        private bool _isInternetThreadRunning = false;
        private bool _disposed = false;
        
        private Thread _internetCheckThread;

        public event EventHandler InternetLost;
        public event EventHandler InternetRestored;

        public bool IsInternetAvailable()
        {
            ThrowIfDisposed();
            
            try
            {
                if (TryConnect(GoogleDns, 53, ConnectionTimeoutMs))
                {
                    return true;
                }
                
                if (TryConnect(CloudflareDns, 53, ConnectionTimeoutMs))
                {
                    return true;
                }
                
                lock (_stateLock)
                {
                    bool disposed;
                    lock (_disposeLock)
                    {
                        disposed = _disposed;
                    }
                    
                    if (!_isInternetThreadRunning && !disposed)
                    {
                        _isInternetThreadRunning = true;
                        LogHelper.LogWarning("No internet connection, starting internet check thread...");
                        this.OnInternetLost();

                        _internetCheckThread = new Thread(new ThreadStart(this.CheckInternetConnectionLoop));
                        _internetCheckThread.Start();
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error checking internet connection: {ex.Message}");
                return false;
            }
        }

        public void StopService()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
            }
            
            _stopSignal.Set();
            
            lock (_stateLock)
            {
                if (_internetCheckThread != null && _internetCheckThread.IsAlive)
                {
                    if (!_internetCheckThread.Join(5000))
                    {
                        LogHelper.LogWarning("Internet check thread did not terminate within timeout.");
                    }
                }
                _internetCheckThread = null;
                _isInternetThreadRunning = false;
            }
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            try
            {
                StopService();
                
                if (_stopSignal != null)
                {
                    _stopSignal.Set();
                }
                
                // Clear event handlers to prevent memory leaks
                InternetLost = null;
                InternetRestored = null;
                
                LogHelper.LogInformation("InternetConnectionService disposed.");
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error disposing InternetConnectionService: {ex.Message}");
            }
        }

        private void CheckInternetConnectionLoop()
        {
            try
            {
                bool disposed;
                lock (_disposeLock)
                {
                    disposed = _disposed;
                }
                
                while (!disposed && !this.TryCheckInternet())
                {
                    LogHelper.LogWarning($"Internet not available, checking again in {CheckIntervalMs / 1000} seconds...");
                    if (_stopSignal.WaitOne(CheckIntervalMs, false))
                    {
                        // Stop signal received
                        return;
                    }
                    
                    lock (_disposeLock)
                    {
                        disposed = _disposed;
                    }
                }

                lock (_disposeLock)
                {
                    disposed = _disposed;
                }
                
                if (!disposed)
                {
                    LogHelper.LogInformation("Internet is back.");
                    OnInternetRestored();
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error in internet check loop: {ex.Message}");
            }
            finally
            {
                lock (_stateLock)
                {
                    _isInternetThreadRunning = false;
                }
            }
        }

        private bool TryCheckInternet()
        {
            try
            {
                return TryConnect(GoogleDns, 53, ConnectionTimeoutMs) ||
                       TryConnect(CloudflareDns, 53, ConnectionTimeoutMs);
            }
            catch
            {
                return false;
            }
        }

        private bool TryConnect(IPAddress address, int port, int timeoutMs)
        {
            TcpClient tcpClient = null;
            try
            {
                tcpClient = new TcpClient();
                tcpClient.Connect(address, port);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    tcpClient?.Close();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
        }

        private void OnInternetLost()
        {
            try
            {
                InternetLost?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error in InternetLost event handler: {ex.Message}");
            }
        }

        private void OnInternetRestored()
        {
            try
            {
                InternetRestored?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"Error in InternetRestored event handler: {ex.Message}");
            }
        }

        private void ThrowIfDisposed()
        {
            lock (_disposeLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(InternetConnectionService));
                }
            }
        }
    }
}
