namespace CS2M.Lobby;

/// <summary>Settings from environment variables; see the README.</summary>
public sealed class LobbyConfig
{
    public int UdpPort { get; init; } = 4242;
    public int HttpPort { get; init; } = 8080;
    public string HttpBind { get; init; } = "+";
    public TimeSpan SessionTtl { get; init; } = TimeSpan.FromSeconds(60);
    public int MaxSessions { get; init; } = 1000;
    public int MaxSessionsPerIp { get; init; } = 5;
    public int MaxPendingSessions { get; init; } = 200;
    public int MaxSessionsPerSubnet { get; init; } = 20;
    public bool LogIps { get; init; }
    public int UdpPacketsPerSecond { get; init; } = 2000;
    public int IntroTokensPerIp { get; init; } = 5;
    public int IntroTokensPerSubnet { get; init; } = 20;
    public int IntrosPerSession { get; init; } = 30;
    public int IntrosPerSecond { get; init; } = 50;
    public int PortChecksPerSecond { get; init; } = 5;
    public int MaxPortChecksInFlight { get; init; } = 32;
    public bool AllowPrivatePeers { get; init; }
    public TimeSpan TombstoneTtl { get; init; } = TimeSpan.FromHours(6);
    public int HttpConcurrency { get; init; } = 32;

    public static LobbyConfig FromEnvironment()
    {
        return new LobbyConfig
        {
            UdpPort = Int("LOBBY_UDP_PORT", 4242, 1, 65535),
            HttpPort = Int("LOBBY_HTTP_PORT", 8080, 1, 65535),
            HttpBind = Environment.GetEnvironmentVariable("LOBBY_HTTP_BIND") is { Length: > 0 } bind ? bind : "+",
            SessionTtl = TimeSpan.FromSeconds(Int("LOBBY_SESSION_TTL", 60, 10, 3600)),
            MaxSessions = Int("LOBBY_MAX_SESSIONS", 1000, 1, 100000),
            MaxSessionsPerIp = Int("LOBBY_MAX_SESSIONS_PER_IP", 5, 1, 1000),
            MaxPendingSessions = Int("LOBBY_MAX_PENDING_SESSIONS", 200, 1, 100000),
            MaxSessionsPerSubnet = Int("LOBBY_MAX_SESSIONS_PER_SUBNET", 20, 1, 100000),
            LogIps = Flag("LOBBY_LOG_IPS"),
            UdpPacketsPerSecond = Int("LOBBY_UDP_PPS", 2000, 10, 1000000),
            IntroTokensPerIp = Int("LOBBY_INTRO_TOKENS_PER_IP", 5, 1, 1000),
            IntroTokensPerSubnet = Int("LOBBY_INTRO_TOKENS_PER_SUBNET", 20, 1, 1000),
            IntrosPerSession = Int("LOBBY_INTROS_PER_SESSION", 30, 1, 100000),
            IntrosPerSecond = Int("LOBBY_INTROS_PER_SECOND", 50, 1, 100000),
            PortChecksPerSecond = Int("LOBBY_PORT_CHECKS_PER_SECOND", 5, 1, 1000),
            MaxPortChecksInFlight = Int("LOBBY_PORT_CHECKS_IN_FLIGHT", 32, 1, 1000),
            AllowPrivatePeers = Flag("LOBBY_ALLOW_PRIVATE_PEERS"),
            TombstoneTtl = TimeSpan.FromHours(Int("LOBBY_TOMBSTONE_HOURS", 6, 0, 168)),
            HttpConcurrency = Int("LOBBY_HTTP_CONCURRENCY", 32, 1, 1024)
        };
    }

    private static bool Flag(string name)
    {
        return Environment.GetEnvironmentVariable(name) is "1" or "true";
    }

    private static int Int(string name, int fallback, int min, int max)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        if (int.TryParse(value, out int result) && result >= min && result <= max)
        {
            return result;
        }

        throw new ArgumentException($"{name} must be a number from {min} to {max}");
    }
}
