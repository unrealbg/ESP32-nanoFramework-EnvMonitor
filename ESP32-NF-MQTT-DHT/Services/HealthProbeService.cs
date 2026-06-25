namespace ESP32_NF_MQTT_DHT.Services
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Configuration;
    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    public class HealthProbeService : IHealthProbeService, IDisposable
    {
        private const int SocketTimeoutMs = 3000;
        private const int SocketErrorRetryDelayMs = 1000;

        private readonly IUptimeService _uptimeService;
        private readonly IConnectionService _connectionService;
        private readonly IMqttClientService _mqttClientService;
        private readonly object _syncLock = new object();

        private TcpListener _listener;
        private Thread _listenerThread;
        private bool _isRunning;
        private bool _disposed;

        public HealthProbeService(
            IUptimeService uptimeService,
            IConnectionService connectionService,
            IMqttClientService mqttClientService)
        {
            _uptimeService = uptimeService;
            _connectionService = connectionService;
            _mqttClientService = mqttClientService;

            _connectionService.ConnectionLost += this.ConnectionLost;
            _connectionService.ConnectionRestored += this.ConnectionRestored;
        }

        public void Start()
        {
            Thread listenerThread;

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HealthProbeService));
            }

            lock (_syncLock)
            {
                if (_isRunning || (_listenerThread != null && _listenerThread.IsAlive))
                {
                    return;
                }

                _isRunning = true;
                _listenerThread = new Thread(this.Listen);
                listenerThread = _listenerThread;
            }

            listenerThread.Start();

            LogHelper.LogInformation("Health probe start requested on port " + AppConfiguration.Network.HealthProbePort);
        }

        public void Stop()
        {
            Thread listenerThread;

            lock (_syncLock)
            {
                _isRunning = false;
                listenerThread = _listenerThread;
            }

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("Error stopping health probe: " + ex.Message);
                }
            }

            if (listenerThread != null && listenerThread.IsAlive && Thread.CurrentThread != listenerThread)
            {
                listenerThread.Join(2000);
            }

            lock (_syncLock)
            {
                if (_listenerThread == listenerThread && (listenerThread == null || !listenerThread.IsAlive))
                {
                    _listenerThread = null;
                }
            }

            LogHelper.LogInformation("Health probe stopped.");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            this.Stop();
            _connectionService.ConnectionLost -= this.ConnectionLost;
            _connectionService.ConnectionRestored -= this.ConnectionRestored;
            _disposed = true;
        }

        private void Listen()
        {
            Thread currentThread = Thread.CurrentThread;
            TcpListener listener = null;

            try
            {
                if (!_connectionService.IsConnected)
                {
                    LogHelper.LogWarning("Health probe start skipped: no active network connection.");
                    lock (_syncLock)
                    {
                        _isRunning = false;
                    }

                    return;
                }

                listener = new TcpListener(IPAddress.Any, AppConfiguration.Network.HealthProbePort);
                _listener = listener;
                listener.Server.ReceiveTimeout = SocketTimeoutMs;
                listener.Server.SendTimeout = SocketTimeoutMs;
                listener.Start(2);

                LogHelper.LogInformation("Health probe listening on port " + AppConfiguration.Network.HealthProbePort);

                while (this.IsRunning())
                {
                    TcpClient client = null;
                    try
                    {
                        client = listener.AcceptTcpClient();
                        this.WriteHealthSnapshot(client);
                    }
                    catch (SocketException ex)
                    {
                        if (this.IsRunning())
                        {
                            LogHelper.LogError("Health probe socket error: " + ex.Message);
                            LogService.LogCritical("Health probe socket error: " + ex.Message);
                            Thread.Sleep(SocketErrorRetryDelayMs);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (this.IsRunning())
                        {
                            LogHelper.LogError("Health probe request error: " + ex.Message);
                            LogService.LogCritical("Health probe request error: " + ex.Message);
                        }
                    }
                    finally
                    {
                        if (client != null)
                        {
                            try
                            {
                                client.Close();
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Fatal health probe error: " + ex.Message);
                LogService.LogCritical("Fatal health probe error: " + ex.Message);
            }
            finally
            {
                lock (_syncLock)
                {
                    if (_listenerThread == currentThread)
                    {
                        _isRunning = false;
                        _listenerThread = null;
                    }
                }

                if (listener != null)
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch
                    {
                    }

                    if (_listener == listener)
                    {
                        _listener = null;
                    }
                }
            }
        }

        private void WriteHealthSnapshot(TcpClient client)
        {
            NetworkStream stream = client.GetStream();

            this.SendLine(stream, "ok; probe=alive");

            string payload;
            try
            {
                payload = HealthSnapshot.BuildLine(_uptimeService, _connectionService, _mqttClientService);
            }
            catch (Exception ex)
            {
                payload = "ok; healthSnapshot=error:" + ex.Message;
            }

            this.SendLine(stream, payload);
            stream.Flush();
            Thread.Sleep(250);
        }

        private void SendLine(NetworkStream stream, string line)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
            stream.Write(bytes, 0, bytes.Length);
        }

        private bool IsRunning()
        {
            lock (_syncLock)
            {
                return _isRunning;
            }
        }

        private void ConnectionRestored(object sender, EventArgs e)
        {
            if (!_disposed && AppConfiguration.Features.EnableHealthProbe)
            {
                this.Start();
            }
        }

        private void ConnectionLost(object sender, EventArgs e)
        {
            if (!_disposed)
            {
                LogHelper.LogWarning("Health probe keeping listener active after connection lost event.");
            }
        }
    }
}
