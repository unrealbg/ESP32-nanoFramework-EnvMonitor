namespace ESP32_NF_MQTT_DHT.Helpers
{
#if DEBUG
    using Microsoft.Extensions.Logging;

    using nanoFramework.Logging.Debug;
#endif
    using System;
        using System.Diagnostics;
    using System.Text;

    /// <summary>
    /// Helper class for logging messages optimized for nanoFramework memory constraints.
    /// </summary>
    public static class LogHelper
    {
#if DEBUG
        private static DebugLogger _logger = new DebugLogger("GlobalLogger");

        private const string INFO_COLOR = "\u001b[32m";
        private const string WARNING_COLOR = "\u001b[33m";
        private const string ERROR_COLOR = "\u001b[31m";
        private const string RESET_COLOR = "\u001b[0m";

        private static readonly object _builderLock = new object();
#endif

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        [Conditional("DEBUG")]
        public static void LogInformation(string message)
        {
#if DEBUG
            string formattedMessage = FormatMessage("INFO", message, INFO_COLOR);
            _logger.LogInformation(formattedMessage);
#endif
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="ex">The exception, if any.</param>
        public static void LogError(string message, Exception ex = null)
        {
#if DEBUG
            string formattedMessage;
            if (ex != null)
            {
                formattedMessage = FormatMessage("ERROR", message + " | Exception: " + ex.Message, ERROR_COLOR);
            }
            else
            {
                formattedMessage = FormatMessage("ERROR", message, ERROR_COLOR);
            }
            
            _logger.LogError(formattedMessage);
#else
                        if (ex != null)
                        {
                                Debug.WriteLine("[ERROR] " + message + " | Exception: " + ex.Message);
                        }
                        else
                        {
                                Debug.WriteLine("[ERROR] " + message);
                        }
#endif
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        [Conditional("DEBUG")]
        public static void LogWarning(string message)
        {
#if DEBUG
            string formattedMessage = FormatMessage("WARNING", message, WARNING_COLOR);
            _logger.LogWarning(formattedMessage);
#endif
        }

        /// <summary>
        /// Logs a debug message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        [Conditional("DEBUG")]
        public static void LogDebug(string message)
        {
#if DEBUG
            string formattedMessage = FormatMessage("DEBUG", message, RESET_COLOR);
            _logger.LogDebug(formattedMessage);
#endif
        }

#if DEBUG
        private static string FormatMessage(string level, string message, string color)
        {
            var now = DateTime.UtcNow;

            return "[" + now.Year + "-" + PadZero(now.Month) + "-" + PadZero(now.Day) + 
                   " " + PadZero(now.Hour) + ":" + PadZero(now.Minute) + ":" + PadZero(now.Second) + "] " +
                   color + "[" + level + "]" + RESET_COLOR + " " + message;
        }

        private static string PadZero(int num)
        {
            return num < 10 ? "0" + num.ToString() : num.ToString();
        }
#endif
    }
}