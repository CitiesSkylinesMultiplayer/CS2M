using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using CS2M.Mods;
using Game;
using Game.UI;

namespace CS2M.UI
{

    public partial class UISystem : UISystemBase
    {
        private ValueBinding<int> _activeScreenBinding;
        private ValueBinding<bool> _joinMenuVisible;

        private ValueBinding<string> _ipAddress;
        private ValueBinding<int> _port;
        private ValueBinding<string> _username;
        private ValueBinding<bool> _joinGameEnabled;

        private ValueBinding<List<ModSupportStatus>> _modSupportStatus;

        private GameMode _gameMode = GameMode.Other;

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            _activeScreenBinding = BindingsHelper.GetValueBinding<int>("menu", "activeScreen");
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            AddBinding(new TriggerBinding(nameof(CS2M), "ShowMultiplayerMenu", ShowMultiplayerMenu));
            AddBinding(new TriggerBinding(nameof(CS2M), "HideJoinGameMenu", HideJoinGameMenu));

            AddBinding(new TriggerBinding<string>(nameof(CS2M), "SetJoinIpAddress", ip =>
            {
                _ipAddress.Update(ip);
            }));
            AddBinding(new TriggerBinding<int>(nameof(CS2M), "SetJoinPort", port =>
            {
                _port.Update(port);
            }));
            AddBinding(new TriggerBinding<string>(nameof(CS2M), "SetJoinUsername", username =>
            {
                _username.Update(username);
            }));

            AddBinding(new TriggerBinding(nameof(CS2M), "JoinGame", JoinGame));

            AddBinding(_joinMenuVisible = new ValueBinding<bool>(nameof(CS2M), "JoinMenuVisible", false));
            AddBinding(_modSupportStatus = new ValueBinding<List<ModSupportStatus>>(nameof(CS2M), "modSupport", new List<ModSupportStatus>(),new ListWriter<ModSupportStatus>(new ValueWriter<ModSupportStatus>())));

            AddBinding(_ipAddress = new ValueBinding<string>(nameof(CS2M), "JoinIpAddress", ""));
            AddBinding(_port = new ValueBinding<int>(nameof(CS2M), "JoinPort", 0));
            AddBinding(_username = new ValueBinding<string>(nameof(CS2M), "JoinUsername", ""));
            AddBinding(_joinGameEnabled = new ValueBinding<bool>(nameof(CS2M), "JoinGameEnabled", true));
        }

        private void RefreshModSupport()
        {
            _modSupportStatus.Update(ModCompat.GetModSupportList());
        }

        private void ShowMultiplayerMenu()
        {
            RefreshModSupport();
            if (_gameMode == GameMode.MainMenu)
            {
                this._activeScreenBinding.Update(99);
                this._joinMenuVisible.Update(true);
            }
            else if (_gameMode == GameMode.Game)
            {
                // TODO: Show start server/multiplayer info menu
            }
        }

        private void HideJoinGameMenu()
        {
            Log.Info("HideJoinGameMenu");
            this._activeScreenBinding.Update(0);
            this._joinMenuVisible.Update(false);
        }

        private void JoinGame()
        {
            //MultiplayerManager.JoinGame(_ipAddress.value, _port.value, _username.value)
            _joinGameEnabled.Update(false);
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
