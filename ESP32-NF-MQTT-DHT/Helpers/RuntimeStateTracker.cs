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

        private static readonly object _syncLock = new object();

        private static bool _initialized;
        private static bool _watchdogStarted;
        private static string _lastState = "not-started";
        private static DateTime _lastProgressUtc = DateTime.MinValue;
        private static string _lastPersistedState;
        private static DateTime _lastPersistedUtc = DateTime.MinValue;
        private static string _lastMqttState = "mqtt:not-started";
        private static DateTime _lastMqttProgressUtc = DateTime.MinValue;
        private static string _lastMqttPublishState = "mqtt-publish:not-started";
        private static DateTime _lastMqttPublishSuccessUtc = DateTime.MinValue;
        private static string _previousState;
        private static string _lastWatchdogState = "watchdog:ok";
        private static string _lastWatchdogReportKey;

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

            lock (_syncLock)
            {
                _lastState = state;
                _lastProgressUtc = DateTime.UtcNow;

                if (IsMqttState(state))
                {
                    _lastMqttState = state;
                    _lastMqttProgressUtc = _lastProgressUtc;
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

        /// <summary>
        /// Tracks a successful MQTT publish separately from general MQTT worker progress.
        /// </summary>
        public static void MarkMqttPublishSuccess(string context)
        {
            if (string.IsNullOrEmpty(context))
            {
                context = "unknown";
            }

            lock (_syncLock)
            {
                _lastMqttPublishState = "mqtt-publish:success|" + context;
                _lastMqttPublishSuccessUtc = DateTime.UtcNow;
                _lastWatchdogState = "watchdog:ok";
                _lastWatchdogReportKey = null;
            }
        }

        /// <summary>
        /// Tracks a failed MQTT publish without refreshing the publish watchdog timestamp.
        /// </summary>
        public static void MarkMqttPublishFailure(string context)
        {
            if (string.IsNullOrEmpty(context))
            {
                context = "unknown";
            }

            lock (_syncLock)
            {
                _lastMqttPublishState = "mqtt-publish:failure|" + context + " at " + DateTime.UtcNow.ToString("u");
            }
        }

        public static string GetMqttPublishState()
        {
            lock (_syncLock)
            {
                string lastSuccess = _lastMqttPublishSuccessUtc == DateTime.MinValue
                    ? "never"
                    : _lastMqttPublishSuccessUtc.ToString("u");

                return _lastMqttPublishState + "; lastSuccess=" + lastSuccess;
            }
        }

        public static string GetWatchdogState()
        {
            lock (_syncLock)
            {
                return _lastWatchdogState;
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
                string lastMqttPublishState;
                DateTime lastMqttPublishSuccessUtc;

                lock (_syncLock)
                {
                    lastState = _lastState;
                    lastProgressUtc = _lastProgressUtc;
                    lastMqttState = _lastMqttState;
                    lastMqttProgressUtc = _lastMqttProgressUtc;
                    lastMqttPublishState = _lastMqttPublishState;
                    lastMqttPublishSuccessUtc = _lastMqttPublishSuccessUtc;
                }

                if (lastProgressUtc == DateTime.MinValue)
                {
                    continue;
                }

                if (IsMqttPublishWatchdogExpired(lastMqttPublishSuccessUtc))
                {
                    ReportWatchdogTimeout("mqtt-publish-stale|" + lastMqttPublishState,
                        "Watchdog timeout. MQTT publish stalled. Last publish state: '" + lastMqttPublishState + "'");
                    continue;
                }

                if ((DateTime.UtcNow - lastProgressUtc).TotalMilliseconds < WatchdogTimeoutMs)
                {
                    if (!IsMqttWatchdogExpired(lastMqttProgressUtc))
                    {
                        continue;
                    }

                    ReportWatchdogTimeout("mqtt-stale|" + lastMqttState,
                        "Watchdog timeout. MQTT progress stalled. Last MQTT state: '" + lastMqttState + "' at " + lastMqttProgressUtc.ToString("u"));
                    continue;
                }

                ReportWatchdogTimeout("runtime-stale|" + lastState,
                    "Watchdog timeout. Last runtime state: '" + lastState + "' at " + lastProgressUtc.ToString("u"));
            }
        }

        private static void ReportWatchdogTimeout(string reportKey, string message)
        {
            bool shouldReport = false;
            string snapshot = BuildSnapshot(DateTime.UtcNow, "watchdog-timeout|" + reportKey);

            lock (_syncLock)
            {
                _lastWatchdogState = snapshot;

                if (_lastWatchdogReportKey != reportKey)
                {
                    _lastWatchdogReportKey = reportKey;
                    _lastPersistedState = "watchdog-timeout|" + reportKey;
                    _lastPersistedUtc = DateTime.UtcNow;
                    shouldReport = true;
                }
            }

            if (!shouldReport)
            {
                return;
            }

            TryWriteState(snapshot);
            LogService.LogCritical(message);

            if (AppConfiguration.Features.EnableWatchdogReboot)
            {
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

        private static bool IsMqttPublishWatchdogExpired(DateTime lastMqttPublishSuccessUtc)
        {
            if (!AppConfiguration.Features.EnableMqttClient)
            {
                return false;
            }

            if (lastMqttPublishSuccessUtc == DateTime.MinValue)
            {
                return false;
            }

            return (DateTime.UtcNow - lastMqttPublishSuccessUtc).TotalMilliseconds >= WatchdogTimeoutMs;
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
