using Colossal.Logging;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using CS2M;
using CS2M.API;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.UI;
using Game;
using Game.UI;
using Game.UI.InGame;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;
using Random = System.Random;

namespace CS2M.UI
{

    public partial class UISystem : UISystemBase
    {

        public static UISystem Instance { get; private set; }


        private ValueBinding<int> _activeMenuScreenBinding;
        private ValueBinding<int> _activeGameScreenBinding;
        private ValueBinding<bool> _joinMenuVisible;
        private ValueBinding<bool> _hostMenuVisible;

        public ValueBinding<string> _NetworkStates;
        private ValueBinding<string> _usrMsg;

        private ValueBinding<string> _joinIPAddress;
        private ValueBinding<int> _joinPort;
        private ValueBinding<int> _hostPort;
        private ValueBinding<string> _username;
        private ValueBinding<bool> _joinGameEnabled;
        private ValueBinding<bool> _hostGameEnabled;
        private ValueBinding<bool> _chatSendEnabled;
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
            Instance = this;

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

            AddBinding(new TriggerBinding<string>(nameof(CS2M), "SetMsgFromUsr", usrMsg =>
            {
                _usrMsg.Update(usrMsg);
            }));

            AddBinding(_usrMsg = new ValueBinding<string>(nameof(CS2M), "playerMessage", ""));



            AddBinding(new TriggerBinding(nameof(CS2M), "JoinGame", JoinGame));
            AddBinding(new TriggerBinding(nameof(CS2M), "HostGame", HostGame));

            AddBinding(_joinMenuVisible = new ValueBinding<bool>(nameof(CS2M), "JoinMenuVisible", false));
            AddBinding(_hostMenuVisible = new ValueBinding<bool>(nameof(CS2M), "HostMenuVisible", false));
            AddBinding(_modSupportStatus = new ValueBinding<List<ModSupportStatus>>(nameof(CS2M), "modSupport", new List<ModSupportStatus>(), new ListWriter<ModSupportStatus>(new ValueWriter<ModSupportStatus>())));

            AddBinding(_joinIPAddress = new ValueBinding<string>(nameof(CS2M), "JoinIpAddress", "127.0.0.1"));
            AddBinding(_joinPort = new ValueBinding<int>(nameof(CS2M), "JoinPort", 4230));
            AddBinding(_hostPort = new ValueBinding<int>(nameof(CS2M), "HostPort", 4230));
            Random rnd = new Random();
            AddBinding(_username = new ValueBinding<string>(nameof(CS2M), "Username", "CS2M_u" + rnd.Next(100, 1000)));

            AddBinding(_joinGameEnabled = new ValueBinding<bool>(nameof(CS2M), "JoinGameEnabled", true));
            AddBinding(_hostGameEnabled = new ValueBinding<bool>(nameof(CS2M), "HostGameEnabled", true));
            AddBinding(_chatSendEnabled = new ValueBinding<bool>(nameof(CS2M), "ChatSendEnabled", false));

            AddBinding(_playerStatus = new ValueBinding<string>(nameof(CS2M), "PlayerStatus", "Playing network session in CSII"));

            AddBinding(_NetworkStates = new ValueBinding<string>(nameof(CS2M), "uiNetworkStates", "= Waiting for commands ="));
            AddBinding(new TriggerBinding(nameof(CS2M), "SendMessage", sendMessage));

            Command.ConnectToCSM(
                sendToAll: NetworkInterface.Instance.SendToAll,
                sendToServer: NetworkInterface.Instance.SendToServer,
                sendToClients: NetworkInterface.Instance.SendToClients,
                getCommandHandler: type =>
                {
                    if (type == typeof(PlayerJoinedCommand))
                    {
                        return new PlayerJoinedHandler();
                    } 
                    if (type == typeof(textMessageCommand))
                    {
                        return new textMessageHandler();
                    }
                    return null;                       
                        
                }
            );

            
        }

        private void RefreshModSupport()
        {
            _modSupportStatus.Update(ModCompat.GetModSupportList());
        }

        private void sendMessage()
        {
            string fullMessage = _username.value + ": " + _usrMsg.value;
            Log.Info(fullMessage);
            Command.SendToAll(new textMessageCommand(fullMessage));
            //piblishNetworkStateInUI(fullMessage);
            //NetworkInterface.Instance.SendToAll()
        }


        private void ShowUITraversal()
        {
            if (_gameMode == GameMode.MainMenu)
            {
                ShowJoinGameMenu();
                Log.Info("I'm in game MainMenu. Opening Join UI");
            }
            else if (_gameMode == GameMode.Game)
            {
                ShowHostGameMenu();
                Log.Info("I'm in active game session. Opening Host UI");
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
            NetworkInterface.Instance.LocalPlayer.Username = _username.value;
            NetworkInterface.Instance.Connect(new ConnectionConfig(_joinIPAddress.value, _joinPort.value, ""));
            _hostGameEnabled.Update(false);
            _joinGameEnabled.Update(false);
            _chatSendEnabled.Update(true);
        }

        private void HostGame()
        {
            NetworkInterface.Instance.LocalPlayer.Username = _username.value;
            NetworkInterface.Instance.StartServer(new ConnectionConfig(_joinPort.value));
            _hostGameEnabled.Update(false);
            _joinGameEnabled.Update(false);
            _chatSendEnabled.Update(true);
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



public class PlayerJoinedCommand : CommandBase
{
    public string PlayerName { get; set; }

    public PlayerJoinedCommand() { }

    public PlayerJoinedCommand(int senderId, string playerName)
    {
        SenderId = senderId;
        PlayerName = playerName;
        MesasgeBody = $"{playerName} has joined the server";
    }

}

public class PlayerJoinedHandler : CommandHandler<PlayerJoinedCommand>
{
    protected override void Handle(PlayerJoinedCommand command)
    {

        Log.Info(command.MesasgeBody);
        UISystem.Instance?.piblishNetworkStateInUI(command.MesasgeBody);

    }
}

public class textMessageCommand : CommandBase
{
    public string Text { get; set; }
    public textMessageCommand() { }

    public textMessageCommand(string text)
    {
        Text = text;
    }
}

public class textMessageHandler : CommandHandler<textMessageCommand>
{
    protected override void Handle(textMessageCommand command)
    {
        Log.Info($"User message: {command.Text}");
        UISystem.Instance?.piblishNetworkStateInUI(command.Text);
    }
}