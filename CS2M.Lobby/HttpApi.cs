using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2M.Lobby;

/// <summary>
///     Read-only JSON API for the in-game server browser: GET /sessions and GET /health.
///     Plain HTTP, meant to sit behind a reverse proxy that terminates TLS.
///     At most <see cref="LobbyConfig.HttpConcurrency" /> requests are handled at once (the rest get a 503), and
///     the /sessions body is built at most every <see cref="SessionsCacheTime" />, however many ask.
/// </summary>
public sealed class HttpApi
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static readonly TimeSpan SessionsCacheTime = TimeSpan.FromSeconds(2);
    private static readonly byte[] BusyBody = "{\"error\":\"busy\"}"u8.ToArray();

    private readonly LobbyConfig _config;
    private readonly SessionRegistry _registry;
    private readonly Func<bool> _udpHealthy;
    private readonly HttpListener _listener = new();
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly SemaphoreSlim _slots;
    private readonly object _cacheLock = new();
    private byte[]? _sessionsBody;
    private long _sessionsBuilt;

    public HttpApi(LobbyConfig config, SessionRegistry registry, Func<bool> udpHealthy)
    {
        _config = config;
        _registry = registry;
        _udpHealthy = udpHealthy;
        _slots = new SemaphoreSlim(config.HttpConcurrency, config.HttpConcurrency);
        _listener.Prefixes.Add($"http://{config.HttpBind}:{config.HttpPort}/");
    }

    public async Task Run(CancellationToken cancel)
    {
        _listener.Start();
        Log.Info($"HTTP listening on {_config.HttpPort}");
        await using CancellationTokenRegistration stopOnCancel = cancel.Register(() => _listener.Stop());

        while (!cancel.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException e)
            {
                Log.Warn($"HTTP listener: {e.Message}");
                continue;
            }

            if (!_slots.Wait(0))
            {
                Busy(context.Response);
                continue;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    Handle(context);
                }
                finally
                {
                    _slots.Release();
                }
            }, CancellationToken.None);
        }
    }

    private static void Busy(HttpListenerResponse response)
    {
        try
        {
            response.StatusCode = 503;
            response.ContentType = "application/json; charset=utf-8";
            response.Headers["Retry-After"] = "2";
            response.Headers["Cache-Control"] = "no-store";
            response.ContentLength64 = BusyBody.Length;
            response.OutputStream.Write(BusyBody);
            response.Close();
        }
        catch
        {
            response.Abort();
        }
    }

    private void Handle(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        try
        {
            HttpListenerRequest request = context.Request;
            string path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (request.HttpMethod != "GET" && request.HttpMethod != "HEAD")
            {
                Write(response, 405, Serialize(new { error = "method not allowed" }), "no-store");
            }
            else if (path == "/sessions")
            {
                Write(response, 200, SessionsBody(), "public, max-age=2");
            }
            else if (path == "/health")
            {
                bool udp = _udpHealthy();
                Write(response, udp ? 200 : 503, Serialize(new
                {
                    status = udp ? "ok" : "udp stalled",
                    sessions = _registry.Count,
                    uptimeSeconds = (long)(DateTime.UtcNow - _started).TotalSeconds
                }), "no-store");
            }
            else
            {
                Write(response, 404, Serialize(new { error = "not found" }), "no-store");
            }
        }
        catch (Exception e)
        {
            Log.Warn($"HTTP request failed: {e.GetType().Name}");
            try
            {
                response.Abort();
            }
            catch
            {
                // already gone
            }
        }
    }

    /// <summary>The serialized session list, rebuilt at most every <see cref="SessionsCacheTime" />.</summary>
    private byte[] SessionsBody()
    {
        lock (_cacheLock)
        {
            long now = Stopwatch.GetTimestamp();
            if (_sessionsBody == null ||
                now - _sessionsBuilt > SessionsCacheTime.TotalSeconds * Stopwatch.Frequency)
            {
                _sessionsBody = Serialize(_registry.Snapshot());
                _sessionsBuilt = now;
            }

            return _sessionsBody;
        }
    }

    private static byte[] Serialize(object body)
    {
        return JsonSerializer.SerializeToUtf8Bytes(body, Json);
    }

    private static void Write(HttpListenerResponse response, int status, byte[] bytes, string cacheControl)
    {
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = cacheControl;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.Close();
    }
}
