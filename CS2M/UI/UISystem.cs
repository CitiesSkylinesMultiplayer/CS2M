using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using CS2M.API.Networking;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.Settings;
using CS2M.Util;
using Game;
using Game.UI;
using Game.UI.InGame;

namespace CS2M.UI
{
    public partial class UISystem : UISystemBase
    {
        private ValueBinding<GameScreenUISystem.GameScreen> _activeGameScreenBinding;
        private ValueBinding<int> _activeMenuScreenBinding;
        private ValueBinding<int> _downloadDone;
        private ValueBinding<int> _downloadRemaining;
        private ValueBinding<int> _downloadSpeed;

        private GameMode _gameMode = GameMode.Other;
        private ValueBinding<bool> _hostMenuVisible;
        private ValueBinding<int> _hostPort;

        private ValueBinding<string> _joinIPAddress;
        private ValueBinding<bool> _joinMenuVisible;
        private ValueBinding<int> _joinPort;
        private ValueBinding<List<string>> _joinErrorMessage;

        private ValueBinding<List<ModSupportStatus>> _modSupportStatus;
        private ValueBinding<string> _playerStatus;

        private ValueBinding<string> _username;

        // Server browser: the lobby's session list as JSON, and "idle", "loading", "ok" or "error"
        private ValueBinding<string> _lobbySessions;
        private ValueBinding<string> _lobbyStatus;
        private readonly object _lobbyLock = new();
        private (string Status, string Json)? _lobbyResult;
        private const int MaxLobbyResponse = 4 * 1024 * 1024;
        private static readonly Regex SessionToken = new Regex("^[A-Za-z0-9_-]{4,64}$");

        // In-game HUD
        private ValueBinding<bool> _hudCollapsed;
        private ValueBinding<string> _disconnectNotice;
        private ValueBinding<bool> _isHost;

        // Join menu: this game's mods and versions, to compare with the listed sessions, and the Recent tab
        private ValueBinding<string> _localMods;
        private ValueBinding<string> _recentSessions;
        private ValueBinding<int> _hostMaxPlayers;
        // Passwords are kept for this game run only, never saved
        private ValueBinding<string> _hostPassword;
        private ValueBinding<string> _joinPassword;
        private RecentSessions.Entry _pendingRecent;
        private readonly Stopwatch _sinceModsRefresh = new();
        private const int MaxPlayersLimit = 64;

        private readonly Stopwatch _downloadTimer = new();
        private int _lastDownloadDone = 0;

        private ChatPanel ChatPanel { get; } = new();

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            _activeMenuScreenBinding = BindingsHelper.GetValueBinding<int>("menu", "activeScreen");
            _activeGameScreenBinding =
                BindingsHelper.GetValueBinding<GameScreenUISystem.GameScreen>("game", "activeScreen");

            GamePanelUISystem gameChatPanel = World.GetOrCreateSystemManaged<GamePanelUISystem>();
            gameChatPanel.SetDefaultArgs(ChatPanel);
            ChatPanel.WelcomeChatMessage();
        }

        protected override void OnUpdate()
        {
            string notice = NetworkInterface.DisconnectNotice ?? "";
            if (notice != _disconnectNotice.value)
            {
                _disconnectNotice.Update(notice);
            }

            bool isHost = NetworkInterface.Instance.LocalPlayer.PlayerType == PlayerType.SERVER;
            if (isHost != _isHost.value)
            {
                _isHost.Update(isHost);
            }

            base.OnUpdate();

            // Hand the server browser list over from the download thread
            (string Status, string Json)? result;
            lock (_lobbyLock)
            {
                result = _lobbyResult;
                _lobbyResult = null;
            }

            if (result != null)
            {
                if (result.Value.Json != null)
                {
                    _lobbySessions.Update(result.Value.Json);
                }

                _lobbyStatus.Update(result.Value.Status);
            }

            // The playset arrives asynchronously, so keep the local mod list fresh while the join menu is open
            if (_joinMenuVisible.value && (!_sinceModsRefresh.IsRunning || _sinceModsRefresh.Elapsed.TotalSeconds >= 2))
            {
                UpdateLocalMods();
            }
        }

        private void UpdateLocalMods()
        {
            _sinceModsRefresh.Restart();
            string json = EnabledMods.ToJson(EnabledMods.Get());
            if (json != _localMods.value)
            {
                _localMods.Update(json);
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            AddBinding(new TriggerBinding(Mod.Name, "ShowMultiplayerMenu", ShowMultiplayerMenu));
            AddBinding(new TriggerBinding(Mod.Name, "HideJoinGameMenu", HideJoinGameMenu));
            AddBinding(new TriggerBinding(Mod.Name, "HideHostGameMenu", HideHostGameMenu));

            AddBinding(new TriggerBinding<string>(Mod.Name, "SetJoinIpAddress", ip => { _joinIPAddress.Update(ip); }));
            AddBinding(new TriggerBinding<int>(Mod.Name, "SetJoinPort", port => { _joinPort.Update(port); }));
            AddBinding(new TriggerBinding<int>(Mod.Name, "SetHostPort", port => { _hostPort.Update(port); }));
            AddBinding(new TriggerBinding<string>(Mod.Name, "SetUsername",
                username =>
                {
                    // No trimming while typing (spaces between words); the host checks the name on join
                    username = new string((username ?? "").Where(c => !char.IsControl(c)).ToArray());
                    _username.Update(username.Length <= TextRules.MaxUsernameLength
                        ? username
                        : username.Substring(0, TextRules.MaxUsernameLength));
                }));

            AddBinding(new TriggerBinding(Mod.Name, "JoinGame", JoinGame));
            AddBinding(new TriggerBinding<string, string, string>(Mod.Name, "JoinSession", JoinSession));
            AddBinding(new TriggerBinding<int>(Mod.Name, "RemoveRecent", RemoveRecent));
            AddBinding(new TriggerBinding(Mod.Name, "RefreshLobby", RefreshLobby));
            AddBinding(_lobbySessions = new ValueBinding<string>(Mod.Name, "LobbySessions", "[]"));
            AddBinding(_lobbyStatus = new ValueBinding<string>(Mod.Name, "LobbyStatus", "idle"));
            AddBinding(new TriggerBinding(Mod.Name, "HostGame", HostGame));
            AddBinding(new TriggerBinding(Mod.Name, "StopServer", StopServer));
            AddBinding(new TriggerBinding(Mod.Name, "Resync", () => NetworkInterface.Instance.Resync()));

            AddBinding(_joinMenuVisible = new ValueBinding<bool>(Mod.Name, "JoinMenuVisible", false));
            AddBinding(_hostMenuVisible = new ValueBinding<bool>(Mod.Name, "HostMenuVisible", false));
            AddBinding(_modSupportStatus = new ValueBinding<List<ModSupportStatus>>(Mod.Name, "modSupport",
                new List<ModSupportStatus>(), new ListWriter<ModSupportStatus>(new ValueWriter<ModSupportStatus>())));

            ModSettings settings = Mod.Instance?.Settings;
            AddBinding(_joinIPAddress = new ValueBinding<string>(Mod.Name, "JoinIpAddress", settings?.LastJoinIp ?? ""));
            AddBinding(_joinPort = new ValueBinding<int>(Mod.Name, "JoinPort", settings?.LastJoinPort ?? 0));
            AddBinding(_hostPort = new ValueBinding<int>(Mod.Name, "HostPort", settings?.LastHostPort ?? 0));
            AddBinding(_hostMaxPlayers = new ValueBinding<int>(Mod.Name, "HostMaxPlayers", settings?.HostMaxPlayers ?? 0));
            AddBinding(_hostPassword = new ValueBinding<string>(Mod.Name, "HostPassword", ""));
            AddBinding(_joinPassword = new ValueBinding<string>(Mod.Name, "JoinPassword", ""));
            AddBinding(new TriggerBinding<string>(Mod.Name, "SetHostPassword", pw => _hostPassword.Update(CapPassword(pw))));
            AddBinding(new TriggerBinding<string>(Mod.Name, "SetJoinPassword", pw => _joinPassword.Update(CapPassword(pw))));
            AddBinding(new TriggerBinding<int>(Mod.Name, "SetHostMaxPlayers",
                max => { _hostMaxPlayers.Update(Math.Max(0, Math.Min(MaxPlayersLimit, max))); }));
            AddBinding(_localMods = new ValueBinding<string>(Mod.Name, "LocalMods", "[]"));
            AddBinding(new ValueBinding<string>(Mod.Name, "LocalVersions",
                "{\"game\":" + EnabledMods.JsonString(VersionUtil.GetGameVersion().ToString()) +
                ",\"mod\":" + EnabledMods.JsonString(VersionUtil.GetModVersion().ToString()) + "}"));
            AddBinding(_recentSessions = new ValueBinding<string>(Mod.Name, "RecentSessions",
                RecentSessions.ToJson(RecentSessions.Load(settings?.RecentSessions))));
            AddBinding(_username = new ValueBinding<string>(Mod.Name, "Username", settings?.LastUsername ?? ""));
            // Kick/ban from the HUD (host only); the kicked player sees the reason once
            AddBinding(new TriggerBinding<int, bool>(Mod.Name, "KickPlayer", (id, ban) =>
            {
                string name = NetworkInterface.Instance.Kick(id, ban);
                if (name != null)
                {
                    CS2M.API.Chat.Instance?.PrintGameMessage($"{name} was {(ban ? "banned" : "kicked")}");
                }
            }));
            AddBinding(new TriggerBinding(Mod.Name, "ClearBans", () => NetworkInterface.Instance.ClearBans()));
            AddBinding(new TriggerBinding(Mod.Name, "DismissDisconnectNotice", () =>
            {
                NetworkInterface.DisconnectNotice = null;
                _disconnectNotice.Update("");
            }));
            AddBinding(_disconnectNotice = new ValueBinding<string>(Mod.Name, "DisconnectNotice", ""));
            AddBinding(_isHost = new ValueBinding<bool>(Mod.Name, "IsHost", false));
            // Multiplayer HUD hidden/shown, remembered between games
            AddBinding(_hudCollapsed = new ValueBinding<bool>(Mod.Name, "HudCollapsed", settings?.HudCollapsed ?? false));
            AddBinding(new TriggerBinding<bool>(Mod.Name, "SetHudCollapsed", collapsed =>
            {
                _hudCollapsed.Update(collapsed);
                if (Mod.Instance?.Settings != null)
                {
                    Mod.Instance.Settings.HudCollapsed = collapsed;
                    Mod.Instance.Settings.ApplyAndSave();
                }
            }));

            AddBinding(_playerStatus = new ValueBinding<string>(Mod.Name, "PlayerStatus", "INACTIVE"));
            AddBinding(_downloadDone = new ValueBinding<int>(Mod.Name, "DownloadDone", 0));
            AddBinding(_downloadRemaining = new ValueBinding<int>(Mod.Name, "DownloadRemaining", 0));
            AddBinding(_downloadSpeed = new ValueBinding<int>(Mod.Name, "DownloadSpeed", 0));
            AddBinding(_joinErrorMessage = new ValueBinding<List<string>>(Mod.Name, "JoinErrorMessage",
                new List<string>(), new ListWriter<string>()));

            RegisterChatPanelBindings();

            NetworkInterface.Instance.LocalPlayer.PlayerStatusChangedEvent += (_, status) =>
            {
                _playerStatus.Update(status.ToString());
                if (status == PlayerStatus.LOADING_MAP)
                {
                    _joinMenuVisible.Update(false);
                    RememberJoin();
                }
                else if (status == PlayerStatus.INACTIVE)
                {
                    _pendingRecent = null;
                }
            };
        }

        private void RefreshModSupport()
        {
            _modSupportStatus.Update(DlcCompat.GetDlcSupport().Concat(ModCompat.GetModSupport()).ToList());
        }

        private void ShowMultiplayerMenu()
        {
            RefreshModSupport();
            if (_gameMode == GameMode.MainMenu)
            {
                EnabledMods.RefreshPlayset(true);
                UpdateLocalMods();
                _activeMenuScreenBinding.Update(99);
                _joinMenuVisible.Update(true);
                RefreshLobby();
            }
            else if (_gameMode == GameMode.Game)
            {
                _activeGameScreenBinding.Update((GameScreenUISystem.GameScreen)99);
                _hostMenuVisible.Update(true);
            }
        }

        private void HideJoinGameMenu()
        {
            _activeMenuScreenBinding.Update(0);
            _activeGameScreenBinding.Update(GameScreenUISystem.GameScreen.PauseMenu);
            _joinMenuVisible.Update(false);
        }

        private void HideHostGameMenu()
        {
            _activeMenuScreenBinding.Update(0);
            _activeGameScreenBinding.Update(GameScreenUISystem.GameScreen.PauseMenu);
            _hostMenuVisible.Update(false);
        }

        private void JoinGame()
        {
            SaveConnection(settings =>
            {
                settings.LastJoinIp = _joinIPAddress.value;
                settings.LastJoinPort = _joinPort.value;
            });
            NetworkInterface.Instance.UpdateLocalPlayerUsername(_username.value?.Trim());
            _pendingRecent = new RecentSessions.Entry { Ip = _joinIPAddress.value?.Trim() ?? "", Port = _joinPort.value };
            NetworkInterface.Instance.Connect(new ConnectionConfig(_joinIPAddress.value, _joinPort.value, _joinPassword.value ?? ""));
        }

        /// <summary>
        ///     Joins a session from the server browser, through the API server's NAT introduction.
        /// </summary>
        /// <param name="token">The session's lobby token.</param>
        /// <param name="cityName">The city as listed, only for the Recent tab.</param>
        /// <param name="hostName">The host as listed, only for the Recent tab.</param>
        private void JoinSession(string token, string cityName, string hostName)
        {
            if (token == null || !SessionToken.IsMatch(token))
            {
                return;
            }

            SaveConnection(_ => { });
            NetworkInterface.Instance.UpdateLocalPlayerUsername(_username.value?.Trim());
            _pendingRecent = new RecentSessions.Entry
            {
                Token = token,
                Name = Clean(cityName, 64),
                Host = Clean(hostName, TextRules.MaxUsernameLength)
            };
            NetworkInterface.Instance.Connect(new ConnectionConfig(token, _joinPassword.value ?? ""));
        }

        /// <summary>
        ///     The join got as far as loading the city: remember it for the Recent tab.
        /// </summary>
        private void RememberJoin()
        {
            RecentSessions.Entry entry = _pendingRecent;
            _pendingRecent = null;
            ModSettings settings = Mod.Instance?.Settings;
            if (entry == null || settings == null)
            {
                return;
            }

            List<RecentSessions.Entry> entries = RecentSessions.Add(RecentSessions.Load(settings.RecentSessions), entry);
            settings.RecentSessions = RecentSessions.ToJson(entries);
            settings.ApplyAndSave();
            _recentSessions.Update(settings.RecentSessions);
        }

        private void RemoveRecent(int index)
        {
            ModSettings settings = Mod.Instance?.Settings;
            if (settings == null)
            {
                return;
            }

            List<RecentSessions.Entry> entries = RecentSessions.Load(settings.RecentSessions);
            if (index < 0 || index >= entries.Count)
            {
                return;
            }

            entries.RemoveAt(index);
            settings.RecentSessions = RecentSessions.ToJson(entries);
            settings.ApplyAndSave();
            _recentSessions.Update(settings.RecentSessions);
        }

        private static string Clean(string text, int maxLength)
        {
            text = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength);
        }

        /// <summary>
        ///     Loads the server browser list from the lobby server, off the main thread.
        /// </summary>
        private void RefreshLobby()
        {
            if (_lobbyStatus.value == "loading")
            {
                return;
            }

            string lobbyUrl = Mod.Instance?.Settings?.LobbyUrl;
            if (string.IsNullOrWhiteSpace(lobbyUrl))
            {
                _lobbyStatus.Update("error");
                return;
            }

            string url = lobbyUrl.Trim().TrimEnd('/') + "/sessions";
            string userAgent = "CS2M/" + VersionUtil.GetModVersion();
            _lobbyStatus.Update("loading");
            Task.Run(() =>
            {
                (string, string) result;
                try
                {
                    // Cloudflare needs TLS 1.2, which older Mono doesn't enable by default
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var client = new CSMWebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        client.Headers[HttpRequestHeader.UserAgent] = userAgent;
                        client.Headers[HttpRequestHeader.Accept] = "application/json";
                        string body = client.DownloadString(url).Trim();
                        if (body.Length > MaxLobbyResponse || !body.StartsWith("["))
                        {
                            throw new FormatException("not a session list");
                        }

                        result = ("ok", body);
                    }
                }
                catch (Exception e)
                {
                    Log.Warn($"Could not load the server list from {url}: {e.Message}");
                    result = ("error", null);
                }

                lock (_lobbyLock)
                {
                    _lobbyResult = result;
                }
            });
        }

        private static string CapPassword(string password)
        {
            password = password ?? "";
            return password.Length > 64 ? password.Substring(0, 64) : password;
        }

        private void HostGame()
        {
            SaveConnection(settings =>
            {
                settings.LastHostPort = _hostPort.value;
                settings.HostMaxPlayers = _hostMaxPlayers.value;
            });
            NetworkInterface.Instance.UpdateLocalPlayerUsername(_username.value?.Trim());
            NetworkInterface.Instance.StartServer(new ConnectionConfig(_hostPort.value, string.IsNullOrEmpty(_hostPassword.value) ? null : _hostPassword.value));
        }

        private void SaveConnection(Action<ModSettings> update)
        {
            ModSettings settings = Mod.Instance?.Settings;
            if (settings == null)
            {
                return;
            }

            update(settings);
            if (!string.IsNullOrEmpty(_username.value))
            {
                settings.LastUsername = _username.value;
            }

            settings.ApplyAndSave();
        }

        private void StopServer()
        {
            NetworkInterface.Instance.StopServer();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _gameMode = mode;
        }

        private void RegisterChatPanelBindings()
        {
            AddBinding(ChatPanel.ChatMessages);
            AddBinding(ChatPanel.CurrentUsername);
            AddBinding(ChatPanel.LocalChatMessage);
            AddBinding(ChatPanel.SendChatMessage);
            AddBinding(ChatPanel.SetLocalChatMessage);
        }

        public void SetLoadProgress(int downloadDone, int downloadRemaining)
        {
            if (downloadDone == 0)
            {
                _downloadTimer.Restart();
                _lastDownloadDone = 0;
            }

            long elapsedMillis = _downloadTimer.ElapsedMilliseconds;
            if (elapsedMillis > 500)
            {
                int bytesDiff = downloadDone - _lastDownloadDone;
                _downloadTimer.Restart();
                _lastDownloadDone = downloadDone;
                _downloadSpeed.Update((int)((bytesDiff / elapsedMillis) * 1000));
            }

            _downloadDone.Update(downloadDone);
            _downloadRemaining.Update(downloadRemaining);
        }

        public void SetJoinErrors(params string[] errorMessageKey)
        {
            _joinErrorMessage.Update(errorMessageKey.ToList());
        }
    }
}
