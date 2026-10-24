using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CS2M.Lobby;

public sealed class Session
{
    public required string Token { get; init; }

    /// <summary>The host's game socket as this server sees it. Never leaves the server except in a NAT introduction.</summary>
    public required IPEndPoint External { get; set; }

    /// <summary>The host's LAN address, for joiners behind the same router.</summary>
    public IPEndPoint? Internal { get; set; }

    public string? CityName { get; set; }
    public string? HostName { get; set; }
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public bool HasPassword { get; set; }
    public string? GameVersion { get; set; }
    public string? ModVersion { get; set; }
    public DateTime LastSeen { get; set; }

    /// <summary>From SessionDetailsCommand; null until the host sends one (older mods never do).</summary>
    public SessionDetailsView? Details { get; set; }

    /// <summary>SHA-256 of the host's TokenSecret, or null for a host that sent none (older mods).</summary>
    public byte[]? SecretHash { get; init; }

    /// <summary>
    ///     Keep-alives after the first registration; at <see cref="SessionRegistry.ConfirmAfterRefreshes" /> the
    ///     session counts as confirmed.
    /// </summary>
    public int Refreshes { get; set; }

    public bool Confirmed => Refreshes >= SessionRegistry.ConfirmAfterRefreshes;

    // A host without a secret moves to a new port only after it keeps using it (see SessionRegistry.MovePort)
    public int PendingPort { get; set; }
    public int PendingPortHits { get; set; }
    public DateTime PendingPortSince { get; set; }

    public DateTime IntroWindow { get; set; }
    public int IntroCount { get; set; }
    public int PortChecks { get; set; }
}

/// <summary>Who owned a token that expired or was closed, so nobody else can take it over for a while.</summary>
internal sealed record Tombstone(string Token, byte[]? SecretHash, IPAddress Owner, DateTime Expires);

/// <summary>The host's endpoints for a NAT introduction.</summary>
public sealed record HostEndpoints(IPEndPoint External, IPEndPoint? Internal, string Token);

public enum IntroduceResult
{
    Ok,
    Unknown,
    SessionLimited
}

public enum PortCheckStatus
{
    /// <summary>No session at exactly that address and port (yet).</summary>
    NoSession,

    /// <summary>The session hasn't sent enough keep-alives yet.</summary>
    NotConfirmed,
    Ready,

    /// <summary>The session already had its port checks.</summary>
    Exhausted
}

public sealed record PlayerView(string Name, string? Color);

public sealed record ModView(string Id, string? Name, string? Version, bool ClientSide);

/// <summary>
///     The extra details a host sends. Month is 0-11. ModCount can be larger than Mods when the host's list was cut
///     short to fit a packet; it lists required mods first.
/// </summary>
public sealed record SessionDetailsView(
    int Population, int Milestone, int Year, int Month, IReadOnlyList<PlayerView> PlayerList,
    IReadOnlyList<ModView> Mods, int ModCount, DateTime Updated);

/// <summary>Public view of a session for the HTTP API. Has no addresses on purpose.</summary>
public sealed record SessionView(
    string Token, string? Name, string? Host, int Players, int MaxPlayers, bool HasPassword,
    string? GameVersion, string? ModVersion, DateTime LastSeen, SessionDetailsView? Details);

public enum RegisterResult
{
    Added,
    Refreshed,
    Invalid,
    TokenTaken,
    Full,

    /// <summary>A host without a secret sent from a new port; held until it keeps using it.</summary>
    PortChangeHeld
}

/// <summary>Live sessions by token. Thread safe: the UDP side writes, the HTTP API reads.</summary>
public sealed partial class SessionRegistry
{
    public const int ConfirmAfterRefreshes = 2;
    public const int MaxPortChecksPerSession = 2;
    private const int MaxTombstones = 50_000;
    private const int PortMoveHits = 3;
    private static readonly TimeSpan PortMoveAfter = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IntroWindowLength = TimeSpan.FromMinutes(1);

    private readonly LobbyConfig _config;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tombstone> _tombstones = new(StringComparer.Ordinal);
    private readonly Queue<Tombstone> _tombstoneOrder = new();
    private readonly object _lock = new();

    public SessionRegistry(LobbyConfig config)
    {
        _config = config;
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Count;
            }
        }
    }

    public int ConfirmedCount
    {
        get
        {
            lock (_lock)
            {
                return _sessions.Values.Count(s => s.Confirmed);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{4,64}$")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{22,128}$")]
    private static partial Regex SecretPattern();

    public static bool IsValidToken(string? token)
    {
        return token != null && TokenPattern().IsMatch(token);
    }

    /// <summary>
    ///     A host secret: 22 to 128 characters of A-Z, a-z, 0-9, '-' and '_' (base64url or hex of at least 16
    ///     random bytes). Null or empty means the host sent none.
    /// </summary>
    public static bool IsValidSecret(string? secret)
    {
        return secret != null && SecretPattern().IsMatch(secret);
    }

    private static byte[] HashSecret(string secret)
    {
        return SHA256.HashData(Encoding.ASCII.GetBytes(secret));
    }

    private static bool SecretMatches(byte[]? stored, byte[]? given)
    {
        return stored != null && given != null && CryptographicOperations.FixedTimeEquals(stored, given);
    }

    /// <summary>
    ///     A keep-alive (SessionInfo, or the older ServerRegistration with <paramref name="info" /> null).
    ///     Ownership: a session registered with a secret only accepts that secret, from any address. One without
    ///     a secret accepts its own address, and moves to a new port only once the host keeps using it.
    /// </summary>
    public RegisterResult Register(IPEndPoint external, string? token, string? secret, string? localIp, int localPort,
        Protocol.SessionInfoMessage? info)
    {
        if (!IsValidToken(token))
        {
            return RegisterResult.Invalid;
        }

        byte[]? secretHash = null;
        if (!string.IsNullOrEmpty(secret))
        {
            if (!IsValidSecret(secret))
            {
                return RegisterResult.Invalid;
            }

            secretHash = HashSecret(secret);
        }

        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            RegisterResult result;
            if (_sessions.TryGetValue(token!, out Session? session) && IsExpired(session, now))
            {
                Remove(session, now);
                session = null;
            }

            if (session != null)
            {
                if (session.SecretHash != null)
                {
                    if (!SecretMatches(session.SecretHash, secretHash))
                    {
                        return RegisterResult.TokenTaken;
                    }

                    // The secret proves it's the host, so its address may change too (new ISP lease, roaming)
                    session.External = external;
                }
                else
                {
                    // No secret: the address must stay, and the port only moves when the host keeps using the new one
                    if (!session.External.Address.Equals(external.Address))
                    {
                        return RegisterResult.TokenTaken;
                    }

                    if (session.External.Port != external.Port)
                    {
                        if (!MovePort(session, external, now))
                        {
                            return RegisterResult.PortChangeHeld;
                        }
                    }
                    else
                    {
                        session.PendingPort = 0;
                        session.PendingPortHits = 0;
                    }
                }

                session.Refreshes = Math.Min(session.Refreshes + 1, 1_000_000);
                if (session.Refreshes == ConfirmAfterRefreshes &&
                    _sessions.Values.Count(s => s.Confirmed && !IsExpired(s, now)) > _config.MaxSessions)
                {
                    // Only confirmed sessions count toward the limit; this one just became one too many
                    _sessions.Remove(session.Token);
                    return RegisterResult.Full;
                }

                result = RegisterResult.Refreshed;
            }
            else
            {
                if (_tombstones.TryGetValue(token!, out Tombstone? tomb))
                {
                    if (tomb.Expires > now &&
                        !(tomb.SecretHash != null
                            ? SecretMatches(tomb.SecretHash, secretHash)
                            : tomb.Owner.Equals(external.Address)))
                    {
                        return RegisterResult.TokenTaken;
                    }

                    _tombstones.Remove(token!);
                }

                int pending = 0, sameAddress = 0, sameSubnet = 0;
                ulong subnet = Net.SubnetKey(external.Address);
                foreach (Session s in _sessions.Values)
                {
                    if (IsExpired(s, now))
                    {
                        continue;
                    }

                    if (!s.Confirmed)
                    {
                        pending++;
                    }

                    if (s.External.Address.Equals(external.Address))
                    {
                        sameAddress++;
                    }

                    if (Net.SubnetKey(s.External.Address) == subnet)
                    {
                        sameSubnet++;
                    }
                }

                if (pending >= _config.MaxPendingSessions || sameAddress >= _config.MaxSessionsPerIp ||
                    sameSubnet >= _config.MaxSessionsPerSubnet)
                {
                    return RegisterResult.Full;
                }

                session = new Session { Token = token!, External = external, SecretHash = secretHash };
                _sessions[token!] = session;
                result = RegisterResult.Added;
            }

            session.LastSeen = now;
            session.Internal = ParseInternal(localIp, localPort);
            if (info != null)
            {
                session.CityName = Clean(info.CityName, 64);
                session.HostName = Clean(info.HostName, 32);
                session.PlayerCount = Math.Clamp(info.PlayerCount, 0, 10000);
                session.MaxPlayers = Math.Clamp(info.MaxPlayers, 0, 10000);
                session.HasPassword = info.HasPassword;
                session.GameVersion = Clean(info.GameVersion, 32);
                session.ModVersion = Clean(info.ModVersion, 32);
            }

            return result;
        }
    }

    /// <summary>
    ///     A host without a secret whose router gave it a new port: switch once the new port was seen
    ///     <see cref="PortMoveHits" /> times over at least <see cref="PortMoveAfter" /> with no keep-alive from the
    ///     old port in between (that resets it). A single spoofed packet can't re-point a session that way, as the
    ///     real host keeps refreshing from the old port every 10 s.
    /// </summary>
    private static bool MovePort(Session session, IPEndPoint external, DateTime now)
    {
        if (session.PendingPort != external.Port)
        {
            session.PendingPort = external.Port;
            session.PendingPortHits = 1;
            session.PendingPortSince = now;
            return false;
        }

        session.PendingPortHits++;
        if (session.PendingPortHits < PortMoveHits || now - session.PendingPortSince < PortMoveAfter)
        {
            return false;
        }

        session.External = external;
        session.PendingPort = 0;
        session.PendingPortHits = 0;
        return true;
    }

    /// <summary>
    ///     Stores a host's SessionDetails. Only for a live session, from exactly the address and port it
    ///     registered from (the mod sends both from its game socket).
    /// </summary>
    public bool UpdateDetails(IPEndPoint external, Protocol.SessionDetailsMessage details)
    {
        if (!IsValidToken(details.Token))
        {
            return false;
        }

        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(details.Token!, out Session? session) || IsExpired(session, now) ||
                !session.External.Equals(external))
            {
                return false;
            }

            List<ModView> mods = details.Mods
                .Select(m => new ModView(Clean(m.Id, 48) ?? "?", Clean(m.Name, 40), Clean(m.Version, 20),
                    m.ClientSide))
                .ToList();
            if (details.ModOffset > 0)
            {
                // The rest of a long mod list: only right after the part before it, so pages never mix
                if (session.Details == null || session.Details.Mods.Count != details.ModOffset)
                {
                    return false;
                }

                session.Details = session.Details with
                {
                    Mods = session.Details.Mods.Concat(mods).Take(Protocol.MaxListedMods).ToList(),
                    Updated = now
                };
                return true;
            }

            session.Details = new SessionDetailsView(
                Math.Clamp(details.Population, 0, 100_000_000),
                Math.Clamp(details.Milestone, 0, 1000),
                Math.Clamp(details.Year, 0, 100_000),
                Math.Clamp(details.Month, 0, 11),
                details.Players.Take(Protocol.MaxListedPlayers)
                    .Select(p => new PlayerView(Clean(p.Name, 32) ?? "?", CleanColor(p.Color)))
                    .ToList(),
                mods.Take(Protocol.MaxListedMods).ToList(),
                Math.Clamp(details.ModCount, 0, 10000),
                now);
            return true;
        }
    }

    /// <summary>
    ///     Closes a session: with its secret if it has one, otherwise only from exactly its address and port.
    /// </summary>
    public bool Unregister(IPEndPoint external, string? token, string? secret)
    {
        if (!IsValidToken(token))
        {
            return false;
        }

        byte[]? secretHash = IsValidSecret(secret) ? HashSecret(secret!) : null;
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(token!, out Session? session))
            {
                return false;
            }

            bool owner = session.SecretHash != null
                ? SecretMatches(session.SecretHash, secretHash)
                : session.External.Equals(external);
            if (!owner)
            {
                return false;
            }

            Remove(session, now, true);
            return true;
        }
    }

    /// <summary>
    ///     The host for a NAT introduction, by token or by its exact public address and port, counting toward the
    ///     session's limit of introductions per minute.
    /// </summary>
    public IntroduceResult Introduce(string? token, IPEndPoint? exactExternal, out HostEndpoints? host)
    {
        host = null;
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            Session? session = null;
            if (token != null)
            {
                _sessions.TryGetValue(token, out session);
            }
            else if (exactExternal != null)
            {
                session = _sessions.Values
                    .Where(s => s.External.Equals(exactExternal))
                    .MaxBy(s => s.LastSeen);
            }

            if (session == null || IsExpired(session, now))
            {
                return IntroduceResult.Unknown;
            }

            if (now - session.IntroWindow > IntroWindowLength)
            {
                session.IntroWindow = now;
                session.IntroCount = 0;
            }

            if (session.IntroCount >= _config.IntrosPerSession)
            {
                return IntroduceResult.SessionLimited;
            }

            session.IntroCount++;
            host = new HostEndpoints(session.External, session.Internal, session.Token);
            return IntroduceResult.Ok;
        }
    }

    /// <summary>Whether the host at exactly <paramref name="requester" /> may have its port checked now.</summary>
    public PortCheckStatus GetPortCheckStatus(IPEndPoint requester)
    {
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            PortCheckStatus best = PortCheckStatus.NoSession;
            foreach (Session s in _sessions.Values)
            {
                if (IsExpired(s, now) || !s.External.Equals(requester))
                {
                    continue;
                }

                PortCheckStatus status = !s.Confirmed ? PortCheckStatus.NotConfirmed
                    : s.PortChecks >= MaxPortChecksPerSession ? PortCheckStatus.Exhausted
                    : PortCheckStatus.Ready;
                if (status == PortCheckStatus.Ready)
                {
                    return status;
                }

                if (best == PortCheckStatus.NoSession || status == PortCheckStatus.NotConfirmed)
                {
                    best = status;
                }
            }

            return best;
        }
    }

    /// <summary>Counts a port check against a confirmed session at exactly <paramref name="requester" />.</summary>
    public bool TakePortCheck(IPEndPoint requester)
    {
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            Session? session = _sessions.Values.FirstOrDefault(s =>
                !IsExpired(s, now) && s.External.Equals(requester) && s.Confirmed &&
                s.PortChecks < MaxPortChecksPerSession);
            if (session == null)
            {
                return false;
            }

            session.PortChecks++;
            return true;
        }
    }

    public int RemoveExpired()
    {
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            List<Session> expired = _sessions.Values.Where(s => IsExpired(s, now)).ToList();
            foreach (Session session in expired)
            {
                Remove(session, now);
            }

            while (_tombstoneOrder.TryPeek(out Tombstone? oldest) && oldest.Expires <= now)
            {
                _tombstoneOrder.Dequeue();
                if (_tombstones.TryGetValue(oldest.Token, out Tombstone? current) && ReferenceEquals(current, oldest))
                {
                    _tombstones.Remove(oldest.Token);
                }
            }

            return expired.Count;
        }
    }

    /// <summary>
    ///     Drops a session and remembers its owner, for confirmed sessions and for ones the host closed, so the
    ///     token can't be taken over right after. Caller holds the lock.
    /// </summary>
    private void Remove(Session session, DateTime now, bool closedByHost = false)
    {
        _sessions.Remove(session.Token);
        if (_config.TombstoneTtl <= TimeSpan.Zero || !(session.Confirmed || closedByHost))
        {
            return;
        }

        var tomb = new Tombstone(session.Token, session.SecretHash, session.External.Address,
            now + _config.TombstoneTtl);
        _tombstones[session.Token] = tomb;
        _tombstoneOrder.Enqueue(tomb);
        while (_tombstoneOrder.Count > MaxTombstones)
        {
            Tombstone oldest = _tombstoneOrder.Dequeue();
            if (_tombstones.TryGetValue(oldest.Token, out Tombstone? current) && ReferenceEquals(current, oldest))
            {
                _tombstones.Remove(oldest.Token);
            }
        }
    }

    public List<SessionView> Snapshot()
    {
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            return _sessions.Values
                .Where(s => !IsExpired(s, now))
                .OrderByDescending(s => s.PlayerCount)
                .ThenBy(s => s.CityName ?? "\uFFFF", StringComparer.OrdinalIgnoreCase) // unnamed last
                .Select(s => new SessionView(s.Token, s.CityName, s.HostName, s.PlayerCount, s.MaxPlayers,
                    s.HasPassword, s.GameVersion, s.ModVersion, s.LastSeen, s.Details))
                .ToList();
        }
    }

    private bool IsExpired(Session session, DateTime now)
    {
        return now - session.LastSeen > _config.SessionTtl;
    }

    /// <summary>The host's LAN endpoint: only a private IPv4 address (see <see cref="Net.IsLan" />).</summary>
    private static IPEndPoint? ParseInternal(string? localIp, int localPort)
    {
        if (localPort is <= 0 or > 65535 || !IPAddress.TryParse(localIp, out IPAddress? address) ||
            !Net.IsLan(address))
        {
            return null;
        }

        return new IPEndPoint(address.MapToIPv4(), localPort);
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();

    private static string? CleanColor(string? color)
    {
        return color != null && ColorPattern().IsMatch(color) ? color.ToUpperInvariant() : null;
    }

    /// <summary>
    ///     Trims and caps the length of a name shown to other players. Drops control and format characters
    ///     (bidi overrides, zero-width), line and paragraph separators, private use and unpaired surrogates, and
    ///     stacks of more than two combining marks.
    /// </summary>
    public static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        int marks = 0;
        for (int i = 0; i < value.Length && builder.Length < maxLength; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    UnicodeCategory pair = CharUnicodeInfo.GetUnicodeCategory(value, i);
                    if (pair is not (UnicodeCategory.Format or UnicodeCategory.PrivateUse) &&
                        builder.Length + 2 <= maxLength)
                    {
                        builder.Append(c).Append(value[i + 1]);
                        marks = 0;
                    }

                    i++;
                }

                continue;
            }

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            switch (category)
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.OtherNotAssigned:
                    continue;
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.EnclosingMark:
                    if (++marks > 2 || builder.Length == 0)
                    {
                        continue;
                    }

                    break;
                default:
                    marks = 0;
                    break;
            }

            builder.Append(c);
        }

        string result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }
}
