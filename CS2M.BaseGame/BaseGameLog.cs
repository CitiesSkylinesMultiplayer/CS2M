using Colossal.Logging;

namespace CS2M.BaseGame
{
    /// <summary>
    ///     Writes to the same log as the main mod (CS2M.log).
    /// </summary>
    internal static class BaseGameLog
    {
        private static readonly ILog Logger = LogManager.GetLogger("CS2M");

        public static void Info(string message) => Logger.Info(message);

        public static void Warn(string message) => Logger.Warn(message);
    }
}
