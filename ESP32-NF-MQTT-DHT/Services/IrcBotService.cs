namespace ESP32_NF_MQTT_DHT.Services
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Net.Sockets;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;

    using ESP32_NF_MQTT_DHT.Helpers;
    using ESP32_NF_MQTT_DHT.Services.Contracts;

    using nanoFramework.M2Mqtt;

    using static ESP32_NF_MQTT_DHT.Settings.IrcSettings;

    /// <summary>
    /// Minimal IRC bot built for nanoFramework memory constraints.
    /// Supports plain TCP and TLS using <see cref="MqttNetworkChannel"/>.
    /// </summary>
    internal sealed class IrcBotService : IIrcBotService, IDisposable
    {
        private const int ReconnectDelayMs = 5000;
        private const int RetryAfterSocketErrorMs = 1500;
        private const int ThreadJoinTimeoutMs = 1000;
        private const int IdleWaitMs = 250;
        private const int ReadTimeoutMs = 1000;
        private const int RegistrationTimeoutMs = 20000;
        private const int ClientKeepAliveMs = 30000;
        private const int MaxNickLength = 24;
        private const int MaxServerLinesToTracePerSession = 8;

        private readonly IConnectionService _connectionService;
        private readonly IInternetConnectionService _internetConnectionService;
        private readonly ISensorService _sensorService;
        private readonly IRelayService _relayService;
        private readonly IUptimeService _uptimeService;

        private readonly ManualResetEvent _stopSignal = new ManualResetEvent(false);
        private readonly ManualResetEvent _wakeSignal = new ManualResetEvent(false);
        private readonly WaitHandle[] _waitHandles;
        private readonly object _stateLock = new object();
        private readonly object _sessionLock = new object();
        private readonly byte[] _receiveBuffer = new byte[256];
        private readonly StringBuilder _lineBuffer = new StringBuilder();
        private readonly Queue _pendingLines = new Queue();

        private readonly EventHandler _connectionLostHandler;
        private readonly EventHandler _connectionRestoredHandler;
        private readonly EventHandler _internetLostHandler;
        private readonly EventHandler _internetRestoredHandler;

        private Thread _workerThread;
        private MqttNetworkChannel _channel;

        private bool _isRunning;
        private bool _isDisposed;
        private bool _startRequested;
        private bool _hasJoined;
        private bool _registrationAccepted;
        private int _nickRetryCount;
        private int _serverLinesTracedThisSession;
        private string _currentNick;
        private DateTime _connectedAtUtc;
        private DateTime _lastInboundAtUtc;
        private DateTime _lastOutboundAtUtc;

        public IrcBotService(
            IConnectionService connectionService,
            IInternetConnectionService internetConnectionService,
            ISensorService sensorService,
            IRelayService relayService,
            IUptimeService uptimeService)
        {
            _connectionService = connectionService ?? throw new ArgumentNullException(nameof(connectionService));
            _internetConnectionService = internetConnectionService ?? throw new ArgumentNullException(nameof(internetConnectionService));
            _sensorService = sensorService ?? throw new ArgumentNullException(nameof(sensorService));
            _relayService = relayService ?? throw new ArgumentNullException(nameof(relayService));
            _uptimeService = uptimeService ?? throw new ArgumentNullException(nameof(uptimeService));

            _connectionLostHandler = (s, e) => this.OnConnectivityChanged(false, "WiFi");
            _connectionRestoredHandler = (s, e) => this.OnConnectivityChanged(true, "WiFi");
            _internetLostHandler = (s, e) => this.OnConnectivityChanged(false, "Internet");
            _internetRestoredHandler = (s, e) => this.OnConnectivityChanged(true, "Internet");

            _connectionService.ConnectionLost += _connectionLostHandler;
            _connectionService.ConnectionRestored += _connectionRestoredHandler;
            _internetConnectionService.InternetLost += _internetLostHandler;
            _internetConnectionService.InternetRestored += _internetRestoredHandler;

            _waitHandles = new WaitHandle[] { _stopSignal, _wakeSignal };
        }

        public void Start()
        {
            this.ThrowIfDisposed();

            this.LogIrcState("Start requested. Server=" + Server + ", Port=" + Port + ", TLS=" + UseTls + ", Channel=" + Channel + ", Nick=" + this.GetInitialNick());

            if (!this.HasMinimumConfiguration())
            {
                return;
            }

            lock (_stateLock)
            {
                _startRequested = true;
                _isRunning = true;

                if (_workerThread != null && _workerThread.IsAlive)
                {
                    _wakeSignal.Set();
                    return;
                }

                _stopSignal.Reset();
                _wakeSignal.Reset();

                _workerThread = new Thread(this.RunWorker);
                _workerThread.Start();
            }

            _wakeSignal.Set();
            this.LogIrcState("Worker thread started.");
        }

        public void Stop()
        {
            if (_isDisposed)
            {
                return;
            }

            lock (_stateLock)
            {
                _startRequested = false;
                _isRunning = false;
            }

            _stopSignal.Set();
            _wakeSignal.Set();
            this.LogIrcState("Stop requested.");
            this.SafeDisconnect();

            lock (_stateLock)
            {
                if (_workerThread != null && _workerThread.IsAlive)
                {
                    _workerThread.Join(ThreadJoinTimeoutMs);
                }

                _workerThread = null;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                this.Stop();

                _connectionService.ConnectionLost -= _connectionLostHandler;
                _connectionService.ConnectionRestored -= _connectionRestoredHandler;
                _internetConnectionService.InternetLost -= _internetLostHandler;
                _internetConnectionService.InternetRestored -= _internetRestoredHandler;
            }
            finally
            {
                _isDisposed = true;
            }
        }

        private void RunWorker()
        {
            while (this.GetIsRunning())
            {
                if (!this.EnsureConnectivity())
                {
                    if (this.WaitForWake(ReconnectDelayMs))
                    {
                        break;
                    }

                    continue;
                }

                if (!this.IsSessionActive())
                {
                    if (!this.TryConnect())
                    {
                        if (this.WaitForWake(ReconnectDelayMs))
                        {
                            break;
                        }

                        continue;
                    }
                }

                try
                {
                    string line;
                    if (!this.TryReadLine(out line))
                    {
                        if (this.WaitForWake(IdleWaitMs))
                        {
                            break;
                        }

                        continue;
                    }

                    this.ProcessServerLine(line);
                }
                catch (IOException ex)
                {
                    if (this.GetIsRunning())
                    {
                        LogHelper.LogWarning("IRC IO error: " + ex.Message);
                    }

                    this.SafeDisconnect();

                    if (this.WaitForWake(RetryAfterSocketErrorMs))
                    {
                        break;
                    }
                }
                catch (SocketException ex)
                {
                    if (this.GetIsRunning())
                    {
                        LogHelper.LogWarning("IRC socket error: " + ex.Message);
                    }

                    this.SafeDisconnect();

                    if (this.WaitForWake(RetryAfterSocketErrorMs))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.LogError("IRC worker error: " + ex.Message, ex);
                    this.SafeDisconnect();

                    if (this.WaitForWake(RetryAfterSocketErrorMs))
                    {
                        break;
                    }
                }
            }

            this.SafeDisconnect();
        }

        private bool HasMinimumConfiguration()
        {
            if (string.IsNullOrEmpty(Server))
            {
                this.LogIrcState("Startup blocked: missing 'irc.server' in device.config.");
                return false;
            }

            if (Port <= 0)
            {
                this.LogIrcState("Startup blocked: 'irc.port' must be greater than zero.");
                return false;
            }

            return true;
        }

        private bool EnsureConnectivity()
        {
            try
            {
                if (!_connectionService.IsConnected)
                {
                    _connectionService.CheckConnection();

                    if (!_connectionService.IsConnected)
                    {
                        return false;
                    }
                }

                // Do not hard-block IRC on the generic internet probe.
                // The probe uses TCP/53 to public DNS and may fail on some networks even when the
                // target IRC server is reachable. The actual IRC connect attempt is a better signal.
                try
                {
                    if (!_internetConnectionService.IsInternetAvailable())
                    {
                        this.LogIrcState("Generic internet probe failed; attempting direct IRC connection anyway.");
                    }
                }
                catch (Exception ex)
                {
                    this.LogIrcState("Generic internet probe threw before IRC connect: " + ex.Message, ex);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.LogError("IRC connectivity check failed: " + ex.Message, ex);
                return false;
            }
        }

        private bool TryConnect()
        {
            try
            {
                _nickRetryCount = 0;
                _currentNick = this.GetInitialNick();

                bool requestedValidation = UseTls && ValidateServerCertificate;
                if (this.TryConnectCore(requestedValidation))
                {
                    return true;
                }

                if (requestedValidation)
                {
                    this.LogIrcState("Retrying TLS connection with server certificate validation disabled.");
                    return this.TryConnectCore(false);
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("Failed to connect IRC bot: " + ex.Message, ex);
            }

            return false;
        }

        private bool TryConnectCore(bool validateServerCertificate)
        {
            MqttNetworkChannel channel = null;

            try
            {
                channel = this.CreateNetworkChannel(validateServerCertificate);

                this.LogIrcState(
                    "Connecting IRC bot to " + Server + ":" + Port +
                    (UseTls ? " (TLS" + (validateServerCertificate ? ", validated" : ", unvalidated") + ")" : string.Empty) +
                    " as '" + _currentNick + "'.");

                channel.Connect();
                this.LogIrcState("Socket connected. Sending IRC registration commands.");

                lock (_sessionLock)
                {
                    _channel = channel;
                    _hasJoined = false;
                    _registrationAccepted = false;
                    _serverLinesTracedThisSession = 0;
                    _connectedAtUtc = DateTime.UtcNow;
                    _lastInboundAtUtc = _connectedAtUtc;
                    _lastOutboundAtUtc = _connectedAtUtc;
                    _lineBuffer.Length = 0;
                    _pendingLines.Clear();
                }

                channel = null;

                if (!string.IsNullOrEmpty(Password))
                {
                    if (!this.SendRaw("PASS " + Password))
                    {
                        throw new IOException("Failed to send PASS.");
                    }
                }

                if (!this.SendRaw("NICK " + _currentNick))
                {
                    throw new IOException("Failed to send NICK.");
                }

                if (!this.SendRaw("USER " + this.GetConfiguredUser() + " 0 * :" + this.GetConfiguredRealName()))
                {
                    throw new IOException("Failed to send USER.");
                }

                this.LogIrcState("Registration commands sent. Waiting for server greeting.");

                if (string.IsNullOrEmpty(Channel))
                {
                    this.LogIrcState("Connected without 'irc.channel'. The bot will only reply to direct messages.");
                }

                return true;
            }
            catch (Exception ex)
            {
                LogHelper.LogError("IRC connect attempt failed: " + ex.Message, ex);

                try
                {
                    channel?.Close();
                }
                catch
                {
                }

                this.SafeDisconnect();
                return false;
            }
        }

        private MqttNetworkChannel CreateNetworkChannel(bool validateServerCertificate)
        {
            if (!UseTls)
            {
                return new MqttNetworkChannel(Server, Port);
            }

            X509Certificate caCertificate = this.TryLoadCaCertificate();
            this.LogIrcState(
                "Preparing TLS channel. ValidateServerCertificate=" + validateServerCertificate +
                ", RequestedCaPath=" + CaCertificatePath +
                ", CaLoaded=" + (caCertificate != null));

            if (validateServerCertificate && caCertificate == null)
            {
                this.LogIrcState(
                    "IRC TLS requested but CA certificate was not found at '" + CaCertificatePath +
                    "'. Falling back to insecure TLS (server certificate validation disabled).");
                validateServerCertificate = false;
            }

            var channel = new MqttNetworkChannel(Server, Port, true, caCertificate, null, MqttSslProtocols.TLSv1_2);
            channel.ValidateServerCertificate = validateServerCertificate;

            if (!validateServerCertificate)
            {
                this.LogIrcState("TLS server certificate validation is disabled.");
            }

            return channel;
        }

        private X509Certificate TryLoadCaCertificate()
        {
            try
            {
                string certificatePath = this.ResolveCaCertificatePath();
                if (string.IsNullOrEmpty(certificatePath))
                {
                    return null;
                }

                if (!File.Exists(certificatePath))
                {
                    this.LogIrcState("CA certificate file not found at '" + certificatePath + "'.");
                    return null;
                }

                string pemText = File.ReadAllText(certificatePath);
                var certificate = this.TryLoadCertificateFromPem(pemText);
                if (certificate != null)
                {
                    this.LogIrcState("CA certificate loaded from '" + certificatePath + "'.");
                }

                return certificate;
            }
            catch (Exception ex)
            {
                this.LogIrcState("Failed to load IRC CA certificate: " + ex.Message, ex);
                return null;
            }
        }

        private string ResolveCaCertificatePath()
        {
            string configuredPath = CaCertificatePath;
            if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            string configFolderPath = @"I:\config\irc_root_ca.pem";
            if (File.Exists(configFolderPath))
            {
                this.LogIrcState("Using fallback CA path '" + configFolderPath + "'.");
                return configFolderPath;
            }

            return configuredPath;
        }

        private X509Certificate TryLoadCertificateFromPem(string pemText)
        {
            if (string.IsNullOrEmpty(pemText))
            {
                return null;
            }

            const string begin = "-----BEGIN CERTIFICATE-----";
            const string end = "-----END CERTIFICATE-----";

            int start = pemText.IndexOf(begin);
            if (start < 0)
            {
                return null;
            }

            int stop = pemText.IndexOf(end, start);
            if (stop < 0)
            {
                return null;
            }

            stop += end.Length;

            try
            {
                return new X509Certificate(pemText.Substring(start, stop - start));
            }
            catch (Exception ex)
            {
                LogHelper.LogWarning("Failed to parse IRC CA certificate PEM: " + ex.Message);
                return null;
            }
        }

        private bool IsSessionActive()
        {
            lock (_sessionLock)
            {
                return _channel != null;
            }
        }

        private bool TryReadLine(out string line)
        {
            line = null;

            if (this.TryDequeuePendingLine(out line))
            {
                return true;
            }

            MqttNetworkChannel channel;
            lock (_sessionLock)
            {
                channel = _channel;
            }

            if (channel == null)
            {
                this.CheckRegistrationTimeout();
                this.CheckClientKeepAlive();
                return false;
            }

            int read;
            try
            {
                read = channel.Receive(_receiveBuffer, ReadTimeoutMs);
            }
            catch (SocketException ex)
            {
                if (this.IsLikelyTimeout(ex))
                {
                    this.CheckRegistrationTimeout();
                    this.CheckClientKeepAlive();
                    return false;
                }

                throw;
            }
            catch (IOException ex)
            {
                if (this.IsLikelyTimeout(ex))
                {
                    this.CheckRegistrationTimeout();
                    this.CheckClientKeepAlive();
                    return false;
                }

                throw;
            }

            if (read <= 0)
            {
                this.CheckRegistrationTimeout();
                this.CheckClientKeepAlive();
                return false;
            }

            this.ProcessReceivedBytes(read);
            return this.TryDequeuePendingLine(out line);
        }

        private void CheckRegistrationTimeout()
        {
            bool shouldReconnect = false;

            lock (_sessionLock)
            {
                if (_channel == null || _registrationAccepted)
                {
                    return;
                }

                TimeSpan wait = DateTime.UtcNow - _connectedAtUtc;
                if (wait.TotalMilliseconds >= RegistrationTimeoutMs)
                {
                    shouldReconnect = true;
                }
            }

            if (shouldReconnect)
            {
                this.LogIrcState("No IRC greeting received within " + RegistrationTimeoutMs + "ms after connect. Reconnecting.");
                this.SafeDisconnect();
            }
        }

        private void CheckClientKeepAlive()
        {
            bool shouldSendKeepAlive = false;

            lock (_sessionLock)
            {
                if (_channel == null)
                {
                    return;
                }

                TimeSpan inboundIdle = DateTime.UtcNow - _lastInboundAtUtc;
                TimeSpan outboundIdle = DateTime.UtcNow - _lastOutboundAtUtc;

                if (inboundIdle.TotalMilliseconds >= ClientKeepAliveMs &&
                    outboundIdle.TotalMilliseconds >= ClientKeepAliveMs)
                {
                    shouldSendKeepAlive = true;
                }
            }

            if (shouldSendKeepAlive)
            {
                this.LogIrcState("No inbound traffic for " + ClientKeepAliveMs + "ms. Sending client keepalive PING " + Server + ".");
                this.SendRaw("PING " + Server);
            }
        }

        private void ProcessReceivedBytes(int count)
        {
            lock (_sessionLock)
            {
                for (int i = 0; i < count; i++)
                {
                    char ch = (char)_receiveBuffer[i];
                    if (ch == '\0')
                    {
                        continue;
                    }

                    if (ch == '\n')
                    {
                        string line = _lineBuffer.ToString();
                        _lineBuffer.Length = 0;

                        if (line.Length > 0 && line[line.Length - 1] == '\r')
                        {
                            line = line.Substring(0, line.Length - 1);
                        }

                        if (!string.IsNullOrEmpty(line))
                        {
                            _pendingLines.Enqueue(line);
                        }

                        continue;
                    }

                    _lineBuffer.Append(ch);
                }
            }
        }

        private bool TryDequeuePendingLine(out string line)
        {
            line = null;

            lock (_sessionLock)
            {
                if (_pendingLines.Count == 0)
                {
                    return false;
                }

                line = (string)_pendingLines.Dequeue();
                return true;
            }
        }

        private void ProcessServerLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            LogHelper.LogDebug("IRC << " + line);
            this.TraceServerLine(line);

            string prefix;
            string command;
            string parameters;

            if (!this.TryParseLine(line, out prefix, out command, out parameters))
            {
                return;
            }

            lock (_sessionLock)
            {
                _lastInboundAtUtc = DateTime.UtcNow;
            }

            if (command == "PING")
            {
                this.LogIrcState("Server PING received. Sending PONG.");
                this.SendRaw("PONG :" + this.TrimLeadingColon(parameters));
                return;
            }

            if (command == "PONG")
            {
                this.LogIrcState("Server PONG received.");
                return;
            }

            if (command == "433")
            {
                _nickRetryCount++;
                _currentNick = this.BuildRetryNick(this.GetInitialNick(), _nickRetryCount);
                this.LogIrcState("Nick already in use. Retrying with '" + _currentNick + "'.");
                this.SendRaw("NICK " + _currentNick);
                return;
            }

            if (command == "001")
            {
                lock (_sessionLock)
                {
                    _registrationAccepted = true;
                }

                this.LogIrcState("Server accepted registration (001).");
                this.JoinConfiguredChannel();
                return;
            }

            if ((command == "376") || (command == "422"))
            {
                this.LogIrcState("Server finished welcome sequence (" + command + ").");
                this.JoinConfiguredChannel();
                return;
            }

            if (command == "ERROR")
            {
                this.LogIrcState("Server sent ERROR: " + parameters);
                return;
            }

            if (command == "PRIVMSG")
            {
                this.HandlePrivMsg(prefix, parameters);
            }
        }

        private bool TryParseLine(string line, out string prefix, out string command, out string parameters)
        {
            prefix = null;
            command = null;
            parameters = string.Empty;

            int index = 0;

            if (line[0] == ':')
            {
                int prefixEnd = line.IndexOf(' ');
                if (prefixEnd <= 1)
                {
                    return false;
                }

                prefix = line.Substring(1, prefixEnd - 1);
                index = prefixEnd + 1;
            }

            while (index < line.Length && line[index] == ' ')
            {
                index++;
            }

            if (index >= line.Length)
            {
                return false;
            }

            int commandEnd = line.IndexOf(' ', index);
            if (commandEnd < 0)
            {
                command = line.Substring(index);
                return !string.IsNullOrEmpty(command);
            }

            command = line.Substring(index, commandEnd - index);
            parameters = commandEnd + 1 < line.Length ? line.Substring(commandEnd + 1) : string.Empty;
            return !string.IsNullOrEmpty(command);
        }

        private void JoinConfiguredChannel()
        {
            if (_hasJoined)
            {
                return;
            }

            string configuredChannel = Channel;
            if (string.IsNullOrEmpty(configuredChannel))
            {
                this.LogIrcState("No channel configured, skipping JOIN.");
                return;
            }

            if (this.SendRaw("JOIN " + configuredChannel))
            {
                _hasJoined = true;
                this.LogIrcState("JOIN sent for " + configuredChannel + ".");
            }
            else
            {
                this.LogIrcState("Failed to send JOIN for " + configuredChannel + ".");
            }
        }

        private void HandlePrivMsg(string prefix, string parameters)
        {
            if (string.IsNullOrEmpty(parameters))
            {
                return;
            }

            int separator = parameters.IndexOf(" :");
            if (separator <= 0)
            {
                return;
            }

            string target = parameters.Substring(0, separator).Trim();
            string message = parameters.Substring(separator + 2);
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(message))
            {
                return;
            }

            string prefixToken = this.GetCommandPrefix();
            if (!this.StartsWith(message, prefixToken))
            {
                return;
            }

            string senderNick = this.GetNickFromPrefix(prefix);
            string replyTarget = this.IsPrivateTarget(target) ? senderNick : target;
            string commandText = message.Substring(prefixToken.Length).Trim();

            if (string.IsNullOrEmpty(commandText))
            {
                return;
            }

            this.ExecuteCommand(senderNick, replyTarget, commandText);
        }

        private void ExecuteCommand(string senderNick, string replyTarget, string commandText)
        {
            try
            {
                string[] parts = commandText.Split(' ');
                if (parts == null || parts.Length == 0)
                {
                    return;
                }

                string command = parts[0].ToLower();
                switch (command)
                {
                    case "help":
                        this.Reply(replyTarget, "Commands: !help, !temp, !humidity, !status, !relay on|off|status, !uptime, !ip");
                        break;
                    case "temp":
                        this.Reply(replyTarget, "Temp: " + _sensorService.GetTemp() + " C");
                        break;
                    case "humidity":
                        this.Reply(replyTarget, "Humidity: " + _sensorService.GetHumidity() + " %");
                        break;
                    case "status":
                        this.Reply(
                            replyTarget,
                            "Temp: " + _sensorService.GetTemp() +
                            " C | Humidity: " + _sensorService.GetHumidity() +
                            " % | Relay: " + (_relayService.IsRelayOn() ? "ON" : "OFF") +
                            " | Uptime: " + _uptimeService.GetUptime());
                        break;
                    case "uptime":
                        this.Reply(replyTarget, "Uptime: " + _uptimeService.GetUptime());
                        break;
                    case "ip":
                        this.Reply(replyTarget, "IP: " + _connectionService.GetIpAddress());
                        break;
                    case "relay":
                        this.HandleRelayCommand(replyTarget, parts);
                        break;
                    default:
                        this.Reply(replyTarget, senderNick + ": unknown command. Try !help");
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.LogError("IRC command execution failed: " + ex.Message, ex);
                this.Reply(replyTarget, senderNick + ": command failed");
            }
        }

        private void HandleRelayCommand(string replyTarget, string[] parts)
        {
            if (parts.Length < 2)
            {
                this.Reply(replyTarget, "Usage: !relay on|off|status");
                return;
            }

            string relayCommand = parts[1].ToLower();
            if (relayCommand == "on")
            {
                _relayService.TurnOn();
                this.Reply(replyTarget, "Relay: ON");
                return;
            }

            if (relayCommand == "off")
            {
                _relayService.TurnOff();
                this.Reply(replyTarget, "Relay: OFF");
                return;
            }

            if (relayCommand == "status")
            {
                this.Reply(replyTarget, "Relay: " + (_relayService.IsRelayOn() ? "ON" : "OFF"));
                return;
            }

            this.Reply(replyTarget, "Usage: !relay on|off|status");
        }

        private void Reply(string target, string message)
        {
            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(message))
            {
                return;
            }

            this.SendRaw("PRIVMSG " + target + " :" + this.SanitizeMessage(message));
        }

        private bool SendRaw(string line)
        {
            MqttNetworkChannel channel;
            lock (_sessionLock)
            {
                channel = _channel;
            }

            if (channel == null)
            {
                return false;
            }

            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(line + "\r\n");
                int sent = channel.Send(payload);
                if (sent <= 0)
                {
                    throw new IOException("IRC channel send returned no data.");
                }

                if (this.StartsWith(line, "PASS "))
                {
                    LogHelper.LogDebug("IRC >> PASS ***");
                }
                else
                {
                    LogHelper.LogDebug("IRC >> " + line);
                }

                lock (_sessionLock)
                {
                    _lastOutboundAtUtc = DateTime.UtcNow;
                }

                if (!this.StartsWith(line, "PASS "))
                {
                    this.LogIrcState("Sent: " + this.TruncateForLog(line, 120));
                }

                return true;
            }
            catch (Exception ex)
            {
                this.LogIrcState("Send failed: " + ex.Message, ex);
                this.SafeDisconnect();
                return false;
            }
        }

        private void SafeDisconnect()
        {
            MqttNetworkChannel channel;

            lock (_sessionLock)
            {
                channel = _channel;

                _channel = null;
                _hasJoined = false;
                _registrationAccepted = false;
                _serverLinesTracedThisSession = 0;
                _connectedAtUtc = DateTime.MinValue;
                _lastInboundAtUtc = DateTime.MinValue;
                _lastOutboundAtUtc = DateTime.MinValue;
                _lineBuffer.Length = 0;
                _pendingLines.Clear();
            }

            try
            {
                channel?.Close();
            }
            catch
            {
            }
        }

        private void OnConnectivityChanged(bool restored, string source)
        {
            if (_isDisposed)
            {
                return;
            }

            if (!this.IsStartRequested())
            {
                return;
            }

            if (restored)
            {
                this.LogIrcState(source + " restored.");
                _wakeSignal.Set();
                this.Start();
                return;
            }

            this.LogIrcState(source + " lost.");
            this.SafeDisconnect();
            _wakeSignal.Set();
        }

        private void TraceServerLine(string line)
        {
            bool shouldLog = false;
            int tracedCount = 0;

            lock (_sessionLock)
            {
                if (_serverLinesTracedThisSession < MaxServerLinesToTracePerSession)
                {
                    _serverLinesTracedThisSession++;
                    tracedCount = _serverLinesTracedThisSession;
                    shouldLog = true;
                }
            }

            if (shouldLog)
            {
                this.LogIrcState("Server[" + tracedCount + "]: " + this.TruncateForLog(line, 160));
            }
        }

        private void LogIrcState(string message, Exception ex = null)
        {
            LogHelper.LogError("[IRC] " + message, ex);
        }

        private bool IsLikelyTimeout(Exception ex)
        {
            if (ex == null || string.IsNullOrEmpty(ex.Message))
            {
                return false;
            }

            string message = ex.Message.ToLower();
            return message.IndexOf("timeout") >= 0 || message.IndexOf("timed out") >= 0;
        }

        private bool WaitForWake(int timeoutMs)
        {
            if (timeoutMs < 250)
            {
                timeoutMs = 250;
            }

            int signaled = WaitHandle.WaitAny(_waitHandles, timeoutMs, false);
            if (signaled == 0)
            {
                return true;
            }

            if (signaled == 1)
            {
                _wakeSignal.Reset();
            }

            return false;
        }

        private bool GetIsRunning()
        {
            lock (_stateLock)
            {
                return _isRunning;
            }
        }

        private bool IsStartRequested()
        {
            lock (_stateLock)
            {
                return _startRequested;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(IrcBotService));
            }
        }

        private string GetInitialNick()
        {
            return this.SanitizeNick(Nick, "ESP32Bot");
        }

        private string GetConfiguredUser()
        {
            return this.SanitizeNick(User, "esp32");
        }

        private string GetConfiguredRealName()
        {
            string realName = RealName;
            return string.IsNullOrEmpty(realName) ? "ESP32 nanoFramework Bot" : realName;
        }

        private string BuildRetryNick(string baseNick, int attempt)
        {
            string suffix = attempt.ToString();
            if (suffix.Length >= MaxNickLength)
            {
                suffix = "1";
            }

            if (baseNick.Length > MaxNickLength - suffix.Length)
            {
                baseNick = baseNick.Substring(0, MaxNickLength - suffix.Length);
            }

            return baseNick + suffix;
        }

        private string SanitizeNick(string value, string fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < value.Length && builder.Length < MaxNickLength; i++)
            {
                char ch = value[i];
                if (this.IsNickChar(ch))
                {
                    builder.Append(ch);
                }
                else if (builder.Length > 0)
                {
                    builder.Append('_');
                }
            }

            return builder.Length == 0 ? fallback : builder.ToString();
        }

        private bool IsNickChar(char ch)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
            {
                return true;
            }

            return ch == '-' || ch == '_' || ch == '[' || ch == ']';
        }

        private string GetCommandPrefix()
        {
            string prefix = CommandPrefix;
            return string.IsNullOrEmpty(prefix) ? "!" : prefix;
        }

        private string GetNickFromPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return "unknown";
            }

            int separator = prefix.IndexOf('!');
            if (separator > 0)
            {
                return prefix.Substring(0, separator);
            }

            return prefix;
        }

        private bool IsPrivateTarget(string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                return false;
            }

            char first = target[0];
            return first != '#' && first != '&' && first != '+' && first != '!';
        }

        private bool StartsWith(string value, string prefix)
        {
            if (value == null || prefix == null)
            {
                return false;
            }

            if (prefix.Length > value.Length)
            {
                return false;
            }

            for (int i = 0; i < prefix.Length; i++)
            {
                if (value[i] != prefix[i])
                {
                    return false;
                }
            }

            return true;
        }

        private string TrimLeadingColon(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value[0] == ':' ? value.Substring(1) : value;
        }

        private string SanitizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();

            for (int i = 0; i < message.Length; i++)
            {
                char ch = message[i];
                if (ch == '\r' || ch == '\n')
                {
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        private string TruncateForLog(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            if (maxLength < 4)
            {
                return value.Substring(0, maxLength);
            }

            return value.Substring(0, maxLength - 3) + "...";
        }
    }
}
