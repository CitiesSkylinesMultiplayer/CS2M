using System;
using Colossal.Logging;
using System.Diagnostics;

namespace CS2M
{
    public static class Log
    {
        public static ILog Logger { get; } = LogManager.GetLogger(nameof(CS2M))
            .SetShowsErrorsInUI(true).SetEffectiveness(Level.Info).SetLogStackTrace(false);

        public static void SetLoggingLevel(Level loggingLevel)
        {
            Logger.SetEffectiveness(loggingLevel);
        }

        public static void ErrorWithStackTrace(string message)
        {
            Logger.SetLogStackTrace(true);
            Logger.Error(message);
            Logger.SetLogStackTrace(false);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Error(string message)
        {
            Logger.Error(message);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Error(string message, Exception ex)
        {
            Logger.Error(ex, message);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Warn(string message)
        {
            Logger.Warn(message);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Info(string message)
        {
            Logger.Info(message);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Debug(string message)
        {
            Logger.Debug(message);
            System.Diagnostics.Debug.Print(message);
        }
    }
}
