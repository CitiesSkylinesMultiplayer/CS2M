using System;
using System.Collections.Generic;
using System.Linq;
using Colossal;
using Colossal.PSI.Common;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands;
using CS2M.Commands.ApiServer;
using CS2M.Commands.Data.Internal;
using CS2M.Helpers;
using CS2M.Mods;
using CS2M.UI;
using CS2M.Util;
using LiteNetLib;
using Unity.Entities;

namespace CS2M.Networking
{
    public class LocalPlayer : Player
    {
        private SlicedPacketStream _packetStream;
        private readonly SaveLoadHelper _saveLoadHelper;
        private NetworkManager _networkManager;
        private UISystem _uiSystem;

        public LocalPlayer()
        {
            PlayerStatusChangedEvent += PlayerStatusChanged;
            PlayerTypeChangedEvent += PlayerTypeChanged;
            _saveLoadHelper = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<SaveLoadHelper>();
        }

        public bool GetServerInfo(ConnectionConfig connectionConfig)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                return false;
            }

            _networkManager = new NetworkManager();

            _networkManager.NatHolePunchSuccessfulEvent += NatConnect;
            _networkManager.NatHolePunchFailedEvent += DirectConnect;
            _networkManager.ClientConnectSuccessfulEvent += ConnectionEstablished;
            _networkManager.ClientConnectFailedEvent += ConnectionFailed;
            _networkManager.ClientDisconnectEvent += Inactive;
            _networkManager.ClientSetupFailedEvent += SetupFailed;

            if (!_networkManager.InitConnect(connectionConfig))
            {
                _uiSystem.SetJoinErrors("CS2M.UI.JoinError.ClientFailed");
                return false;
            }

            if (!_networkManager.SetupNatConnect())
            {
                _uiSystem.SetJoinErrors("CS2M.UI.JoinError.InvalidIP");
                return false;
            }

            PlayerType = PlayerType.CLIENT;
            PlayerStatus = PlayerStatus.GET_SERVER_INFO;
            return true;
        }

        public bool NatConnect()
        {
            if (PlayerStatus != PlayerStatus.GET_SERVER_INFO)
            {
                return false;
            }

            if (!_networkManager.Connect())
            {
                _uiSystem.SetJoinErrors("CS2M.UI.JoinError.FailedToConnect");
                Inactive();
                return false;
            }

            PlayerStatus = PlayerStatus.NAT_CONNECT;
            return true;
        }

        public bool DirectConnect()
        {
            if (PlayerStatus != PlayerStatus.GET_SERVER_INFO &&
                PlayerStatus != PlayerStatus.NAT_CONNECT)
            {
                return false;
            }

            if (!_networkManager.Connect())
            {
                _uiSystem.SetJoinErrors("CS2M.UI.JoinError.FailedToConnect");
                Inactive();
                return false;
            }

            PlayerStatus = PlayerStatus.DIRECT_CONNECT;
            return true;
        }

        public bool SetupFailed()
        {
            _uiSystem.SetJoinErrors("CS2M.UI.JoinError.InvalidIP");
            Inactive();
            return true;
        }

        public bool ConnectionFailed()
        {
            _uiSystem.SetJoinErrors("CS2M.UI.JoinError.FailedToConnect");
            Inactive();
            return true;
        }

        public bool ConnectionEstablished()
        {
            if (PlayerStatus != PlayerStatus.NAT_CONNECT &&
                PlayerStatus != PlayerStatus.DIRECT_CONNECT)
            {
                return false;
            }

            SendToServer(new PreconditionsCheckCommand
            {
                Username = Username,
                Password = GetConnectionPassword(),
                ModVersion = VersionUtil.GetModVersion(),
                GameVersion = VersionUtil.GetGameVersion(),
                Mods = ModSupport.Instance.RequiredModsForSync,
                DlcIds = DlcCompat.RequiredDLCsForSync,
            });

            PlayerStatus = PlayerStatus.CONNECTION_ESTABLISHED;
            return true;
        }

        public void PreconditionsError(PreconditionsErrorCommand command)
        {
            // Only an answer to our own join request; never ends a session we're already in
            if (PlayerType != PlayerType.CLIENT || PlayerStatus != PlayerStatus.CONNECTION_ESTABLISHED)
            {
                Log.Warn("Ignored a join error outside of joining");
                return;
            }

            Inactive();
            command.DlcIds ??= new List<int>();
            command.Mods ??= new List<string>();
            var errors = new List<string>();
            PreconditionsUtil.Errors err = command.Errors;
            if (err.HasFlag(PreconditionsUtil.Errors.GAME_VERSION_MISMATCH))
            {
                errors.Add("precondition:GAME_VERSION_MISMATCH");
                errors.Add(command.GameVersion.ToString());
                errors.Add(VersionUtil.GetGameVersion().ToString());
            }

            if (err.HasFlag(PreconditionsUtil.Errors.MOD_VERSION_MISMATCH))
            {
                errors.Add("precondition:MOD_VERSION_MISMATCH");
                errors.Add(command.ModVersion.ToString());
                errors.Add(VersionUtil.GetModVersion().ToString());
            }

            if (err.HasFlag(PreconditionsUtil.Errors.USERNAME_NOT_AVAILABLE))
            {
                errors.Add("precondition:USERNAME_NOT_AVAILABLE");
            }

            if (err.HasFlag(PreconditionsUtil.Errors.SERVER_FULL))
            {
                errors.Add("precondition:SERVER_FULL");
            }

            if (err.HasFlag(PreconditionsUtil.Errors.PASSWORD_INCORRECT))
            {
                errors.Add("precondition:PASSWORD_INCORRECT");
            }

            if (err.HasFlag(PreconditionsUtil.Errors.DLCS_MISMATCH))
            {
                List<int> clientDLCs = DlcCompat.RequiredDLCsForSync;
                List<int> serverDLCs = command.DlcIds;

                IEnumerable<string> clientNotServer = clientDLCs.Where(mod => !serverDLCs.Contains(mod))
                    .Select(id => DlcCompat.GetDisplayName(new DlcId(id)));
                IEnumerable<string> serverNotClient = serverDLCs.Where(mod => !clientDLCs.Contains(mod))
                    .Select(id => DlcCompat.GetDisplayName(new DlcId(id)));

                errors.Add("precondition:DLCS_MISMATCH");
                errors.Add(string.Join(", ", serverNotClient));
                errors.Add(string.Join(", ", clientNotServer));
            }

            if (err.HasFlag(PreconditionsUtil.Errors.MODS_MISMATCH))
            {
                List<string> clientMods = ModSupport.Instance.RequiredModsForSync;
                List<string> serverMods = command.Mods;

                IEnumerable<string> clientNotServer = clientMods.Where(mod => !serverMods.Contains(mod));
                IEnumerable<string> serverNotClient = serverMods.Where(mod => !clientMods.Contains(mod));

                errors.Add("precondition:MODS_MISMATCH");
                errors.Add(string.Join(", ", serverNotClient));
                errors.Add(string.Join(", ", clientNotServer));
            }

            _uiSystem.SetJoinErrors(errors.ToArray());
        }

        public bool WaitingToJoin()
        {
            if (PlayerStatus != PlayerStatus.CONNECTION_ESTABLISHED)
            {
                return false;
            }

            //TODO: Implement JoinRequest

            PlayerStatus = PlayerStatus.WAITING_TO_JOIN;
            return DownloadingMap(); //TODO: Switch to 'return true;', when JoinRequest implemented
        }

        public bool DownloadingMap()
        {
            if (PlayerStatus != PlayerStatus.WAITING_TO_JOIN)
            {
                return false;
            }

            // Change state to downloading map, next step is to wait until all
            // map packets have been received by `SliceReceived` below.
            PlayerStatus = PlayerStatus.DOWNLOADING_MAP;
            _uiSystem.SetLoadProgress(0, 0);
            return true;
        }

        /// <summary>
        ///     Largest city we accept from a host.
        /// </summary>
        private const long MaxWorldBytes = 1024L * 1024 * 1024;

        // The size the first slice announced (its length plus RemainingBytes)
        private long _expectedWorldBytes;

        public void SliceReceived(WorldTransferCommand cmd)
        {
            if (PlayerType != PlayerType.CLIENT)
            {
                return;
            }

            if (cmd.WorldSlice == null || cmd.RemainingBytes < 0 || (cmd.NewTransfer && cmd.WorldSlice.Length == 0))
            {
                Log.Warn("Received an invalid world slice");
                return;
            }

            // Resync: the host sends the city again while we're playing. Going back to downloading sets the
            // multiplayer role to None, so the sync systems hold what arrives until the reloaded city is running.
            if (cmd.NewTransfer && PlayerStatus == PlayerStatus.PLAYING)
            {
                Log.Info("Resync: the host is sending its city again");
                PlayerStatus = PlayerStatus.DOWNLOADING_MAP;
                _uiSystem.SetLoadProgress(0, 0);
            }

            if (PlayerStatus != PlayerStatus.DOWNLOADING_MAP)
            {
                Log.Warn("Received world slice, but not in downloading state");
                return;
            }

            if (cmd.NewTransfer)
            {
                _expectedWorldBytes = (long)cmd.WorldSlice.Length + cmd.RemainingBytes;
                if (_expectedWorldBytes > MaxWorldBytes)
                {
                    Log.Warn($"The host's city is {_expectedWorldBytes} bytes, more than the {MaxWorldBytes} allowed");
                    _packetStream = null;
                    _uiSystem.SetJoinErrors("CS2M.UI.JoinError.DownloadFailed");
                    Inactive();
                    return;
                }

                _packetStream = new SlicedPacketStream(cmd.WorldSlice.Length);
            }
            else if (_packetStream == null)
            {
                Log.Warn("Received world slice without initialized packet stream");
                _uiSystem.SetJoinErrors("CS2M.UI.JoinError.DownloadFailed");
                Inactive();
                return;
            }

            // Every slice must add up to what the first one announced
            if (_packetStream.Length + cmd.WorldSlice.Length + cmd.RemainingBytes != _expectedWorldBytes)
            {
                Log.Warn("Received a world slice that doesn't fit the transfer");
                _packetStream = null;
                _uiSystem.SetJoinErrors("CS2M.UI.JoinError.DownloadFailed");
                Inactive();
                return;
            }

            _packetStream.AppendSlice(cmd.WorldSlice);
            _uiSystem.SetLoadProgress((int)_packetStream.Length, cmd.RemainingBytes);

            if (cmd.RemainingBytes == 0)
            {
                LoadingMap();
            }
        }

        public void LoadingMap()
        {
            if (PlayerStatus != PlayerStatus.DOWNLOADING_MAP)
            {
                return;
            }

            PlayerStatus = PlayerStatus.LOADING_MAP;
            TaskManager.instance.EnqueueTask("LoadMap", async () =>
            {
                bool success = await _saveLoadHelper.LoadGame(_packetStream);
                if (success)
                {
                    _packetStream = null; // Clean up save game memory
                    Playing();
                }
                // TODO: Error handling
            });
        }

        public bool Playing()
        {
            if (PlayerStatus != PlayerStatus.LOADING_MAP)
            {
                return false;
            }

            PlayerStatus = PlayerStatus.PLAYING;
            return true;
        }

        // INACTIVE -> PLAYING (Server)
        public bool Playing(ConnectionConfig connectionConfig)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                return false;
            }

            _networkManager = new NetworkManager();

            bool serverStarted = _networkManager.StartServer(connectionConfig);
            if (!serverStarted)
            {
                return false;
            }

            //TODO: Setup server variables (player list, etc.)

            PlayerStatus = PlayerStatus.PLAYING;
            PlayerType = PlayerType.SERVER;

            return true;
        }

        public void Blocked()
        {
        }

        // PLAYING -> INACTIVE
        public bool Inactive()
        {
            // if (PlayerStatus != PlayerStatus.PLAYING)
            // {
            //     return false;
            // }

            if (PlayerType == PlayerType.SERVER)
            {
                //TODO: Clear the remaining server variables
                NetworkInterface.Instance.ClearRemotePlayers();
            }
            else if (PlayerType == PlayerType.CLIENT)
            {
                //TODO: Clean-Up client
            }

            _networkManager?.Stop();

            PlayerStatus = PlayerStatus.INACTIVE;
            PlayerType = PlayerType.NONE;
            return true;
        }

        public void OnUpdate()
        {
            if (_uiSystem == null)
            {
                _uiSystem = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<UISystem>();
            }

            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                _networkManager.ProcessEvents();
            }
        }

        public void UpdateUsername(string username)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                //TODO: Print Warning
                return;
            }

            Username = username;
        }

        public void UpdatePlayerType(PlayerType playerType)
        {
            PlayerType = playerType;
        }

        public string GetConnectionPassword()
        {
            return _networkManager.GetConnectionPassword();
        }

        public void SendToAll(CommandBase message)
        {
            message.SenderId = PlayerId;
            if (PlayerType == PlayerType.SERVER)
            {
                _networkManager.SendToAllClients(message);
            }
            else
            {
                _networkManager.SendToServer(message);
            }
        }

        public void SendToClient(NetPeer peer, CommandBase message)
        {
            message.SenderId = PlayerId;
            _networkManager.SendToClient(peer, message);
        }

        public void SendToServer(CommandBase message)
        {
            if (PlayerType == PlayerType.CLIENT)
            {
                message.SenderId = PlayerId;
                _networkManager.SendToServer(message);
            }
        }

        public void SendToClients(CommandBase message)
        {
            if (PlayerType == PlayerType.SERVER)
            {
                message.SenderId = PlayerId;
                _networkManager.SendToAllClients(message);
            }
        }

        public void SendToApiServer(ApiCommandBase message)
        {
            _networkManager.SendToApiServer(message);
        }

        public void PlayerStatusChanged(PlayerStatus oldPlayerStatus, PlayerStatus newPlayerStatus)
        {
            Log.Debug($"LocalPlayer: changed player status from {oldPlayerStatus} to {newPlayerStatus}");
            UpdateRole(newPlayerStatus, PlayerType);
        }

        public void PlayerTypeChanged(PlayerType oldPlayerType, PlayerType newPlayerType)
        {
            Log.Debug($"LocalPlayer: changed player type from {oldPlayerType} to {newPlayerType}");
            UpdateRole(PlayerStatus, newPlayerType);
        }

        /// <summary>
        ///     The BaseGame sync systems only send and apply while this isn't None. A client only gets a role
        ///     once it's playing: before that there's no connection to send on, and the city isn't loaded.
        ///     Takes the new values from the change events, which fire before the property is updated.
        /// </summary>
        private static void UpdateRole(PlayerStatus status, PlayerType type)
        {
            Command.CurrentRole = status != PlayerStatus.PLAYING ? MultiplayerRole.None : type switch
            {
                PlayerType.SERVER => MultiplayerRole.Server,
                PlayerType.CLIENT => MultiplayerRole.Client,
                _ => MultiplayerRole.None
            };
            Log.Info($"Multiplayer role: {Command.CurrentRole}");
        }
    }
}
