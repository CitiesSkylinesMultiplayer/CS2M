using Colossal.Logging;
using System;

namespace CS2M
{
    public static class Log
    {
        public static ILog Logger { get; } = LogManager.GetLogger(Mod.Name)
            .SetShowsErrorsInUI(true)
            .SetEffectiveness(Level.Info)
            .SetLogStackTrace(false);

        public static void SetLoggingLevel(Level loggingLevel)
        {
            Logger.SetEffectiveness(loggingLevel);
        }

        public static void ErrorWithStackTrace(string message)
        {
            Logger.SetLogStackTrace(true);
            Logger.Error(message);
            Logger.SetLogStackTrace(false);
        }

        public static void Error(string message)
        {
            Logger.Error(message);
        }

        public static void Error(string message, Exception ex)
        {
            Logger.Error(ex, message);
        }

        public static void Warn(string message)
        {
            Logger.Warn(message);
        }

        public static void Info(string message)
        {
            Logger.Info(message);
        }

        public static void Debug(string message)
        {
            Logger.Debug(message);
        }

        public static void Trace(string message)
        {
            Logger.Trace(message);
        }
    }
}
