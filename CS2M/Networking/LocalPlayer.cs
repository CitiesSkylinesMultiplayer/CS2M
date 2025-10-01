using Colossal.Entities;
using Colossal.IO.AssetDatabase;
using Colossal.Win32;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands.ApiServer;
using CS2M.UI;
using Game;
using Game.Assets;
using Game.City;
using Game.PSI;
using Game.Routes;
using Game.SceneFlow;
using Game.Settings;
using Game.Simulation;
using Game.UI;
using Game.UI.Menu;
using Game.Zones;
using LiteNetLib;
using LiteNetLib.Utils;
using MessagePack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Entities;
using UnityEngine;
using static Game.Rendering.Debug.RenderPrefabRenderer;

namespace CS2M.Networking
{
    public class LocalPlayer : Player
    {

        public static LocalPlayer Instance { get; private set; }

        private NetworkManager _networkManager;
        private string _message;

        public const string stateSnapshotName = "CS2M_Server_State";

        public LocalPlayer() : base()
        {
            Instance = this;
        }

        public bool GetServerInfo(ConnectionConfig connectionConfig)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                return false;
            }
            // Check if is in main menu

            _networkManager = new NetworkManager();

            _networkManager.NatHolePunchSuccessfulEvent += NatConnect;
            _networkManager.NatHolePunchFailedEvent += DirectConnect;
            _networkManager.ClientConnectSuccessfulEvent += DownloadingMap;
            _networkManager.ClientConnectFailedEvent += Inactive;
            _networkManager.ClientDisconnectEvent += Inactive;

            if (!_networkManager.InitConnect(connectionConfig))
            {
                _message = "= Can't init connect to server =";
                printStatus(_message);
                return false;
            }

            if (!_networkManager.SetupNatConnect())
            {
                _message = "= Can't perform NAT connection to server ";
                printStatus(_message);
                return false;
            }

            PlayerType = PlayerType.CLIENT;
            PlayerStatus = PlayerStatus.GET_SERVER_INFO;

            _message = "= Connection to server is succsesful! =";
            printStatus(_message);

            return true;
        }

        private void printStatus(string message)
        {
            Log.Info(message);
            UISystem.Instance.piblishNetworkStateInUI(message);
        }

        public bool NatConnect()
        {
            if (PlayerStatus != PlayerStatus.GET_SERVER_INFO)
            {
                return false;
            }
            
            if (!_networkManager.Connect())
            {
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
                Inactive();
                return false;
            }
            
            PlayerStatus = PlayerStatus.DIRECT_CONNECT;
            return true;
        }

        public bool DownloadingMap()
        {
            // TODO: Change, when implemented Map transfer

            if (PlayerType == PlayerType.SERVER)
            {
                Command.CurrentRole = MultiplayerRole.Server;
            }
            else if (PlayerType == PlayerType.CLIENT)
            {
                Command.CurrentRole= MultiplayerRole.Client;
            }
            
            Command.SendToAll(new textMessageCommand($"= {Username} joined game ="));
            _networkManager.CancelConnectTimeout();

            return Playing();
        }



        public void LoadingMap()
        {
        }

        public bool Playing()
        {
            return true;
        }

        public string saveCurrentState()
        {

            // Save Game Run 
            Log.Info("World name:" + World.DefaultGameObjectInjectionWorld.Name);


            //var gm = GameManager.instance;
            //if (gm != null && GameManager.instance.gameMode.IsGame() )
            if (UISystem.Instance._gameMode == GameMode.Game)
            {
                CityConfigurationSystem cityConfig = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<CityConfigurationSystem>();

                XPSystem curXPsys = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<XPSystem>();

                Guid session = Telemetry.GetCurrentSession();

                var currentSaveInfo = new SaveInfo
                {
                    cityName = cityConfig.overrideCityName,
                    mapName = cityConfig.cityName,
                    population = Telemetry.gameplayData.population.m_Population,
                    money = Telemetry.gameplayData.moneyAmount,
                    sessionGuid = Telemetry.GetCurrentSession(),
                    lastModified = DateTime.Now
                };

                // Creating preview, like in AutoSaveSystem.SafeAutoSave();
                RenderTexture preview = ScreenCaptureHelper.CreateRenderTarget("PreviewSaveGame-Auto", 680, 383);
                ScreenCaptureHelper.CaptureScreenshot(Camera.main, preview, new MenuHelpers.SaveGamePreviewSettings());

                MenuUISystem existingSystemManaged = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<MenuUISystem>();

                //COSystemBase.baseLog.InfoFormat("Auto-saving {0}...", text);
                Log.Info("Saving ... " + stateSnapshotName);

                try
                {
                    ILocalAssetDatabase cs2SaveDatabaseTarget = AssetDatabase.user;

                    /*if (cs2SaveDatabaseTarget.Exists<PackageAsset>(SaveHelpers.GetAssetDataPath<SaveGameMetadata>(cs2SaveDatabaseTarget, text), out var asset))
                    {
                        cs2SaveDatabaseTarget.DeleteAsset(asset);
                    }*/

                    GameManager.instance.Save(stateSnapshotName, existingSystemManaged.GetSaveInfo(autoSave: false), cs2SaveDatabaseTarget, preview).ContinueWith(task =>
                    {
                        if (task.Exception != null)
                            Log.Error($"Error saving: {task.Exception}");
                    });

                    ;
                }
                catch (Exception exception)
                {
                    //COSystemBase.baseLog.Error(exception);
                    Log.Error(exception.ToString());
                }
                finally
                {
                    //CoreUtils.Destroy((UnityEngine.Object)preview);
                    UnityEngine.Object.Destroy(preview);

                }

                Log.Info(
                    $"cityName: {currentSaveInfo.cityName}, " +
                    $"mapName: {currentSaveInfo.mapName}, " +
                    $"population: {currentSaveInfo.population}, " +
                    $"money: {currentSaveInfo.money}, " +
                    $"sessionGuid: {currentSaveInfo.sessionGuid}, " +
                    $"lastModified: {currentSaveInfo.lastModified}. "
                    );

                // Check if path correct
                string fileFullPath = Environment.GetEnvironmentVariable("CSII_USERDATAPATH") + "/Saves/" + Mod.ModSettings.userID + "/" + stateSnapshotName + ".cok";
                if (File.Exists(fileFullPath))
                {
                    Log.Info($"Save file crated succesfully {fileFullPath}");
                    return fileFullPath;
                } else
                {
                    string msg_id = "You didn't set correct USER ID in mod's settings!";
                    ShowWarningMessage("You didn't set correct USER ID in mod's settings! Plaese visit the mod settings, and fill your USER ID.", "No USER_ID set!");
                    Log.Warn($"Save NOT created. {msg_id} CheckPath: {fileFullPath}");
                    return null;
                }                
                

                // Environment.GetEnvironmentVariable("CSII_USERDATAPATH") = C:\Users\SCAD\AppData\LocalLow\Colossal Order\Cities Skylines II/
                // AssetDatabase.user = User
                // return Environment.GetEnvironmentVariable("CSII_USERDATAPATH") + "/Saves/" + AssetDatabase.user.GetHashCode();

                //Log.FindAllMethodsReturning<ILocalAssetDatabase>();

            }
            else
            {
                Log.Info("assetdb root:" + AssetDatabase.user.rootPath + " " + AssetDatabase.user.GetHashCode());
                //assetdb root: C:/Users/SCAD/AppData/LocalLow/Colossal Order/Cities Skylines II

                //AutoSaveSystem.PruneAutoSaves
                //SharedSettings.instance.general

                //GameManager.instance.userInterface.appBindings.ShowMessageDialog(new MessageDialog("Paradox.TELEMETRY_CONSENT_ERROR_TITLE", "Paradox.TELEMETRY_CONSENT_ERROR_DESCRIPTION", "Common.OK"), delegate {} );

                ShowWarningMessage("Nothing to save, because you are in main menu. Here you can discauss upcoming session, but the game simulation not started yet. Thus your stat will not be saved, or transfred to clients.", "Game in main menu!");

                
                //MessageDialog msg2 = new MessageDialog("title", "message", "confact", "act1 act2");
                //AppBindings apb = new AppBindings();
                //apb.ShowMessageDialog(msg2, null);

                Log.Warn(">>> Game in main menu. Nothing to save.");
                return null;
            }



        }

        public void ShowWarningMessage(string message, string tille)
        {
            ErrorDialogManager.Initialize();
            ErrorDialog msgd = new ErrorDialog();
            msgd.localizedTitle = "Game in main menu.";
            msgd.localizedMessage = "Nothing to save, because you are in main menu. Here you can discauss upcoming session, but the game simulation not started yet. Thus your stat will not be saved, or transfred to clients.";
            msgd.severity = ErrorDialog.Severity.Warning;
            msgd.actions = ErrorDialog.Actions.None;
            ErrorDialogManager.ShowErrorDialog(msgd);
            //ErrorDialogManager.Clear();
        }

        // INACTIVE -> PLAYING (Server)
        public bool Playing(ConnectionConfig connectionConfig)
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                return false;
            }

            _networkManager = new NetworkManager();

            // Start the LiteNetLib server
            bool serverStarted = _networkManager.StartServer(connectionConfig);

            if (!serverStarted)
            {
                _message = "= Error! The server failed to start. =";
                printStatus(_message);
                return false;
            }

            //TODO: Setup server variables (player list, etc.)
            PlayerType = PlayerType.SERVER;
            PlayerStatus = PlayerStatus.PLAYING;

            _message = "= The server has started succesfully. =";
            printStatus(_message);

            string specailSave = saveCurrentState();

            if (specailSave != null)
            {

                Command.SendToClients(new StateSyncCommand(specailSave));
                return true;
            }
            else
            {
                return false;
            }

                
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
                //TODO: Clear server variables (player list, etc.)
            } 
            else if (PlayerType == PlayerType.CLIENT)
            {
                //TODO: Clean-Up client
            }
            _networkManager.Stop();

            PlayerStatus = PlayerStatus.INACTIVE;
            return true;
        }

        public void OnUpdate()
        {
            if (PlayerStatus != PlayerStatus.INACTIVE)
            {
                _networkManager.ProcessEvents();
            }
        }

        public void SendToAll(CommandBase message)
        {
            message.SenderId = PlayerId;
            if (PlayerType == PlayerType.SERVER)
            {
                _networkManager.SendToAllClients(message);
                Command.GetCommandHandler(message.GetType())?.Parse(message);
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
    }
}
