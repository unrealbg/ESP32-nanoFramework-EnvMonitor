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

        private const int CheckIntervalMs = 15000;
        private const int MonitorThreadJoinTimeoutMs = 1000;

        private readonly object _syncLock = new object();
        private readonly ManualResetEvent _stopSignal = new ManualResetEvent(false);
        private readonly IConnectionService _connectionService;

        private Thread _monitorThread;

        private bool _disposed;
        private bool _hasProbeResult;
        private bool _lastKnownInternetAvailable;
        private DateTime _lastProbeUtc = DateTime.MinValue;

        public InternetConnectionService(IConnectionService connectionService)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _monitorThread = new Thread(this.MonitorLoop);
            _monitorThread.Start();
        }

        public event EventHandler InternetLost;
        public event EventHandler InternetRestored;

        public bool IsInternetAvailable()
        {
            ThrowIfDisposed();

            if (!this.IsWifiConnected())
            {
                return false;
            }

            lock (_syncLock)
            {
                if (_hasProbeResult && (DateTime.UtcNow - _lastProbeUtc).TotalMilliseconds < CheckIntervalMs)
                {
                    return _lastKnownInternetAvailable;
                }
            }

            bool isAvailable = this.TryCheckInternet();
            this.UpdateInternetState(isAvailable);
            return isAvailable;
        }

        public void Dispose()
        {
            Thread monitorThread = null;

            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                InternetLost = null;
                InternetRestored = null;
                monitorThread = _monitorThread;
                _monitorThread = null;
            }

            _stopSignal.Set();

            if (monitorThread != null && monitorThread.IsAlive)
            {
                if (!monitorThread.Join(MonitorThreadJoinTimeoutMs))
                {
                    LogHelper.LogWarning("Internet monitor thread did not terminate within timeout.");
                }
            }

            LogHelper.LogInformation("InternetConnectionService disposed.");
        }

        private void MonitorLoop()
        {
            while (true)
            {
                if (this.IsDisposed())
                {
                    return;
                }

                if (this.IsWifiConnected())
                {
                    bool isAvailable = this.TryCheckInternet();
                    this.UpdateInternetState(isAvailable);
                }

                if (_stopSignal.WaitOne(CheckIntervalMs, false))
                {
                    return;
                }
            }
        }

        private bool TryCheckInternet()
        {
            try
            {
                return TryConnect(GoogleDns, 53) || TryConnect(CloudflareDns, 53);
            }
            catch
            {
                return false;
            }
        }

        private void UpdateInternetState(bool isAvailable)
        {
            EventHandler handler = null;
            bool shouldRaise = false;

            lock (_syncLock)
            {
                if (_disposed)
                {
                    return;
                }

                bool changed = _hasProbeResult && (_lastKnownInternetAvailable != isAvailable);
                _hasProbeResult = true;
                _lastKnownInternetAvailable = isAvailable;
                _lastProbeUtc = DateTime.UtcNow;

                if (!changed)
                {
                    return;
                }

                shouldRaise = true;
                handler = isAvailable ? InternetRestored : InternetLost;
            }

            try
            {
                if (shouldRaise)
                {
                    if (isAvailable)
                    {
                        LogHelper.LogInformation("Internet connection restored.");
                    }
                    else
                    {
                        LogHelper.LogWarning("Internet connection lost.");
                    }

                    handler?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Error raising internet state event: " + ex.Message);
            }
        }

        private static bool TryConnect(IPAddress address, int port)
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

        private void ThrowIfDisposed()
        {
            lock (_syncLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(InternetConnectionService));
                }
            }
        }

        private bool IsDisposed()
        {
            lock (_syncLock)
            {
                return _disposed;
            }
        }

        private bool IsWifiConnected()
        {
            try
            {
                return _connectionService.IsConnected;
            }
            catch
            {
                return false;
            }
        }
    }
}
