namespace ESP32_NF_MQTT_DHT.Helpers
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Lightweight key=value configuration loader for nanoFramework devices.
    /// Reads from the device filesystem to avoid hard-coded secrets in firmware.
    /// </summary>
    public static class DeviceConfig
    {
        // Keep in the same writable area as CredentialCache
        private const string ConfigPath = "I:\\config\\device.config";
        private static readonly object SyncLock = new object();
        private static readonly Hashtable Values = new Hashtable();
        private static bool _loaded;

        public static void Load()
        {
            lock (SyncLock)
            {
                if (_loaded)
                {
                    return;
                }

                _loaded = true;

                try
                {
                    if (!File.Exists(ConfigPath))
                    {
                        Debug.WriteLine("Device config not found: " + ConfigPath);
                        return;
                    }

                    var bytes = File.ReadAllBytes(ConfigPath);
                    try
                    {
                        var content = new string(Encoding.UTF8.GetChars(bytes));
                        ParseContent(content);
                    }
                    finally
                    {
                        if (bytes != null)
                        {
                            Array.Clear(bytes, 0, bytes.Length);
                        }
                    }

                    Debug.WriteLine("Device config loaded: " + Values.Count + " key(s)");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error loading device config: " + ex.Message);
                }
            }
        }

        public static string GetString(string key, string defaultValue = "")
        {
            if (string.IsNullOrEmpty(key))
            {
                return defaultValue;
            }

            lock (SyncLock)
            {
                if (!_loaded)
                {
                    Load();
                }

                if (Values.Contains(key))
                {
                    return (string)Values[key];
                }
            }

            return defaultValue;
        }

        public static int GetInt32(string key, int defaultValue)
        {
            var s = GetString(key, null);
            if (string.IsNullOrEmpty(s))
            {
                return defaultValue;
            }

            int v;
            return int.TryParse(s, out v) ? v : defaultValue;
        }

        public static bool GetBoolean(string key, bool defaultValue)
        {
            var s = GetString(key, null);
            if (string.IsNullOrEmpty(s))
            {
                return defaultValue;
            }

            // Accept common values: true/false, 1/0, yes/no
            if (s == "1" || s == "true" || s == "True" || s == "yes" || s == "Yes")
            {
                return true;
            }

            if (s == "0" || s == "false" || s == "False" || s == "no" || s == "No")
            {
                return false;
            }

            return defaultValue;
        }

        private static void ParseContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            // Normalize CRLF -> LF (Replace() isn't always available/cheap)
            var sb = new StringBuilder();
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == '\r')
                {
                    continue;
                }

                sb.Append(content[i]);
            }

            string normalized = sb.ToString();
            string[] lines = normalized.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line == null)
                {
                    continue;
                }

                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                // comments
                if (line[0] == '#' || line[0] == ';')
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string value = (eq + 1 < line.Length) ? line.Substring(eq + 1).Trim() : string.Empty;

                if (key.Length == 0)
                {
                    continue;
                }

                // Store as-is (do not log values; may contain secrets)
                Values[key] = value;
            }
        }
    }
}
