using System.Runtime.InteropServices;
using CS2M.Lobby;

// `CS2M.Lobby --healthcheck` is the Docker HEALTHCHECK: the runtime image has no curl
if (args.Contains("--healthcheck"))
{
    try
    {
        int port = LobbyConfig.FromEnvironment().HttpPort;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}/health");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}

LobbyConfig config;
try
{
    config = LobbyConfig.FromEnvironment();
}
catch (ArgumentException e)
{
    Log.Error(e.Message);
    return 2;
}

var cancel = new CancellationTokenSource();
int exitCode = 0;
using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    cancel.Cancel();
});
using PosixSignalRegistration sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
{
    context.Cancel = true;
    cancel.Cancel();
});

var registry = new SessionRegistry(config);
var udp = new UdpService(config, registry);
var http = new HttpApi(config, registry, () => udp.Healthy);

Log.Info($"CS2M lobby starting (UDP {config.UdpPort}, HTTP {config.HttpPort}, session TTL {config.SessionTtl.TotalSeconds:0}s)");

var udpThread = new Thread(() =>
{
    try
    {
        udp.Run(cancel.Token);
    }
    catch (Exception e)
    {
        Log.Error($"UDP service stopped: {e.Message}");
        exitCode = 1;
        cancel.Cancel();
    }
}) { Name = "udp", IsBackground = true };
udpThread.Start();

try
{
    await http.Run(cancel.Token);
}
catch (Exception e)
{
    Log.Error($"HTTP service stopped: {e.Message}");
    exitCode = 1;
    cancel.Cancel();
}

udpThread.Join(TimeSpan.FromSeconds(5));
Log.Info("CS2M lobby stopped");
return exitCode;
