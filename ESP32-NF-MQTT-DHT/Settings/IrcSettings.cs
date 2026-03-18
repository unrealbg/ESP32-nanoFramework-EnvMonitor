namespace ESP32_NF_MQTT_DHT.Settings
{
    using ESP32_NF_MQTT_DHT.Helpers;

    /// <summary>
    /// Runtime IRC configuration loaded from <c>I:\config\device.config</c>.
    /// </summary>
    public static class IrcSettings
    {
        public static string Server => DeviceConfig.GetString("irc.server", "irc.server.com");

        public static int Port => DeviceConfig.GetInt32("irc.port", 6667);

        public static bool UseTls => DeviceConfig.GetBoolean("irc.tls", Port == 6697);

        public static string Channel => DeviceConfig.GetString("irc.channel", "#bws");

        public static string Nick => DeviceConfig.GetString("irc.nick", DeviceSettings.DeviceName);

        public static string User => DeviceConfig.GetString("irc.user", "esp32");

        public static string RealName => DeviceConfig.GetString("irc.realname", DeviceSettings.DeviceName);

        public static string Password => DeviceConfig.GetString("irc.pass", string.Empty);

        public static string CommandPrefix => DeviceConfig.GetString("irc.cmdprefix", "!");

        public static bool ValidateServerCertificate => DeviceConfig.GetBoolean("irc.validateServerCertificate", true);

        public static string CaCertificatePath => DeviceConfig.GetString("irc.ca.path", @"I:\irc_root_ca.pem");
    }
}
