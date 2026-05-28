namespace ESP32_NF_MQTT_DHT.OTA
{
    using System;
    using System.Text;

    using nanoFramework.M2Mqtt;

    internal static class OtaUtil
    {
        private static readonly object _statusClientLock = new object();
        private static MqttClient _statusClient;

        public static void EnsureStatusClient()
        {
            lock (_statusClientLock)
            {
                if (_statusClient != null && _statusClient.IsConnected)
                {
                    return;
                }

                DisposeStatusClient();

                try
                {
                    _statusClient = new MqttClient(Config.BrokerHost, Config.BrokerPort, Config.BrokerTls, null, null, MqttSslProtocols.None);
                    _statusClient.Connect(Config.DeviceId + "-status", Config.BrokerUser, Config.BrokerPass);
                }
                catch (Exception ex)
                {
                    DisposeStatusClient();
                    Console.WriteLine("[MQTT-STATUS] " + ex.Message);
                }
            }
        }

        public static void PublishStatus(MqttClient cli, string state, string msg)
        {
            var json = StatusJson(state, msg);
            try
            {
                if (cli != null && cli.IsConnected)
                {
                    cli.Publish(Config.TopicStatus, Encoding.UTF8.GetBytes(json));
                    return;
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                EnsureStatusClient();
                lock (_statusClientLock)
                {
                    if (_statusClient != null && _statusClient.IsConnected)
                    {
                        _statusClient.Publish(Config.TopicStatus, Encoding.UTF8.GetBytes(json));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MQTT-STATUS] publish: " + ex.Message);
            }
        }

        private static void DisposeStatusClient()
        {
            if (_statusClient == null)
            {
                return;
            }

            try
            {
                if (_statusClient.IsConnected)
                {
                    _statusClient.Disconnect();
                }
            }
            catch
            {
            }

            try
            {
                _statusClient.Dispose();
            }
            catch
            {
            }

            _statusClient = null;
        }

        public static void SafeStatus(string state, string msg)
        {
            try
            {
                EnsureStatusClient();
                PublishStatus(_statusClient, state, msg);
            }
            catch
            {
                // ignored
            }
        }

        public static string StatusJson(string state, string msg)
        {
            var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"ts\":\""); sb.Append(ts); sb.Append("\",");
            sb.Append("\"state\":\"");
            sb.Append(state);
            sb.Append("\"");
            if (!string.IsNullOrEmpty(msg))
            {
                sb.Append(',');
                sb.Append("\"msg\":\"");
                AppendEscaped(sb, msg);
                sb.Append("\"");
            }

            sb.Append('}');
            return sb.ToString();
        }

        public static string ExtractUrl(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }

            if (payload.StartsWith("http://") || payload.StartsWith("https://"))
            {
                return payload;
            }

            var u = JsonGetString(payload, "url");
            return (!string.IsNullOrEmpty(u) && (u.StartsWith("http://") || u.StartsWith("https://"))) ? u : null;
        }

        // --- JSON helpers ---
        public static string JsonGetString(string json, string key)
        {
            string pat = "\"" + key + "\"";
            int i = json.IndexOf(pat);
            if (i < 0)
            {
                return null;
            }

            i = json.IndexOf(':', i);
            if (i < 0)
            {
                return null;
            }

            while (i < json.Length && (json[i] == ':' || json[i] == ' '))
            {
                i++;
            }

            if (i >= json.Length)
            {
                return null;
            }

            if (json[i] == '"')
            {
                i++;
                int j = i;
                while (j < json.Length && json[j] != '"')
                {
                    j++;
                }

                return json.Substring(i, j - i);
            }

            int k = i;
            while (k < json.Length)
            {
                char c = json[k];
                if (c == ',' || c == '}' || c == ']')
                {
                    break;
                }

                k++;
            }

            return json.Substring(i, k - i).Trim();
        }

        public static string[] JsonArrayObjects(string json, string key)
        {
            string pat = "\"" + key + "\"";
            int i = json.IndexOf(pat);
            if (i < 0)
            {
                return null;
            }

            i = json.IndexOf('[', i);
            if (i < 0)
            {
                return null;
            }

            int end = json.IndexOf(']', i);
            if (end < 0)
            {
                end = json.Length - 1;
            }

            int p = i + 1; // after '['
            var vec = new StrVecStr();
            while (p < end)
            {
                // find next '{'
                int s = json.IndexOf('{', p);
                if (s < 0 || s >= end)
                {
                    break;
                }

                int depth = 0;
                int q = s;
                while (q < end)
                {
                    char c = json[q];
                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            q++;
                            break;
                        }
                    }

                    q++;
                }
                if (q <= end)
                {
                    vec.Add(json.Substring(s, q - s));
                    p = q + 1; // move after this object
                }
                else
                {
                    break;
                }
            }

            return vec.ToArray();
        }

        // --- Misc helpers ---
        public static int CmpVer(string a, string b)
        {
            if (a == null && b == null)
            {
                return 0;
            }

            if (a == null)
            {
                return -1;
            }

            if (b == null)
            {
                return 1;
            }

            var ap = a.Split('.');
            var bp = b.Split('.');
            int len = ap.Length > bp.Length ? ap.Length : bp.Length;
            for (int i = 0; i < len; i++)
            {
                int ai = i < ap.Length ? ToInt(ap[i]) : 0;
                int bi = i < bp.Length ? ToInt(bp[i]) : 0;
                if (ai != bi)
                {
                    return ai - bi;
                }
            }

            return 0;
        }

        private static void AppendEscaped(StringBuilder sb, string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                // Use numeric codes to avoid copy/paste escape issues
                if (c == (char)34 || c == (char)92)
                {
                    sb.Append((char)92);
                    sb.Append(c);
                }
                else if (c == (char)10)
                {
                    sb.Append((char)92);
                    sb.Append('n');
                }
                else if (c == (char)13)
                {
                    sb.Append((char)92);
                    sb.Append('r');
                }
                else if (c == (char)9)
                {
                    sb.Append((char)92);
                    sb.Append('t');
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        private static int ToInt(string s)
        {
            int v = 0, i = 0, sign = 1;
            if (s.Length > 0 && s[0] == '-')
            {
                sign = -1;
                i = 1;
            }

            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9')
                {
                    break;
                }

                v = v * 10 + (c - '0');
            }

            return v * sign;
        }

        private sealed class StrVecStr
        {
            private string[] _buf = new string[4];

            private int _count = 0;

            public void Add(string x)
            {
                if (this._count == this._buf.Length) this.Grow();
                this._buf[this._count++] = x;
            }

            public string[] ToArray()
            {
                var a = new string[this._count];
                for (int i = 0; i < this._count; i++)
                {
                    a[i] = this._buf[i];
                }

                return a;
            }

            private void Grow()
            {
                var n = this._buf.Length * 2;
                var nb = new string[n];
                for (int i = 0; i < this._buf.Length; i++)
                {
                    nb[i] = this._buf[i];
                }

                this._buf = nb;
            }
        }
    }
}
