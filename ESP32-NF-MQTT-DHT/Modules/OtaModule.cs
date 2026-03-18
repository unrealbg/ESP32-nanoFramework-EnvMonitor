namespace ESP32_NF_MQTT_DHT.Modules
{
    using System;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Modules.Contracts;
    using ESP32_NF_MQTT_DHT.OTA;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    /// <summary>
    /// OTA module. Leverages the existing MQTT wiring for commands, and optionally
    /// performs periodic manifest checks when configured.
    /// </summary>
    public sealed class OtaModule : IModule, IDisposable
    {
        private readonly IOtaService _otaService;
        private readonly object _stateLock = new object();
        private readonly ManualResetEvent _stopSignal = new ManualResetEvent(false);
        private Thread _periodicThread;
        private bool _running;

        public OtaModule(IOtaService otaService)
        {
            _otaService = otaService;
        }

        public string Name => "OTA";

        public void Start()
        {
            // Periodic background check (optional)
            if (!string.IsNullOrEmpty(Config.PeriodicManifestUrl))
            {
                lock (_stateLock)
                {
                    _running = true;
                }

                _stopSignal.Reset();

                _periodicThread = new Thread(this.Worker);
                _periodicThread.Start();
                LogHelper.LogInformation("OTA periodic check thread started");
            }
        }

        public void Stop()
        {
            lock (_stateLock)
            {
                _running = false;
            }

            _stopSignal.Set();

            if (_periodicThread != null)
            {
                try
                {
                    _periodicThread.Join(1000);
                }
                catch
                {
                    // ignored
                }

                _periodicThread = null;
            }
        }

        public void Dispose()
        {
            this.Stop();
        }

        private bool IsRunning()
        {
            lock (_stateLock)
            {
                return _running;
            }
        }

        private void Worker()
        {
            var mgr = new OtaManager();
            while (this.IsRunning())
            {
                try
                {
                    mgr.CheckAndUpdateFromUrl(Config.PeriodicManifestUrl);
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("OTA periodic check failed: " + ex.Message);
                }

                // Daily check by default, but allow Stop() to interrupt immediately.
                if (_stopSignal.WaitOne(24 * 60 * 60 * 1000, false))
                {
                    break;
                }
            }
        }
    }
}
