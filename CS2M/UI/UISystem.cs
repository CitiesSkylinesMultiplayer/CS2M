using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using CS2M.Mods;
using CS2M.Networking;
using Game;
using Game.UI;
using Game.UI.InGame;

namespace CS2M.UI
{

    public partial class UISystem : UISystemBase
    {
        private ValueBinding<int> _activeMenuScreenBinding;
        private ValueBinding<int> _activeGameScreenBinding;
        private ValueBinding<bool> _joinMenuVisible;
        private ValueBinding<bool> _hostMenuVisible;

        private ValueBinding<string> _joinIPAddress;
        private ValueBinding<int> _joinPort;
        private ValueBinding<int> _hostPort;
        private ValueBinding<string> _username;
        private ValueBinding<bool> _joinGameEnabled;
        private ValueBinding<bool> _hostGameEnabled;

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
            AddBinding(new TriggerBinding(nameof(CS2M), "ShowMultiplayerMenu", ShowJoinGameMenu));
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

            AddBinding(_joinIPAddress = new ValueBinding<string>(nameof(CS2M), "JoinIpAddress", ""));
            AddBinding(_joinPort = new ValueBinding<int>(nameof(CS2M), "JoinPort", 0));
            AddBinding(_hostPort = new ValueBinding<int>(nameof(CS2M), "HostPort", 0));
            AddBinding(_username = new ValueBinding<string>(nameof(CS2M), "Username", ""));
            AddBinding(_joinGameEnabled = new ValueBinding<bool>(nameof(CS2M), "JoinGameEnabled", true));
            AddBinding(_hostGameEnabled = new ValueBinding<bool>(nameof(CS2M), "HostGameEnabled", true));

        }

        private void RefreshModSupport()
        {
            _modSupportStatus.Update(ModCompat.GetModSupportList());
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
            RefreshModSupport();
            if (_gameMode == GameMode.MainMenu)
            {
                this._activeMenuScreenBinding.Update(99);
                this._hostMenuVisible.Update(true);
            }
            else if (_gameMode == GameMode.Game)
            {
                this._activeGameScreenBinding.Update(99);
                this._hostMenuVisible.Update(true);
            }
        }

        private void HideJoinGameMenu()
        {
            this._activeMenuScreenBinding.Update(0);
            this._activeGameScreenBinding.Update(10);
            this._joinMenuVisible.Update(false);
        }

        private void HideHostGameMenu()
        {
            this._activeMenuScreenBinding.Update(0);
            this._activeGameScreenBinding.Update(10);
            this._hostMenuVisible.Update(false);
        }

        private void JoinGame()
        {
            NetworkInterface.Instance.Connect(new ConnectionConfig(_joinIPAddress.value, _joinPort.value, ""));
            _joinGameEnabled.Update(false);
        }

        private void HostGame()
        {
            NetworkInterface.Instance.StartServer(new ConnectionConfig(_joinPort.value));
            _hostGameEnabled.Update(false);
        }

        public void SetGameState(/*PlayerStatus status*/)
        {

        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            _gameMode = mode;
        }
    }
}
