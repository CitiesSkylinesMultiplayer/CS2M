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
        private ValueBinding<GameScreenUISystem.GameScreen> _activeGameScreenBinding;
        private ValueBinding<int> _activeMenuScreenBinding;
        private ValueBinding<int> _downloadDone;
        private ValueBinding<int> _downloadRemaining;

        private GameMode _gameMode = GameMode.Other;
        private ValueBinding<bool> _hostGameEnabled;
        private ValueBinding<bool> _hostMenuVisible;
        private ValueBinding<int> _hostPort;
        private ValueBinding<bool> _joinGameEnabled;

        private ValueBinding<string> _joinIPAddress;
        private ValueBinding<bool> _joinMenuVisible;
        private ValueBinding<int> _joinPort;

        private ValueBinding<List<ModSupportStatus>> _modSupportStatus;
        private ValueBinding<string> _playerStatus;

        private ValueBinding<string> _username;

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
                username => { _username.Update(username); }));

            AddBinding(new TriggerBinding(Mod.Name, "JoinGame", JoinGame));
            AddBinding(new TriggerBinding(Mod.Name, "HostGame", HostGame));

            AddBinding(_joinMenuVisible = new ValueBinding<bool>(Mod.Name, "JoinMenuVisible", false));
            AddBinding(_hostMenuVisible = new ValueBinding<bool>(Mod.Name, "HostMenuVisible", false));
            AddBinding(_modSupportStatus = new ValueBinding<List<ModSupportStatus>>(Mod.Name, "modSupport",
                new List<ModSupportStatus>(), new ListWriter<ModSupportStatus>(new ValueWriter<ModSupportStatus>())));

            AddBinding(_joinIPAddress = new ValueBinding<string>(Mod.Name, "JoinIpAddress", ""));
            AddBinding(_joinPort = new ValueBinding<int>(Mod.Name, "JoinPort", 0));
            AddBinding(_hostPort = new ValueBinding<int>(Mod.Name, "HostPort", 0));
            AddBinding(_username = new ValueBinding<string>(Mod.Name, "Username", ""));
            AddBinding(_joinGameEnabled = new ValueBinding<bool>(Mod.Name, "JoinGameEnabled", true));
            AddBinding(_hostGameEnabled = new ValueBinding<bool>(Mod.Name, "HostGameEnabled", true));

            AddBinding(_playerStatus = new ValueBinding<string>(Mod.Name, "PlayerStatus", ""));
            AddBinding(_downloadDone = new ValueBinding<int>(Mod.Name, "DownloadDone", 0));
            AddBinding(_downloadRemaining = new ValueBinding<int>(Mod.Name, "DownloadRemaining", 0));

            RegisterChatPanelBindings();

            NetworkInterface.Instance.LocalPlayer.PlayerStatusChangedEvent += (old, status) =>
            {
                _playerStatus.Update(status.ToString());
            };
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
                _activeMenuScreenBinding.Update(99);
                _joinMenuVisible.Update(true);
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
            NetworkInterface.Instance.UpdateLocalPlayerUsername(_username.value);
            NetworkInterface.Instance.Connect(new ConnectionConfig(_joinIPAddress.value, _joinPort.value, ""));
            _joinGameEnabled.Update(false);
        }

        private void HostGame()
        {
            NetworkInterface.Instance.UpdateLocalPlayerUsername(_username.value);
            NetworkInterface.Instance.StartServer(new ConnectionConfig(_hostPort.value));
            _hostGameEnabled.Update(false);
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
            _downloadDone.Update(downloadDone);
            _downloadRemaining.Update(downloadRemaining);
        }
    }
}
