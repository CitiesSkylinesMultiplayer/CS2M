using Colossal.Logging;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using CS2M.API.Networking;
using CS2M.Mods;
using CS2M.Networking;
using Game;
using Game.UI;
using Game.UI.InGame;
using System.Collections.Generic;
using System.Diagnostics;

namespace CS2M.UI
{

    public partial class UISystem : UISystemBase
    {
        private ValueBinding<int> _activeMenuScreenBinding;
        private ValueBinding<int> _activeGameScreenBinding;
        private ValueBinding<bool> _joinMenuVisible;
        private ValueBinding<bool> _hostMenuVisible;

        public ValueBinding<string> _NetworkStates;

        private ValueBinding<string> _joinIPAddress;
        private ValueBinding<int> _joinPort;
        private ValueBinding<int> _hostPort;
        private ValueBinding<string> _username;
        private ValueBinding<bool> _joinGameEnabled;
        private ValueBinding<bool> _hostGameEnabled;
        private ValueBinding<string> _playerStatus;

        private ValueBinding<List<ModSupportStatus>> _modSupportStatus;

        private GameMode _gameMode = GameMode.Other;

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            _activeMenuScreenBinding = BindingsHelper.GetValueBinding<int>("menu", "activeScreen");
            _activeGameScreenBinding = BindingsHelper.GetValueBinding<int>("game", "activeScreen");
            var panel = World.GetOrCreateSystemManaged<GamePanelUISystem>();
            panel.SetDefaultArgs(new ChatPanel());
        }

        protected override void OnCreate()
        {
            base.OnCreate();



            AddBinding(new TriggerBinding(nameof(CS2M), "ShowMultiplayerMenu", ShowUITraversal));
            AddBinding(new TriggerBinding(nameof(CS2M), "HideJoinGameMenu", HideJoinGameMenu));
            AddBinding(new TriggerBinding(nameof(CS2M), "HideHostGameMenu", HideHostGameMenu));

            AddBinding(new TriggerBinding<string>(nameof(CS2M), "SetJoinIpAddress", ip =>
            {
                _joinIPAddress.Update(ip);
            }));
            AddBinding(new TriggerBinding<int>(nameof(CS2M), "SetJoinPort", port =>
            {
                _joinPort.Update(port);
            }));
            AddBinding(new TriggerBinding<int>(nameof(CS2M), "SetHostPort", port =>
            {
                _hostPort.Update(port);
            }));
            AddBinding(new TriggerBinding<string>(nameof(CS2M), "SetUsername", username =>
            {
                _username.Update(username);
            }));

            AddBinding(new TriggerBinding(nameof(CS2M), "JoinGame", JoinGame));
            AddBinding(new TriggerBinding(nameof(CS2M), "HostGame", HostGame));

            AddBinding(_joinMenuVisible = new ValueBinding<bool>(nameof(CS2M), "JoinMenuVisible", false));
            AddBinding(_hostMenuVisible = new ValueBinding<bool>(nameof(CS2M), "HostMenuVisible", false));
            AddBinding(_modSupportStatus = new ValueBinding<List<ModSupportStatus>>(nameof(CS2M), "modSupport", new List<ModSupportStatus>(),new ListWriter<ModSupportStatus>(new ValueWriter<ModSupportStatus>())));

            AddBinding(_joinIPAddress = new ValueBinding<string>(nameof(CS2M), "JoinIpAddress", "127.0.0.1"));
            AddBinding(_joinPort = new ValueBinding<int>(nameof(CS2M), "JoinPort", 4230));
            AddBinding(_hostPort = new ValueBinding<int>(nameof(CS2M), "HostPort", 4230));
            AddBinding(_username = new ValueBinding<string>(nameof(CS2M), "Username", "CS2M_Player"));
            AddBinding(_joinGameEnabled = new ValueBinding<bool>(nameof(CS2M), "JoinGameEnabled", true));
            AddBinding(_hostGameEnabled = new ValueBinding<bool>(nameof(CS2M), "HostGameEnabled", true));

            AddBinding(_playerStatus = new ValueBinding<string>(nameof(CS2M), "PlayerStatus", "Playing network session in CSII"));

            AddBinding(_NetworkStates = new ValueBinding<string>(nameof(CS2M), "uiNetworkStates", "= Waiting for commands ="));
        }

        private void RefreshModSupport()
        {
            _modSupportStatus.Update(ModCompat.GetModSupportList());
        }

        private void ShowUITraversal()
        {
            if (_gameMode == GameMode.MainMenu)
            {
                ShowJoinGameMenu();
                Debug.Print("I'm in game MainMenu. Opening Join UI");
            }
            else if (_gameMode == GameMode.Game)
            {
                ShowHostGameMenu();
                Debug.Print("I'm in active game session. Opening Host UI");
            }
        }

        public void piblishNetworkStateInUI(string newSate)
        {
            this._NetworkStates.Update(_NetworkStates.value +"\r\n"+ newSate);
        }

        private void ShowJoinGameMenu()
        {
            RefreshModSupport();
            if (_gameMode == GameMode.MainMenu)
            {
                this._activeMenuScreenBinding.Update(99);
                this._joinMenuVisible.Update(true);
            }
            else if (_gameMode == GameMode.Game)
            {
                this._activeGameScreenBinding.Update(99);
                this._joinMenuVisible.Update(true);
            }
        }

        private void ShowHostGameMenu()
        {
            if (_gameMode == GameMode.MainMenu)
            {
                //this._activeMenuScreenBinding.Update(99);
                this._hostMenuVisible.Update(true);
            }
            else if (_gameMode == GameMode.Game)
            {
                //this._activeGameScreenBinding.Update(99);
                this._hostMenuVisible.Update(true);
            }
        }

        private void HideJoinGameMenu()
        {

            if (_gameMode == GameMode.MainMenu)
            {
                this._activeMenuScreenBinding.Update(0);
                this._joinMenuVisible.Update(false);
            }
            else if (_gameMode == GameMode.Game)
            {
                this._activeGameScreenBinding.Update(10);
                this._joinMenuVisible.Update(false);
            }
        }

        private void HideHostGameMenu()
        {
            if (_gameMode == GameMode.MainMenu)
            {
                //this._activeMenuScreenBinding.Update(0);
                this._hostMenuVisible.Update(false);
            }
            else if (_gameMode == GameMode.Game)
            {
                //this._activeGameScreenBinding.Update(10);
                this._hostMenuVisible.Update(false);
            }
        }

        private void JoinGame()
        {
            NetworkInterface.Instance.Connect(new ConnectionConfig(_joinIPAddress.value, _joinPort.value, ""), piblishNetworkStateInUI);
            _hostGameEnabled.Update(false);
            _joinGameEnabled.Update(false);
        }

        private void HostGame()
        {
            NetworkInterface.Instance.StartServer(new ConnectionConfig(_joinPort.value), piblishNetworkStateInUI);
            _hostGameEnabled.Update(false);
            _joinGameEnabled.Update(false); 
        }

        public void SetGameState(PlayerStatus status)
        {
            _playerStatus.Update(status.ToString());
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _gameMode = mode;
        }
    }
}
