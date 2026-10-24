namespace CS2M.Lobby;

/// <summary>One line per event on stdout, for docker logs.</summary>
public static class Log
{
    private static readonly object Lock = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        lock (Lock)
        {
            Console.Out.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {level} {message}");
            Console.Out.Flush();
        }
    }
}
