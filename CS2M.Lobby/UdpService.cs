using System.Diagnostics;
using System.Net;
using LiteNetLib;

namespace CS2M.Lobby;

/// <summary>
///     The UDP side: the mod's API messages (unconnected LiteNetLib messages) and NAT introductions
///     (LiteNetLib's NatPunchModule).
///     <para>
///         The main socket runs with LiteNetLib's unsynced events, so messages are handled on its receive thread as
///         they arrive instead of piling up in LiteNetLib's unbounded event queues. The loop in <see cref="Run" />
///         does the port check probes and the periodic work. Both take <see cref="_gate" />, so everything here
///         runs one thing at a time.
///     </para>
///     <para>
///         Every limit has a fixed size: a global packet budget is checked first, before any per-address state or
///         decoding, and the per-address limiters are fixed tables (<see cref="WindowLimiter" />,
///         <see cref="DistinctLimiter" />), so spoofed source addresses can't grow the heap.
///     </para>
/// </summary>
public sealed class UdpService : INatPunchListener
{
    // Port checks connect with a key the game never accepts: a rejection proves the port is open.
    private const string PortCheckKey = "CS2M-LOBBY-PORTCHECK";

    // Sent from the probe socket to our own port, so /health notices when the receive side stops working
    private static readonly byte[] SelfTestPayload = "CS2M-LOBBY-SELFTEST"u8.ToArray();

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SelfTestInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StatsInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PendingPortCheckTtl = TimeSpan.FromSeconds(45);
    private const int MaxPendingPortChecks = 64;
    private const int MaxIntroduceTokenLength = 96;

    private readonly LobbyConfig _config;
    private readonly SessionRegistry _registry;
    private readonly NetManager _net;
    private readonly object _gate = new();

    // A second socket on a random port, so a port check is a fresh inbound packet for the host's router
    // rather than a reply on the flow the host opened to us.
    private readonly NetManager _probe;

    // Probes on the wire, by target; and requests waiting for their session to be confirmed, by requester
    private readonly Dictionary<IPEndPoint, List<IPEndPoint>> _portChecks = new();
    private readonly Dictionary<IPEndPoint, (int Port, DateTime Expires)> _pendingPortChecks = new();

    private readonly TokenBucket _packetBudget;
    private readonly TokenBucket _introBudget;
    private readonly TokenBucket _portCheckBudget;
    private readonly TokenBucket _eventLogBudget = new(10.0 / 60, 10);
    private readonly WindowLimiter _messageLimit = new(40, TimeSpan.FromSeconds(10));
    private readonly WindowLimiter _introduceLimit = new(10, TimeSpan.FromSeconds(10));
    private readonly WindowLimiter _portCheckLimit = new(2, TimeSpan.FromSeconds(30), 4096);
    private readonly DistinctLimiter _introPerIp;
    private readonly DistinctLimiter _introPerSubnet;
    private readonly UdpStats _stats = new();

    private long _lastLoop = Stopwatch.GetTimestamp();
    private long _lastSelfTest;
    private readonly long _started = Stopwatch.GetTimestamp();

    public UdpService(LobbyConfig config, SessionRegistry registry)
    {
        // LiteNetLib writes a console line for every malformed packet unless it has a logger: count them instead
        NetDebug.Logger = new CountingLogger(_stats);
        _config = config;
        _registry = registry;
        _packetBudget = new TokenBucket(config.UdpPacketsPerSecond, config.UdpPacketsPerSecond * 2.0);
        _introBudget = new TokenBucket(config.IntrosPerSecond, config.IntrosPerSecond * 2.0);
        _portCheckBudget = new TokenBucket(config.PortChecksPerSecond, config.PortChecksPerSecond);
        _introPerIp = new DistinctLimiter(config.IntroTokensPerIp, TimeSpan.FromHours(1), 8192);
        _introPerSubnet = new DistinctLimiter(config.IntroTokensPerSubnet, TimeSpan.FromHours(1), 4096);

        var listener = new EventBasedNetListener();
        listener.ConnectionRequestEvent += request => request.Reject();
        listener.NetworkReceiveUnconnectedEvent += OnUnconnectedMessage;
        listener.NetworkErrorEvent += (_, _) => Interlocked.Increment(ref _stats.SocketErrors);
        _net = new NetManager(listener)
        {
            UnconnectedMessagesEnabled = true,
            NatPunchEnabled = true,
            IPv6Enabled = false,
            AutoRecycle = true,
            UnsyncedEvents = true
        };
        _net.NatPunchModule.UnsyncedEvents = true;
        _net.NatPunchModule.Init(this);

        var probeListener = new EventBasedNetListener();
        probeListener.PeerConnectedEvent += peer =>
        {
            // Only happens if the key is ever accepted; still proves the port is reachable
            FinishPortCheck(peer, Protocol.PortCheckState.Reachable);
            peer.Disconnect();
        };
        probeListener.PeerDisconnectedEvent += OnProbeDisconnected;
        _probe = new NetManager(probeListener)
        {
            IPv6Enabled = false,
            AutoRecycle = true,
            DisconnectTimeout = 5000,
            // LiteNetLib's default is 10 attempts 500 ms apart; 3 is plenty to tell open from closed and keeps a
            // spoofed request from turning into a burst of packets at someone else
            MaxConnectAttempts = 3,
            ReconnectDelay = 500
        };
    }

    /// <summary>For /health: the loop is running and the socket still receives its own test packets.</summary>
    public bool Healthy
    {
        get
        {
            long now = Stopwatch.GetTimestamp();
            double sinceLoop = (now - Interlocked.Read(ref _lastLoop)) / (double)Stopwatch.Frequency;
            double sinceStart = (now - _started) / (double)Stopwatch.Frequency;
            long selfTest = Interlocked.Read(ref _lastSelfTest);
            double sinceSelfTest = selfTest == 0 ? sinceStart : (now - selfTest) / (double)Stopwatch.Frequency;
            return sinceLoop < 30 && sinceSelfTest < 45;
        }
    }

    public void Run(CancellationToken cancel)
    {
        if (!_net.Start(IPAddress.Any, IPAddress.IPv6Any, _config.UdpPort))
        {
            throw new InvalidOperationException($"Could not bind UDP port {_config.UdpPort}");
        }

        if (!_probe.Start())
        {
            throw new InvalidOperationException("Could not bind the port check socket");
        }

        Log.Info($"UDP listening on {_config.UdpPort}");

        var self = new IPEndPoint(IPAddress.Loopback, _config.UdpPort);
        DateTime now = DateTime.UtcNow;
        DateTime nextSweep = now + SweepInterval, nextSelfTest = now, nextStats = now + StatsInterval;
        DateTime nextPending = now;
        while (!cancel.IsCancellationRequested)
        {
            now = DateTime.UtcNow;
            lock (_gate)
            {
                _probe.PollEvents();

                if (now >= nextPending)
                {
                    nextPending = now + TimeSpan.FromMilliseconds(250);
                    ProcessPendingPortChecks(now);
                }

                if (now >= nextSweep)
                {
                    nextSweep = now + SweepInterval;
                    int expired = _registry.RemoveExpired();
                    if (expired > 0 && _eventLogBudget.TryTake())
                    {
                        Log.Info($"{expired} session(s) expired, {_registry.Count} live");
                    }
                }

                if (now >= nextStats)
                {
                    nextStats = now + StatsInterval;
                    if (_stats.Drain() is { } summary)
                    {
                        Log.Info(
                            $"UDP last minute: {summary}; {_registry.Count} live ({_registry.ConfirmedCount} confirmed)");
                    }
                }
            }

            if (now >= nextSelfTest)
            {
                nextSelfTest = now + SelfTestInterval;
                _probe.SendUnconnectedMessage(SelfTestPayload, self);
            }

            Interlocked.Exchange(ref _lastLoop, Stopwatch.GetTimestamp());
            Thread.Sleep(10);
        }

        _probe.Stop();
        _net.Stop();
    }

    private void OnUnconnectedMessage(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type)
    {
        if (IPAddress.IsLoopback(remote.Address) && reader.AvailableBytes == SelfTestPayload.Length &&
            reader.RawData.AsSpan(reader.Position, reader.AvailableBytes).SequenceEqual(SelfTestPayload))
        {
            Interlocked.Exchange(ref _lastSelfTest, Stopwatch.GetTimestamp());
            return;
        }

        lock (_gate)
        {
            _stats.Packets++;
            // The global budget comes first: past it, nothing is decoded and no per-address state is touched
            if (!_packetBudget.TryTake())
            {
                _stats.OverBudget++;
                return;
            }

            if (type != UnconnectedMessageType.BasicMessage || !_messageLimit.Allow(Net.AddressKey(remote.Address)))
            {
                _stats.RateLimited++;
                return;
            }

            Protocol.Message? message;
            try
            {
                message = Protocol.Decode(reader.GetRemainingBytes());
            }
            catch (Exception)
            {
                _stats.Undecodable++;
                return;
            }

            switch (message)
            {
                case Protocol.SessionInfoMessage info:
                    Registered(remote, info.Token,
                        _registry.Register(remote, info.Token, info.Secret, info.LocalIp, info.LocalPort, info));
                    break;
                case Protocol.ServerRegistrationMessage registration:
                    Registered(remote, registration.Token,
                        _registry.Register(remote, registration.Token, registration.Secret, registration.LocalIp,
                            registration.LocalPort, null));
                    break;
                case Protocol.SessionDetailsMessage details:
                    // Ignored unless the session is live and from the host's address; the next keep-alive retries
                    if (!_registry.UpdateDetails(remote, details))
                    {
                        _stats.DetailsRefused++;
                    }

                    break;
                case Protocol.ServerUnregisterMessage unregister:
                    if (_registry.Unregister(remote, unregister.Token, unregister.Secret))
                    {
                        _stats.Closed++;
                        if (_eventLogBudget.TryTake())
                        {
                            Log.Info($"Session {unregister.Token} closed by host, {_registry.Count} live");
                        }
                    }
                    else
                    {
                        _stats.CloseRefused++;
                    }

                    break;
                case Protocol.PortCheckRequestMessage check:
                    RequestPortCheck(remote, check.Port);
                    break;
                case null:
                    _stats.UnknownType++;
                    break;
            }
        }
    }

    private void Registered(IPEndPoint remote, string? token, RegisterResult result)
    {
        switch (result)
        {
            case RegisterResult.Added:
                _stats.Added++;
                if (_eventLogBudget.TryTake())
                {
                    Log.Info($"Session {token} registered by {Mask(remote)}, {_registry.Count} live");
                }

                break;
            case RegisterResult.Refreshed:
                _stats.Refreshed++;
                break;
            case RegisterResult.Invalid:
                _stats.Invalid++;
                break;
            case RegisterResult.TokenTaken:
                _stats.TokenTaken++;
                break;
            case RegisterResult.Full:
                _stats.Full++;
                break;
            case RegisterResult.PortChangeHeld:
                _stats.Ignored++;
                break;
        }
    }

    public void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        lock (_gate)
        {
            _stats.Packets++;
            if (!_packetBudget.TryTake())
            {
                _stats.OverBudget++;
                return;
            }

            ulong addressKey = Net.AddressKey(remoteEndPoint.Address);
            if (token == null || token.Length > MaxIntroduceTokenLength || !_introduceLimit.Allow(addressKey))
            {
                _stats.RateLimited++;
                return;
            }

            // Each source gets a few distinct sessions an hour (asking again for the same one is free), so the
            // public token list can't be turned into a list of every host's address
            ulong requestKey = Net.Hash(token);
            ulong subnetKey = Net.SubnetKey(remoteEndPoint.Address);
            if (!_introPerIp.Check(addressKey, requestKey) || !_introPerSubnet.Check(subnetKey, requestKey) ||
                !_introBudget.TryTake())
            {
                _stats.IntroLimited++;
                return;
            }

            _introPerIp.Record(addressKey, requestKey);
            _introPerSubnet.Record(subnetKey, requestKey);

            IntroduceResult result = IntroduceResult.Unknown;
            HostEndpoints? host = null;
            if (token.StartsWith("token:", StringComparison.Ordinal))
            {
                string sessionToken = token.Substring(6);
                if (SessionRegistry.IsValidToken(sessionToken))
                {
                    result = _registry.Introduce(sessionToken, null, out host);
                }
            }
            else if (token.StartsWith("ip:", StringComparison.Ordinal) &&
                     IPEndPoint.TryParse(token.Substring(3), out IPEndPoint? target) && target.Port > 0)
            {
                // Exactly the host's public address and port, nothing looser: otherwise this is a way to ask
                // whether some address is hosting
                result = _registry.Introduce(null, new IPEndPoint(target.Address.MapToIPv4(), target.Port),
                    out host);
            }

            if (result == IntroduceResult.SessionLimited)
            {
                _stats.IntroSessionLimited++;
                return;
            }

            if (host == null)
            {
                _stats.IntroUnknown++;
                return;
            }

            // LAN endpoints only matter when both are behind the same router, which then shows as the same public
            // address. Anyone else gets the public endpoints only, both ways: a stranger learns nothing about the
            // host's network, and can't make the host send punches to an address of the stranger's choosing.
            bool sameRouter = host.External.Address.Equals(remoteEndPoint.Address);
            IPEndPoint hostInternal = sameRouter && host.Internal != null ? host.Internal : host.External;
            IPEndPoint clientInternal = sameRouter && IsLanEndPoint(localEndPoint) ? localEndPoint : remoteEndPoint;
            _net.NatPunchModule.NatIntroduce(hostInternal, host.External, clientInternal, remoteEndPoint, host.Token);
            _stats.Introduced++;
        }
    }

    public void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        // Only the two players punch; this server never does
    }

    /// <summary>
    ///     A host asks whether its game port is reachable. The probe only goes out once the requester holds a
    ///     session at exactly that address and port that has sent
    ///     <see cref="SessionRegistry.ConfirmAfterRefreshes" /> more keep-alives (the mod asks right after its first
    ///     one, so the answer comes about 20 s later), at most <see cref="SessionRegistry.MaxPortChecksPerSession" />
    ///     per session, within a global rate and in-flight cap. A request that doesn't qualify gets no reply at all,
    ///     not even an error, so a spoofed one makes us send nothing.
    /// </summary>
    private void RequestPortCheck(IPEndPoint requester, int port)
    {
        if (port is <= 0 or > 65535 ||
            (!_config.AllowPrivatePeers && !Net.IsPublic(requester.Address)) ||
            !_portCheckLimit.Allow(Net.AddressKey(requester.Address)) ||
            (_pendingPortChecks.Count >= MaxPendingPortChecks && !_pendingPortChecks.ContainsKey(requester)))
        {
            _stats.PortCheckRefused++;
            return;
        }

        _pendingPortChecks[requester] = (port, DateTime.UtcNow + PendingPortCheckTtl);
        ProcessPendingPortChecks(DateTime.UtcNow);
    }

    private void ProcessPendingPortChecks(DateTime now)
    {
        if (_pendingPortChecks.Count == 0)
        {
            return;
        }

        foreach ((IPEndPoint requester, (int port, DateTime expires)) in _pendingPortChecks.ToList())
        {
            PortCheckStatus status = _registry.GetPortCheckStatus(requester);
            if (status == PortCheckStatus.Exhausted || now > expires)
            {
                _pendingPortChecks.Remove(requester);
                _stats.PortCheckExpired++;
                continue;
            }

            if (status != PortCheckStatus.Ready || _portChecks.Count >= _config.MaxPortChecksInFlight ||
                !_portCheckBudget.TryTake() || !_registry.TakePortCheck(requester))
            {
                continue;
            }

            _pendingPortChecks.Remove(requester);
            _stats.PortChecks++;
            var target = new IPEndPoint(requester.Address, port);
            if (_portChecks.TryGetValue(target, out List<IPEndPoint>? waiting))
            {
                if (!waiting.Contains(requester))
                {
                    waiting.Add(requester);
                }

                continue;
            }

            _portChecks[target] = new List<IPEndPoint> { requester };
            _probe.Connect(target, PortCheckKey);
        }
    }

    private void OnProbeDisconnected(NetPeer peer, DisconnectInfo info)
    {
        Protocol.PortCheckState state = info.Reason switch
        {
            DisconnectReason.ConnectionRejected or DisconnectReason.RemoteConnectionClose
                or DisconnectReason.InvalidProtocol or DisconnectReason.DisconnectPeerCalled
                => Protocol.PortCheckState.Reachable,
            DisconnectReason.ConnectionFailed or DisconnectReason.Timeout or DisconnectReason.HostUnreachable
                or DisconnectReason.NetworkUnreachable
                => Protocol.PortCheckState.Unreachable,
            _ => Protocol.PortCheckState.Error
        };
        FinishPortCheck(peer, state, state == Protocol.PortCheckState.Error ? info.Reason.ToString() : null);
    }

    // Called from _probe.PollEvents, which Run calls under _gate
    private void FinishPortCheck(NetPeer peer, Protocol.PortCheckState state, string? message = null)
    {
        var target = new IPEndPoint(peer.Address, peer.Port);
        if (!_portChecks.Remove(target, out List<IPEndPoint>? requesters))
        {
            return;
        }

        foreach (IPEndPoint requester in requesters)
        {
            _net.SendUnconnectedMessage(Protocol.EncodePortCheckResult(state, message), requester);
        }

        if (_eventLogBudget.TryTake())
        {
            Log.Info($"Port check {Mask(target)}: {state}");
        }
    }

    private sealed class CountingLogger : INetLogger
    {
        private readonly UdpStats _stats;

        public CountingLogger(UdpStats stats)
        {
            _stats = stats;
        }

        public void WriteNet(NetLogLevel level, string str, params object[] args)
        {
            Interlocked.Increment(ref _stats.LibraryErrors);
        }
    }

    private static bool IsLanEndPoint(IPEndPoint? endPoint)
    {
        return endPoint != null && endPoint.Port > 0 && Net.IsLan(endPoint.Address);
    }

    /// <summary>Players' addresses stay out of the logs unless LOBBY_LOG_IPS is set.</summary>
    private string Mask(IPEndPoint? endPoint)
    {
        if (endPoint == null)
        {
            return "?";
        }

        if (_config.LogIps)
        {
            return endPoint.ToString();
        }

        byte[] bytes = endPoint.Address.MapToIPv4().GetAddressBytes();
        return $"{bytes[0]}.{bytes[1]}.x.x";
    }
}
