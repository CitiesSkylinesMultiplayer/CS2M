using CS2M.BaseGame.Systems;
using CS2M.Commands.ApiServer;
using CS2M.Mods;
using CS2M.Util;
using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Game.City;
using Game.Simulation;
using Unity.Entities;

namespace CS2M.Networking
{
    public class ApiServer
    {
        // Often enough to keep the host's router mapping to the API server open, so NAT introductions reach it
        private const int KeepAliveSeconds = 10;

        // Crockford base32 (no I, L, O or U), so a token can be read out and typed
        private const string TokenAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private const int TokenLength = 10;

        // SessionDetailsCommand has to fit one unconnected packet; LiteNetLib's smallest MTU is 1024
        private const int MaxDetailsBytes = 1000;
        private const int MaxListedPlayers = 16;
        private const int MaxListedMods = 64;
        private const int MaxDetailsPackets = 6;
        private const string HostColor = "#D6FF3F";
        private const string UnknownColor = "#9AA08F";

        private EntityQuery _milestoneQuery;
        private bool _hasMilestoneQuery;

        private readonly NetManager _netManager;
        private readonly Stopwatch _sinceKeepAlive = new Stopwatch();
        private ConnectionConfig _connectionConfig;
        private IPEndPoint _endpoint;
        private string _secret;

        // The name lookup and the adapter scan block, so they run in the background; commands sent before the
        // lookup is done wait in _queued. The local address is looked up again every few minutes.
        private const int LocalIpRefreshSeconds = 300;
        private const int MaxQueued = 32;
        private Task<IPEndPoint> _resolveTask;
        private Task<string> _localIpTask;
        private string _localIp;
        private readonly Stopwatch _sinceLocalIp = new Stopwatch();
        private readonly List<ApiCommandBase> _queued = new List<ApiCommandBase>();

        /// <summary>
        ///     The token joiners use to find this server, while it's registered.
        /// </summary>
        public string Token { get; private set; }

        public ApiServer(NetManager netManager)
        {
            _netManager = netManager;
        }

        /// <summary>
        ///     Start registering the hosted server: a keep-alive now and then every few seconds until <see cref="Stop" />.
        /// </summary>
        public void Start(ConnectionConfig connectionConfig)
        {
            _connectionConfig = connectionConfig;
            Token = GenerateToken();
            _secret = GenerateSecret();
            _endpoint = null;
            _queued.Clear();
            string apiServer = Mod.Instance.Settings.ApiServer;
            int apiPort = Mod.Instance.Settings.GetApiServerPort();
            _resolveTask = Task.Run(() => IPUtil.CreateIP4EndPoint(apiServer, apiPort));
            RefreshLocalIp();
            EnabledMods.RefreshPlayset(true);
            Log.Info($"Registering the server with the API server as {Token}");
            // The first keep-alive goes out from KeepAlive once the address lookups are done
            _sinceKeepAlive.Reset();
            SendCommand(new PortCheckRequestCommand { Port = connectionConfig.Port });
        }

        /// <summary>
        ///     Stop registering and remove the server from the list.
        /// </summary>
        public void Stop()
        {
            if (Token == null)
            {
                return;
            }

            SendCommand(new ServerUnregisterCommand { Token = Token, TokenSecret = _secret });
            Token = null;
            _secret = null;
            _connectionConfig = null;
            _sinceKeepAlive.Reset();
            _queued.Clear();
        }

        /// <summary>
        ///     Whether a message came from the API server, so nobody else can send us API commands.
        /// </summary>
        public bool IsApiServer(IPEndPoint endPoint)
        {
            return _endpoint != null && _endpoint.Equals(endPoint);
        }

        /// <summary>
        ///     Send a message to the API server
        /// </summary>
        /// <param name="message"></param>
        public void SendCommand(ApiCommandBase message)
        {
            if (!EndpointReady())
            {
                if (_resolveTask != null && _queued.Count < MaxQueued)
                {
                    _queued.Add(message);
                }

                return;
            }

            try
            {
                _netManager.SendUnconnectedMessage(ApiCommand.Instance.Serialize(message), _endpoint);
                Log.Debug(
                    $"Sending {message.GetType().Name} to API server at {Mod.Instance.Settings.ApiServer}:{Mod.Instance.Settings.ApiServerPort}");
            }
            catch (Exception e)
            {
                Log.Warn(
                    $"Could not send message to API server at {Mod.Instance.Settings.ApiServer}:{Mod.Instance.Settings.ApiServerPort}: {e}");
            }
        }

        /// <summary>
        ///     Whether the API server's address is known. Picks up the background lookup once it's done.
        /// </summary>
        private bool EndpointReady()
        {
            if (_endpoint != null)
            {
                return true;
            }

            if (_resolveTask == null || !_resolveTask.IsCompleted)
            {
                return false;
            }

            Task<IPEndPoint> task = _resolveTask;
            _resolveTask = null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null && task.Result.Address != null)
            {
                _endpoint = task.Result;
                return true;
            }

            Log.Warn($"Could not resolve the API server {Mod.Instance?.Settings?.ApiServer}: the server won't be listed");
            _queued.Clear();
            return false;
        }

        private void RefreshLocalIp()
        {
            if (_localIpTask != null)
            {
                return;
            }

            _sinceLocalIp.Restart();
            _localIpTask = Task.Run(() =>
            {
                string localIp = NetUtils.GetLocalIp(LocalAddrType.IPv4);
                if (string.IsNullOrEmpty(localIp))
                {
                    localIp = NetUtils.GetLocalIp(LocalAddrType.IPv6);
                }

                return localIp;
            });
        }

        public void KeepAlive()
        {
            if (_localIpTask != null && _localIpTask.IsCompleted)
            {
                if (_localIpTask.Status == TaskStatus.RanToCompletion)
                {
                    _localIp = _localIpTask.Result;
                }

                _localIpTask = null;
            }
            else if (_localIpTask == null && Token != null && _sinceLocalIp.Elapsed.TotalSeconds >= LocalIpRefreshSeconds)
            {
                RefreshLocalIp();
            }

            if (Token == null || !EndpointReady())
            {
                return;
            }

            if (_queued.Count > 0)
            {
                List<ApiCommandBase> queued = new List<ApiCommandBase>(_queued);
                _queued.Clear();
                foreach (ApiCommandBase message in queued)
                {
                    SendCommand(message);
                }
            }

            bool first = !_sinceKeepAlive.IsRunning;
            // The first one waits (briefly) for the local address, so LAN players can find the server
            if ((first && (_localIp != null || _localIpTask == null || _sinceLocalIp.Elapsed.TotalSeconds > 3)) ||
                (!first && _sinceKeepAlive.Elapsed.TotalSeconds >= KeepAliveSeconds))
            {
                SendKeepAlive();
            }
        }

        private void SendKeepAlive()
        {
            _sinceKeepAlive.Restart();

            string localIp = _localIp ?? "";

            CityConfigurationSystem city =
                World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<CityConfigurationSystem>();

            SendCommand(new SessionInfoCommand
            {
                Token = Token,
                TokenSecret = _secret,
                LocalIp = localIp,
                LocalPort = _connectionConfig.Port,
                CityName = city?.cityName ?? "",
                HostName = NetworkInterface.Instance.LocalPlayer.Username ?? "",
                PlayerCount = NetworkInterface.Instance.ConnectedPlayerCount,
                MaxPlayers = GetMaxPlayers(),
                HasPassword = !string.IsNullOrEmpty(_connectionConfig.Password),
                GameVersion = VersionUtil.GetGameVersion().ToString(),
                ModVersion = VersionUtil.GetModVersion().ToString(),
            });

            try
            {
                foreach (SessionDetailsCommand page in BuildDetails())
                {
                    SendCommand(page);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Could not send the session details: {e.Message}");
            }
        }

        /// <summary>
        ///     The host's player limit (host included), 0 for none.
        /// </summary>
        public static int GetMaxPlayers()
        {
            return Math.Max(0, Mod.Instance?.Settings?.HostMaxPlayers ?? 0);
        }

        /// <summary>
        ///     The session details, in as many packets as the mod list needs: the first has everything, the
        ///     others only continue the mod list (<see cref="SessionDetailsCommand.ModOffset" />).
        /// </summary>
        private List<SessionDetailsCommand> BuildDetails()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            var details = new SessionDetailsCommand { Token = Token };

            if (world != null)
            {
                EntityManager entityManager = world.EntityManager;
                CitySystem citySystem = world.GetExistingSystemManaged<CitySystem>();
                if (citySystem != null && citySystem.City != Entity.Null &&
                    entityManager.HasComponent<Population>(citySystem.City))
                {
                    details.Population = entityManager.GetComponentData<Population>(citySystem.City).m_Population;
                }

                if (!_hasMilestoneQuery)
                {
                    _milestoneQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MilestoneLevel>());
                    _hasMilestoneQuery = true;
                }

                if (!_milestoneQuery.IsEmptyIgnoreFilter)
                {
                    details.Milestone = _milestoneQuery.GetSingleton<MilestoneLevel>().m_AchievedMilestone;
                }

                // Same as the game's save info, which the save list shows with LocalizedDate
                TimeSystem timeSystem = world.GetExistingSystemManaged<TimeSystem>();
                if (timeSystem != null)
                {
                    DateTime date = timeSystem.GetCurrentDateTime();
                    details.Year = date.Year;
                    details.Month = date.DayOfYear - 1;
                }
            }

            // Players: the host, then everyone connected, with the cursor colour the others see
            var names = new List<string> { Cap(NetworkInterface.Instance.LocalPlayer.Username, TextRules.MaxUsernameLength) };
            var colors = new List<string> { HostColor };
            Dictionary<string, string> cursorColors = new Dictionary<string, string>();
            foreach (CursorSystem.RemoteCursor cursor in CursorSystem.Remote.Values)
            {
                if (cursor.Name != null && !cursorColors.ContainsKey(cursor.Name))
                {
                    cursorColors[cursor.Name] = Hex(cursor.Color.r, cursor.Color.g, cursor.Color.b);
                }
            }

            foreach (RemotePlayer player in NetworkInterface.Instance.ConnectedRemotePlayers)
            {
                if (names.Count >= MaxListedPlayers)
                {
                    break;
                }

                names.Add(Cap(player.Username, TextRules.MaxUsernameLength));
                colors.Add(cursorColors.TryGetValue(player.Username ?? "", out string color) ? color : UnknownColor);
            }

            details.PlayerNames = names.ToArray();
            details.PlayerColors = colors.ToArray();

            // Mods, required first, split over as many packets as needed
            EnabledMods.RefreshPlayset();
            List<EnabledMod> mods = EnabledMods.Get();
            List<EnabledMod> listed = mods.Take(MaxListedMods).ToList();
            details.ModCount = mods.Count;

            var pages = new List<SessionDetailsCommand>();
            SessionDetailsCommand current = details;
            int offset = 0;
            while (true)
            {
                int count = FillMods(current, listed, offset);
                pages.Add(current);
                offset += count;
                if (offset >= listed.Count || count == 0 || pages.Count >= MaxDetailsPackets)
                {
                    return pages;
                }

                current = new SessionDetailsCommand { Token = Token, ModOffset = offset, ModCount = mods.Count };
            }
        }

        /// <summary>
        ///     Puts as many mods from <paramref name="offset" /> on as fit the packet. Returns how many.
        /// </summary>
        private static int FillMods(SessionDetailsCommand page, List<EnabledMod> mods, int offset)
        {
            int count = mods.Count - offset;
            while (true)
            {
                List<EnabledMod> part = mods.Skip(offset).Take(count).ToList();
                page.ModIds = part.Select(mod => Cap(mod.Id, 48)).ToArray();
                page.ModNames = part.Select(mod => Cap(mod.Name == mod.Id ? "" : mod.Name, 40)).ToArray();
                page.ModVersions = part.Select(mod => Cap(mod.Version, 20)).ToArray();
                page.ModClientSide = part.Select(mod => mod.ClientSide).ToArray();
                if (count == 0 || ApiCommand.Instance.Serialize(page).Length <= MaxDetailsBytes)
                {
                    return count;
                }

                count--;
            }
        }

        private static string Hex(float r, float g, float b)
        {
            int Byte(float value) => (int)Math.Round(Math.Max(0f, Math.Min(1f, value)) * 255f);
            return $"#{Byte(r):X2}{Byte(g):X2}{Byte(b):X2}";
        }

        private static string Cap(string text, int length)
        {
            text = text?.Trim() ?? "";
            return text.Length <= length ? text : text.Substring(0, length);
        }

        // 32 random bytes, base64url without padding (43 characters), as the lobby expects
        private static string GenerateSecret()
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string GenerateToken()
        {
            byte[] bytes = new byte[TokenLength];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            char[] token = new char[TokenLength];
            for (int i = 0; i < TokenLength; i++)
            {
                // 256 is a multiple of 32, so this stays uniform
                token[i] = TokenAlphabet[bytes[i] % TokenAlphabet.Length];
            }

            return new string(token);
        }
    }
}
