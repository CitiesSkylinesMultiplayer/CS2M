// Usage: dotnet run -- <lobby host> [udp port] [http port or base URL] [--remote]
// Exits 0 when every check passes.
//
// --remote: only the basics against a deployed lobby (register with a secret, listed over HTTP, NAT
// introduction and connect, close), e.g. dotnet run -- <lobby host> 4242 https://<lobby HTTP host> --remote
//
// The full run expects a freshly started lobby (it uses up this source's hourly quota of distinct introductions).
// Run it inside the lobby container's network namespace (docker run --network container:<lobby>), against
// 127.0.0.1, with LOBBY_ALLOW_PRIVATE_PEERS=1 on the lobby: hosts then register from 127.0.0.1, and a second
// source is the container's own address, which the lobby sees as a different public address
// ("another router"). Everything else uses the lobby's default limits.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CS2M.Commands.ApiServer;
using LiteNetLib;
using MessagePack;
using MessagePack.Attributeless;
using MessagePack.Resolvers;
using Legacy = CS2M.Commands.ApiServer.Legacy;

bool remote = args.Contains("--remote");
string[] positional = args.Where(a => !a.StartsWith("--")).ToArray();
string lobbyHost = positional.Length > 0 ? positional[0] : "127.0.0.1";
int udpPort = positional.Length > 1 ? int.Parse(positional[1]) : 4242;
string httpBase = positional.Length > 2 ? positional[2] : "8080";
IPAddress lobbyAddress = Dns.GetHostAddresses(lobbyHost).First(a => a.AddressFamily == AddressFamily.InterNetwork);
var lobby = new IPEndPoint(lobbyAddress, udpPort);
var http = new HttpClient
{
    BaseAddress = new Uri(httpBase.StartsWith("http") ? httpBase.TrimEnd('/') + "/" : $"http://{lobbyHost}:{httpBase}/")
};
int failures = 0;

// The mod's model: ApiCommand.RefreshModel, ProtocolOrder. The legacy one is older mods, without TokenSecret.
MessagePackSerializerOptions Model(params Type[] types)
{
    var builder = MessagePackSerializerOptions.Standard.WithResolver(StandardResolver.Instance).Configure();
    foreach (Type type in types)
    {
        builder.SubType(typeof(ApiCommandBase), type);
    }

    return builder.Build();
}

MessagePackSerializerOptions model = Model(typeof(PortCheckRequestCommand), typeof(PortCheckResultCommand),
    typeof(ServerRegistrationCommand), typeof(SessionInfoCommand), typeof(ServerUnregisterCommand),
    typeof(SessionDetailsCommand));
MessagePackSerializerOptions legacyModel = Model(typeof(PortCheckRequestCommand), typeof(PortCheckResultCommand),
    typeof(Legacy.ServerRegistrationCommand), typeof(Legacy.SessionInfoCommand),
    typeof(Legacy.ServerUnregisterCommand), typeof(SessionDetailsCommand));
byte[] Serialize(ApiCommandBase command) => MessagePackSerializer.Serialize(command, model);
byte[] SerializeLegacy(ApiCommandBase command) => MessagePackSerializer.Serialize(command, legacyModel);

void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}");
    if (!ok) failures++;
}

void Pump(int ms, params NetManager[] managers)
{
    for (int i = 0; i < ms / 10; i++)
    {
        foreach (NetManager m in managers)
        {
            m.PollEvents();
            m.NatPunchModule.PollEvents();
        }

        Thread.Sleep(10);
    }
}

// The lobby allows 40 messages per 10 s per source address, and all hosts here share 127.0.0.1: stay under it
var sent = new Queue<long>();

void Send(NetManager from, byte[] message)
{
    while (sent.Count >= 30 && Stopwatch.GetElapsedTime(sent.Peek()) < TimeSpan.FromSeconds(10.5))
    {
        Thread.Sleep(100);
    }

    while (sent.Count >= 30)
    {
        sent.Dequeue();
    }

    sent.Enqueue(Stopwatch.GetTimestamp());
    from.SendUnconnectedMessage(message, lobby);
}

async Task<JsonElement> Sessions() =>
    JsonDocument.Parse(await http.GetStringAsync("sessions")).RootElement;

// /sessions is cached for 2 s
async Task<JsonElement> FreshSessions()
{
    await Task.Delay(2100);
    return await Sessions();
}

JsonElement Find(JsonElement list, string token) =>
    list.EnumerateArray().FirstOrDefault(s => s.GetProperty("token").GetString() == token);

string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

if (remote)
{
    return await RemoteChecks();
}

// Host: like NetworkManager.StartServer
var hostListener = new EventBasedNetListener();
PortCheckResultCommand portResult = null;
hostListener.ConnectionRequestEvent += r => r.AcceptIfKey("CSM");
hostListener.NetworkReceiveUnconnectedEvent += (ep, reader, _) =>
    portResult = MessagePackSerializer.Deserialize<ApiCommandBase>(reader.GetRemainingBytes(), model) as PortCheckResultCommand;
bool hostSawPeer = false;
hostListener.PeerConnectedEvent += _ => hostSawPeer = true;
var host = new NetManager(hostListener) { NatPunchEnabled = true, UnconnectedMessagesEnabled = true };
host.Start(0);
host.NatPunchModule.Init(new EventBasedNatPunchListener());
// A LAN address nothing answers on, to see who gets told about it
const string fakeLan = "10.99.99.99";
string ownIp = NetUtils.GetLocalIp(LocalAddrType.IPv4);
Console.WriteLine($"  host port {host.LocalPort}, second source {ownIp}");

// An attacker on the same address as the host, another port, no secret or the wrong one
var attacker = new NetManager(new EventBasedNetListener()) { UnconnectedMessagesEnabled = true };
attacker.Start(0);

Check((await http.GetAsync("health")).IsSuccessStatusCode, "GET /health");

// Fresh tokens per run: a closed token stays reserved for its owner (and its secret) for hours
string run = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
string token = "SMOKE" + run;
string secret = NewSecret();
SessionInfoCommand Info(string city, string withSecret) => new()
{
    Token = token, LocalIp = fakeLan, LocalPort = host.LocalPort, CityName = city, HostName = "tester",
    PlayerCount = 1, MaxPlayers = 0, HasPassword = true, GameVersion = "1.2.3f1", ModVersion = "0.3.0.0",
    TokenSecret = withSecret
};

// Like ApiServer.Start: the first keep-alive, then the port check request straight away
Send(host, Serialize(Info("Smoke\u202Eville\u200B", secret)));
Pump(200, host);
Send(host, Serialize(new PortCheckRequestCommand { Port = host.LocalPort }));
string raw = await http.GetStringAsync("sessions");
JsonElement listed = Find(await Sessions(), token);
Check(listed.ValueKind == JsonValueKind.Object && listed.GetProperty("name").GetString() == "Smokeville" &&
      listed.GetProperty("host").GetString() == "tester" && listed.GetProperty("hasPassword").GetBoolean() &&
      listed.GetProperty("players").GetInt32() == 1, "SessionInfoCommand with a secret listed, bidi/zero-width dropped");
Check(!raw.Contains(fakeLan) && !raw.Contains("127.0.0.1") && !raw.Contains("\"ip\"", StringComparison.OrdinalIgnoreCase),
    "no addresses in /sessions");
Console.WriteLine("  /sessions: " + raw);
Check(listed.ValueKind == JsonValueKind.Object && listed.GetProperty("details").ValueKind == JsonValueKind.Null,
    "no details before SessionDetailsCommand");

// The port check waits until the session has sent two more keep-alives
Pump(1500, host);
Check(portResult == null, "no port check answer before the session is confirmed");
Send(host, Serialize(Info("Smokeville", secret)));
Pump(100, host);
Send(host, Serialize(Info("Smokeville", secret)));
for (int i = 0; i < 100 && portResult == null; i++) Pump(100, host);
Check(portResult?.State == PortCheckResult.Reachable, $"port check open port after 2 keep-alives -> {portResult?.State}");

// Someone else on the same address can't change, close or fill in the session
Send(attacker, Serialize(Info("Hijacked", null)));
Send(attacker, Serialize(Info("Hijacked2", NewSecret())));
Send(attacker, Serialize(new ServerUnregisterCommand { Token = token }));
Send(attacker, Serialize(new ServerUnregisterCommand { Token = token, TokenSecret = NewSecret() }));
Send(attacker, Serialize(new SessionDetailsCommand { Token = token, Population = 666 }));
Pump(300, attacker);
listed = Find(await FreshSessions(), token);
Check(listed.ValueKind == JsonValueKind.Object && listed.GetProperty("name").GetString() == "Smokeville" &&
      listed.GetProperty("details").ValueKind == JsonValueKind.Null,
    "without the secret: no takeover, no close, no details from another port");

// SessionDetailsCommand: stored and listed; only from the host's socket, so a wrong token is ignored
var details = new SessionDetailsCommand
{
    Token = token, Population = 12480, Milestone = 7, Year = 2026, Month = 5,
    PlayerNames = new[] { "tester", "Play\u202Eer" }, PlayerColors = new[] { "#D6FF3F", "not a colour" },
    ModIds = new[] { "12345", "local:SomeMod", "67890" }, ModNames = new[] { "Some Asset Pack", "", "I18N Everywhere" },
    ModVersions = new[] { "1.0", "0.2.0.0", "2.1" }, ModClientSide = new[] { false, false, true }, ModCount = 4
};
byte[] detailsBytes = Serialize(details);
Console.WriteLine($"  SessionDetailsCommand: {detailsBytes.Length} bytes");
Send(host, detailsBytes);
Send(host, Serialize(new SessionDetailsCommand { Token = "NOSUCHSESSION", Population = 1 }));
Pump(300, host);
await Task.Delay(2100);
raw = await http.GetStringAsync("sessions");
listed = Find(JsonDocument.Parse(raw).RootElement, token);
JsonElement d = listed.ValueKind == JsonValueKind.Object ? listed.GetProperty("details") : default;
Check(d.ValueKind == JsonValueKind.Object && d.GetProperty("population").GetInt32() == 12480 &&
      d.GetProperty("milestone").GetInt32() == 7 && d.GetProperty("year").GetInt32() == 2026 &&
      d.GetProperty("month").GetInt32() == 5 && d.GetProperty("modCount").GetInt32() == 4,
    "SessionDetailsCommand listed with population, milestone, date");
Check(d.ValueKind == JsonValueKind.Object && d.GetProperty("playerList").GetArrayLength() == 2 &&
      d.GetProperty("playerList")[0].GetProperty("color").GetString() == "#D6FF3F" &&
      d.GetProperty("playerList")[1].GetProperty("name").GetString() == "Player" &&
      d.GetProperty("playerList")[1].GetProperty("color").ValueKind == JsonValueKind.Null,
    "player list with colours, bad colour and bidi override dropped");
Check(d.ValueKind == JsonValueKind.Object && d.GetProperty("mods").GetArrayLength() == 3 &&
      d.GetProperty("mods")[1].GetProperty("id").GetString() == "local:SomeMod" &&
      d.GetProperty("mods")[1].GetProperty("name").ValueKind == JsonValueKind.Null &&
      d.GetProperty("mods")[2].GetProperty("clientSide").GetBoolean(),
    "mod list with ids, versions and client side flags");
Check(!raw.Contains("NOSUCHSESSION"), "details for an unknown token ignored");
Check(!raw.Contains(fakeLan) && !raw.Contains("127.0.0.1"), "still no addresses in /sessions");

// A long mod list in two packets, each under the mod's 1000 byte cap (ApiServer.BuildDetails)
string[] allIds = Enumerable.Range(0, 30).Select(i => "local:" + new string('m', 40) + i).ToArray();
SessionDetailsCommand Page(int offset, int count, bool first) => new()
{
    Token = token, ModOffset = offset, ModCount = allIds.Length,
    PlayerNames = first ? Enumerable.Range(0, 16).Select(i => new string('p', 31) + (char)('a' + i)).ToArray() : null,
    PlayerColors = first ? Enumerable.Range(0, 16).Select(_ => "#40BFFF").ToArray() : null,
    ModIds = allIds.Skip(offset).Take(count).ToArray(),
    ModNames = allIds.Skip(offset).Take(count).Select(_ => new string('n', 40)).ToArray(),
    ModVersions = allIds.Skip(offset).Take(count).Select(_ => "1.2.3.4").ToArray(),
    ModClientSide = allIds.Skip(offset).Take(count).Select((_, i) => i % 2 == 0).ToArray()
};
int firstCount = allIds.Length;
while (Serialize(Page(0, firstCount, true)).Length > 1000) firstCount--;
int secondCount = allIds.Length - firstCount;
while (Serialize(Page(firstCount, secondCount, false)).Length > 1000) secondCount--;
// out of order first: a continuation that doesn't follow what's stored is dropped
Send(host, Serialize(Page(firstCount + 1, 1, false)));
Send(host, Serialize(Page(0, firstCount, true)));
Pump(150, host);
Send(host, Serialize(Page(firstCount, secondCount, false)));
Pump(300, host);
d = Find(await FreshSessions(), token).GetProperty("details");
Check(d.GetProperty("playerList").GetArrayLength() == 16 &&
      d.GetProperty("mods").GetArrayLength() == firstCount + secondCount &&
      d.GetProperty("mods")[firstCount].GetProperty("id").GetString() == allIds[firstCount],
    $"mod list in two packets ({firstCount} + {secondCount} mods) joined in order");

// The second port check of this session: a closed port. Then invalid ports get no answer at all.
portResult = null;
Send(host, Serialize(new PortCheckRequestCommand { Port = 1 }));
for (int i = 0; i < 100 && portResult == null; i++) Pump(100, host);
Check(portResult?.State == PortCheckResult.Unreachable, $"port check closed port -> {portResult?.State}");
portResult = null;
Send(attacker, Serialize(new PortCheckRequestCommand { Port = 0 }));
Send(host, Serialize(new PortCheckRequestCommand { Port = 70000 }));
Pump(1500, host);
Check(portResult == null, "no reply to an invalid port");

// Joiner on the host's address (same router): like NetworkManager.SetupNatConnect + Connect
var joinListener = new EventBasedNetListener();
bool joined = false;
joinListener.PeerConnectedEvent += _ => joined = true;
var joiner = new NetManager(joinListener) { NatPunchEnabled = true, UnconnectedMessagesEnabled = true };
joiner.Start();
var natListener = new EventBasedNatPunchListener();
IPEndPoint punched = null;
natListener.NatIntroductionSuccess += (point, type, t) => punched ??= point;
joiner.NatPunchModule.Init(natListener);
joiner.NatPunchModule.SendNatIntroduceRequest(lobby, "token:" + token);
for (int i = 0; i < 50 && punched == null; i++) Pump(100, host, joiner);
Check(punched != null, "NAT introduction by token");
if (punched != null)
{
    joiner.Connect(punched, "CSM");
    for (int i = 0; i < 50 && !joined; i++) Pump(100, host, joiner);
    Check(joined && hostSawPeer, "joiner connected to the host");
}

// ip: form, with exactly the host's public address and port
punched = null;
var joiner2 = new NetManager(new EventBasedNetListener()) { NatPunchEnabled = true };
joiner2.Start();
var natListener2 = new EventBasedNatPunchListener();
natListener2.NatIntroductionSuccess += (point, type, t) => punched ??= point;
joiner2.NatPunchModule.Init(natListener2);
// what the lobby sees as the host's address is our own outgoing address towards it
IPAddress self;
using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
{
    probe.Connect(lobby);
    self = ((IPEndPoint)probe.LocalEndPoint!).Address;
}

joiner2.NatPunchModule.SendNatIntroduceRequest(lobby, "ip:" + new IPEndPoint(self, host.LocalPort));
for (int i = 0; i < 50 && punched == null; i++) Pump(100, host, joiner2);
Check(punched != null, "NAT introduction by ip:address:port");

// No wildcard port, no other port, no unknown token
foreach (string request in new[] { $"ip:{self}:0", $"ip:{self}:{(host.LocalPort == 65535 ? 1 : host.LocalPort + 1)}", "token:NOSUCHTOKEN" })
{
    punched = null;
    joiner2.NatPunchModule.SendNatIntroduceRequest(lobby, request);
    Pump(1000, host, joiner2);
    Check(punched == null, $"no introduction for {request.Replace(self.ToString(), "<host>")}");
}

// What exactly the introduction tells a joiner: capture a real NatIntroduceRequest from LiteNetLib, then replay
// it from a plain socket and read the answer. LiteNetLib writes endpoints as text, so a search is enough.
byte[] CaptureRequest(string request)
{
    using var sink = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    var capturer = new NetManager(new EventBasedNetListener()) { NatPunchEnabled = true };
    capturer.Start();
    capturer.NatPunchModule.Init(new EventBasedNatPunchListener());
    capturer.NatPunchModule.SendNatIntroduceRequest((IPEndPoint)sink.Client.LocalEndPoint!, request);
    sink.Client.ReceiveTimeout = 2000;
    IPEndPoint from = null;
    byte[] bytes = sink.Receive(ref from);
    capturer.Stop();
    return bytes;
}

// Everything that arrives within 1.5 s (the host's punches can follow the lobby's answer), or null
string Ask(UdpClient client, IPEndPoint to, byte[] request)
{
    client.Send(request, request.Length, to);
    var received = new StringBuilder();
    var until = DateTime.UtcNow.AddMilliseconds(1500);
    while (DateTime.UtcNow < until)
    {
        client.Client.ReceiveTimeout = Math.Max(1, (int)(until - DateTime.UtcNow).TotalMilliseconds);
        try
        {
            IPEndPoint from = null;
            received.Append(Encoding.Latin1.GetString(client.Receive(ref from))).Append('|');
        }
        catch (SocketException)
        {
            break;
        }
    }

    return received.Length == 0 ? null : received.ToString();
}

byte[] tokenRequest = CaptureRequest("token:" + token);
using (var sameRouter = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
{
    string answer = Ask(sameRouter, lobby, tokenRequest);
    Check(answer != null && answer.Contains(fakeLan), "same public address: the introduction includes the host's LAN address");
}

using var otherRouter = new UdpClient(new IPEndPoint(IPAddress.Parse(ownIp), 0));
var lobbyOther = new IPEndPoint(IPAddress.Parse(ownIp), udpPort);
string otherAnswer = Ask(otherRouter, lobbyOther, tokenRequest);
Check(otherAnswer != null && !otherAnswer.Contains(fakeLan) && otherAnswer.Contains(self.ToString()),
    "another public address: public endpoint only, no LAN address");

// Five distinct requests an hour per source: the first was the token above, four unknown tokens make five,
// asking for the same token again is still fine, a sixth distinct one is refused (it would have matched)
for (int i = 1; i <= 4; i++)
{
    Ask(otherRouter, lobbyOther, CaptureRequest($"token:NOPE{i:00000}"));
}

Check(Ask(otherRouter, lobbyOther, tokenRequest) != null, "the same session again after 5 distinct requests");
Check(Ask(otherRouter, lobbyOther, CaptureRequest($"ip:{self}:{host.LocalPort}")) == null,
    "a 6th distinct session in the hour is refused");

// An older mod (no secret): works, and another port on its address can't change or close it
var legacyHost = new NetManager(new EventBasedNetListener()) { UnconnectedMessagesEnabled = true };
legacyHost.Start(0);
Legacy.SessionInfoCommand LegacyInfo(string city) => new()
{
    Token = "LEGACY0002", LocalIp = fakeLan, LocalPort = legacyHost.LocalPort, CityName = city, HostName = "old",
    PlayerCount = 1, GameVersion = "1.2.3f1", ModVersion = "0.2.0.0"
};
Send(legacyHost, SerializeLegacy(LegacyInfo("Oldtown")));
Pump(200, legacyHost);
Send(attacker, SerializeLegacy(LegacyInfo("Hijacked")));
Send(attacker, SerializeLegacy(new Legacy.ServerUnregisterCommand { Token = "LEGACY0002" }));
Pump(200, attacker);
JsonElement legacySession = Find(await FreshSessions(), "LEGACY0002");
Check(legacySession.ValueKind == JsonValueKind.Object && legacySession.GetProperty("name").GetString() == "Oldtown",
    "SessionInfo without a secret: listed; another port can't rename or close it");
Send(legacyHost, SerializeLegacy(new Legacy.ServerUnregisterCommand { Token = "LEGACY0002" }));
Pump(200, legacyHost);
Check(Find(await FreshSessions(), "LEGACY0002").ValueKind != JsonValueKind.Object,
    "ServerUnregister without a secret from the host's own socket closes it");

// A host without a secret whose router moved it to a new port: followed once the new port keeps refreshing
// for 20 s with nothing from the old one
var remapped = new NetManager(new EventBasedNetListener()) { UnconnectedMessagesEnabled = true };
remapped.Start(0);
Legacy.SessionInfoCommand Moving(string city) => new()
{
    Token = "LEGACY0003", LocalIp = fakeLan, LocalPort = legacyHost.LocalPort, CityName = city, HostName = "old"
};
Send(legacyHost, SerializeLegacy(Moving("Before")));
Pump(200, legacyHost);
for (int i = 0; i < 3; i++)
{
    if (i > 0) Thread.Sleep(10_500);
    Send(remapped, SerializeLegacy(Moving("Moved")));
}

Pump(300, remapped);
JsonElement moved = Find(await FreshSessions(), "LEGACY0003");
Check(moved.ValueKind == JsonValueKind.Object && moved.GetProperty("name").GetString() == "Moved",
    "without a secret, a new port is followed after 3 keep-alives over 20 s");
Send(remapped, SerializeLegacy(new Legacy.ServerUnregisterCommand { Token = "LEGACY0003" }));
Pump(200, remapped);
remapped.Stop();

// The original registration command still works (listed without details)
Send(host, SerializeLegacy(new Legacy.ServerRegistrationCommand { Token = "LEGACY0001", LocalIp = fakeLan, LocalPort = host.LocalPort }));
Pump(300, host);
JsonElement legacy = Find(await FreshSessions(), "LEGACY0001");
Check(legacy.ValueKind == JsonValueKind.Object && legacy.GetProperty("name").ValueKind == JsonValueKind.Null,
    "ServerRegistrationCommand accepted");
Send(host, SerializeLegacy(new Legacy.ServerUnregisterCommand { Token = "LEGACY0001" }));

// Closing with the secret; afterwards the token stays with its owner
Send(host, Serialize(new ServerUnregisterCommand { Token = token, TokenSecret = secret }));
Pump(300, host);
Check(Find(await FreshSessions(), token).ValueKind != JsonValueKind.Object, "ServerUnregister with the secret closes it");
Send(attacker, Serialize(Info("Squatter", null)));
Send(attacker, Serialize(Info("Squatter", NewSecret())));
Pump(300, attacker);
Check(Find(await FreshSessions(), token).ValueKind != JsonValueKind.Object, "a closed token can't be taken by someone else");
Send(host, Serialize(Info("Smokeville", secret)));
Pump(300, host);
Check(Find(await FreshSessions(), token).ValueKind == JsonValueKind.Object, "the owner can register its token again");
Send(host, Serialize(new ServerUnregisterCommand { Token = token, TokenSecret = secret }));
Pump(300, host);

// Five sessions per address
for (int i = 0; i < 6; i++)
{
    Send(host, Serialize(new SessionInfoCommand { Token = $"CAP{run}{i}", TokenSecret = secret, CityName = "Cap" }));
}

Pump(300, host);
JsonElement all = await FreshSessions();
int capped = all.EnumerateArray().Count(s => s.GetProperty("token").GetString()!.StartsWith("CAP" + run));
Check(capped == 5, $"five sessions per address ({capped} listed)");
for (int i = 0; i < 6; i++)
{
    Send(host, Serialize(new ServerUnregisterCommand { Token = $"CAP{run}{i}", TokenSecret = secret }));
}

// A burst of junk from one source: the lobby keeps working for everyone else
using (var junk = new UdpClient(new IPEndPoint(IPAddress.Parse(ownIp), 0)))
{
    var garbage = new byte[] { 0x07, 0xFF, 0xFF, 0xFF };
    for (int i = 0; i < 500; i++)
    {
        junk.Send(garbage, garbage.Length, lobbyOther);
    }
}

Pump(300, host);
Send(host, Serialize(Info("Afterflood", secret)));
Pump(300, host);
Check(Find(await FreshSessions(), token).ValueKind == JsonValueKind.Object && (await http.GetAsync("health")).IsSuccessStatusCode,
    "still registering and healthy after a junk burst");
Send(host, Serialize(new ServerUnregisterCommand { Token = token, TokenSecret = secret }));
Pump(300, host);
Check((await FreshSessions()).GetArrayLength() == 0, "every session closed again");

host.Stop();
attacker.Stop();
legacyHost.Stop();
joiner.Stop();
joiner2.Stop();
Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

async Task<int> RemoteChecks()
{
    Check((await http.GetAsync("health")).IsSuccessStatusCode, "GET /health");
    string remoteToken = "SMOKE" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
    string remoteSecret = NewSecret();
    var hostListener = new EventBasedNetListener();
    bool sawPeer = false;
    hostListener.ConnectionRequestEvent += r => r.AcceptIfKey("CSM");
    hostListener.PeerConnectedEvent += _ => sawPeer = true;
    var remoteHost = new NetManager(hostListener) { NatPunchEnabled = true, UnconnectedMessagesEnabled = true };
    remoteHost.Start(0);
    remoteHost.NatPunchModule.Init(new EventBasedNatPunchListener());
    remoteHost.SendUnconnectedMessage(Serialize(new SessionInfoCommand
    {
        Token = remoteToken, TokenSecret = remoteSecret, LocalIp = NetUtils.GetLocalIp(LocalAddrType.IPv4),
        LocalPort = remoteHost.LocalPort, CityName = "Smoke test", HostName = "smoke", PlayerCount = 1
    }), lobby);
    Pump(500, remoteHost);
    Check(Find(await FreshSessions(), remoteToken).ValueKind == JsonValueKind.Object, $"{remoteToken} registered and listed");

    var joinListener = new EventBasedNetListener();
    bool connected = false;
    joinListener.PeerConnectedEvent += _ => connected = true;
    var remoteJoiner = new NetManager(joinListener) { NatPunchEnabled = true };
    remoteJoiner.Start();
    var punchListener = new EventBasedNatPunchListener();
    IPEndPoint target = null;
    punchListener.NatIntroductionSuccess += (point, _, _) => target ??= point;
    remoteJoiner.NatPunchModule.Init(punchListener);
    remoteJoiner.NatPunchModule.SendNatIntroduceRequest(lobby, "token:" + remoteToken);
    for (int i = 0; i < 50 && target == null; i++) Pump(100, remoteHost, remoteJoiner);
    Check(target != null, "NAT introduction by token");
    if (target != null)
    {
        remoteJoiner.Connect(target, "CSM");
        for (int i = 0; i < 50 && !connected; i++) Pump(100, remoteHost, remoteJoiner);
        Check(connected && sawPeer, "joiner connected to the host");
    }

    remoteHost.SendUnconnectedMessage(Serialize(new ServerUnregisterCommand { Token = remoteToken, TokenSecret = remoteSecret }), lobby);
    Pump(500, remoteHost);
    Check(Find(await FreshSessions(), remoteToken).ValueKind != JsonValueKind.Object, "closed with the secret");
    remoteHost.Stop();
    remoteJoiner.Stop();
    Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
    return failures == 0 ? 0 : 1;
}
