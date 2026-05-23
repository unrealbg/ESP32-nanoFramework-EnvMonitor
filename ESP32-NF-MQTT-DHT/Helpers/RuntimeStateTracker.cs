namespace ESP32_NF_MQTT_DHT.Helpers
{
    using System;
    using System.IO;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Configuration;
    using ESP32_NF_MQTT_DHT.Services;

    using nanoFramework.Runtime.Native;

    /// <summary>
    /// Tracks the latest runtime state and reboots the device if no forward progress is observed.
    /// </summary>
    public static class RuntimeStateTracker
    {
        private const string StateFilePath = @"I:\last_state.txt";
        private const string MqttStatePrefix = "mqtt:";
        private const int WatchdogCheckIntervalMs = 60000;
        private const int WatchdogTimeoutMs = 600000;
        private const int MinPersistIntervalMs = 2000;

        private static readonly object _syncLock = new object();

        private static bool _initialized;
        private static bool _watchdogStarted;
        private static string _lastState = "not-started";
        private static DateTime _lastProgressUtc = DateTime.MinValue;
        private static string _lastPersistedState;
        private static DateTime _lastPersistedUtc = DateTime.MinValue;
        private static string _lastMqttState = "mqtt:not-started";
        private static DateTime _lastMqttProgressUtc = DateTime.MinValue;
        private static string _previousState;

        /// <summary>
        /// Initializes the tracker and reports the previous persisted state, if available.
        /// </summary>
        public static void Initialize()
        {
            lock (_syncLock)
            {
                if (_initialized)
                {
                    return;
                }

                _initialized = true;
            }

            string previousState = TryReadLastState();
            if (!string.IsNullOrEmpty(previousState))
            {
                lock (_syncLock)
                {
                    _previousState = previousState;
                }

                LogService.LogCritical("Previous runtime state: " + previousState);
            }

            MarkProgress("boot");
        }

        /// <summary>
        /// Starts the watchdog thread once for the process lifetime.
        /// </summary>
        public static void StartWatchdog()
        {
            lock (_syncLock)
            {
                if (_watchdogStarted)
                {
                    return;
                }

                _watchdogStarted = true;
            }

            var watchdogThread = new Thread(WatchdogLoop);
            watchdogThread.Start();
        }

        /// <summary>
        /// Persists the current runtime state and updates the progress timestamp.
        /// </summary>
        /// <param name="state">A short state description.</param>
        public static void MarkProgress(string state)
        {
            if (string.IsNullOrEmpty(state))
            {
                state = "unknown";
            }

            string snapshot = null;
            lock (_syncLock)
            {
                _lastState = state;
                _lastProgressUtc = DateTime.UtcNow;

                if (IsMqttState(state))
                {
                    _lastMqttState = state;
                    _lastMqttProgressUtc = _lastProgressUtc;
                }

                bool stateChanged = _lastPersistedState != state;
                bool persistIntervalElapsed = _lastPersistedUtc == DateTime.MinValue ||
                                              (_lastProgressUtc - _lastPersistedUtc).TotalMilliseconds >= MinPersistIntervalMs;

                if (stateChanged || persistIntervalElapsed)
                {
                    snapshot = BuildSnapshot(_lastProgressUtc, _lastState);
                    TryWriteState(snapshot);
                    _lastPersistedState = state;
                    _lastPersistedUtc = _lastProgressUtc;
                }
            }
        }

        /// <summary>
        /// Gets the last persisted runtime state snapshot.
        /// </summary>
        /// <returns>The last persisted state, or a fallback message when unavailable.</returns>
        public static string GetLastState()
        {
            lock (_syncLock)
            {
                if (_lastPersistedUtc != DateTime.MinValue)
                {
                    string persisted = TryReadLastState();
                    if (!string.IsNullOrEmpty(persisted))
                    {
                        return persisted;
                    }
                }

                if (_lastProgressUtc == DateTime.MinValue)
                {
                    return "No runtime state available.";
                }

                return BuildSnapshot(_lastProgressUtc, _lastState);
            }
        }

        /// <summary>
        /// Gets the runtime state that was persisted before the current boot.
        /// </summary>
        public static string GetPreviousState()
        {
            lock (_syncLock)
            {
                return string.IsNullOrEmpty(_previousState)
                    ? "No previous runtime state available."
                    : _previousState;
            }
        }

        private static void WatchdogLoop()
        {
            while (true)
            {
                Thread.Sleep(WatchdogCheckIntervalMs);

                string lastState;
                DateTime lastProgressUtc;
                string lastMqttState;
                DateTime lastMqttProgressUtc;

                lock (_syncLock)
                {
                    lastState = _lastState;
                    lastProgressUtc = _lastProgressUtc;
                    lastMqttState = _lastMqttState;
                    lastMqttProgressUtc = _lastMqttProgressUtc;
                }

                if (lastProgressUtc == DateTime.MinValue)
                {
                    continue;
                }

                if ((DateTime.UtcNow - lastProgressUtc).TotalMilliseconds < WatchdogTimeoutMs)
                {
                    if (!IsMqttWatchdogExpired(lastMqttProgressUtc))
                    {
                        continue;
                    }

                    string mqttMessage = "Watchdog timeout. MQTT progress stalled. Last MQTT state: '" + lastMqttState + "' at " + lastMqttProgressUtc.ToString("u");
                    lock (_syncLock)
                    {
                        string mqttSnapshot = BuildSnapshot(DateTime.UtcNow, "watchdog-timeout|mqtt-stale|" + lastMqttState);
                        TryWriteState(mqttSnapshot);
                        _lastPersistedState = "watchdog-timeout|mqtt-stale|" + lastMqttState;
                        _lastPersistedUtc = DateTime.UtcNow;
                    }

                    LogService.LogCritical(mqttMessage);
                    Thread.Sleep(1000);
                    Power.RebootDevice();
                    return;
                }

                string message = "Watchdog timeout. Last runtime state: '" + lastState + "' at " + lastProgressUtc.ToString("u");
                lock (_syncLock)
                {
                    string snapshot = BuildSnapshot(DateTime.UtcNow, "watchdog-timeout|" + lastState);
                    TryWriteState(snapshot);
                    _lastPersistedState = "watchdog-timeout|" + lastState;
                    _lastPersistedUtc = DateTime.UtcNow;
                }
                LogService.LogCritical(message);
                Thread.Sleep(1000);
                Power.RebootDevice();
            }
        }

        private static string BuildSnapshot(DateTime timestampUtc, string state)
        {
            return timestampUtc.ToString("u") + " | " + state;
        }

        private static bool IsMqttState(string state)
        {
            return !string.IsNullOrEmpty(state) && state.IndexOf(MqttStatePrefix) == 0;
        }

        private static bool IsMqttWatchdogExpired(DateTime lastMqttProgressUtc)
        {
            if (!AppConfiguration.Features.EnableMqttClient)
            {
                return false;
            }

            if (lastMqttProgressUtc == DateTime.MinValue)
            {
                return false;
            }

            return (DateTime.UtcNow - lastMqttProgressUtc).TotalMilliseconds >= WatchdogTimeoutMs;
        }

        private static string TryReadLastState()
        {
            try
            {
                if (!File.Exists(StateFilePath))
                {
                    return null;
                }

                return File.ReadAllText(StateFilePath);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Failed to read runtime state: " + ex.Message);
                return null;
            }
        }

        private static void TryWriteState(string snapshot)
        {
            try
            {
                File.WriteAllText(StateFilePath, snapshot);
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Failed to write runtime state: " + ex.Message);
            }
        }
    }
}
