using Colossal.UI.Binding;
using CS2M.API;
using CS2M.API.Commands.Data.Internal;
using CS2M.Commands;
using CS2M.Networking;
using Game.UI.InGame;
using System;
using System.Collections.Generic;

namespace CS2M.UI
{
    /// <summary>
    /// Displays a chat to the users screen in a pop up bubble. 
    /// Allows a user to send messages to other players and view 
    /// events such as server startup and player connections.
    /// </summary>
    public class ChatPanel : EntityGamePanel, IChat
    {
        public readonly struct Message : IJsonWritable
        {
            public string Timestamp { get; }
            public string User { get; }
            public string Text { get; }

            public Message(string timestamp, string user, string text)
            {
                Timestamp = timestamp;
                User = user;
                Text = text;
            }

            public void Write(IJsonWriter writer)
            {
                writer.TypeBegin(this.GetType().FullName);
                writer.PropertyName("timestamp");
                writer.Write(this.Timestamp);
                writer.PropertyName("user");
                writer.Write(this.User);
                writer.PropertyName("text");
                writer.Write(this.Text);
                writer.TypeEnd();
            }
        }

        public ValueBinding<List<Message>> ChatMessages { get; }
        public ValueBinding<string> CurrentUsername { get; }
        public ValueBinding<string> LocalChatMessage { get; }
        public TriggerBinding SendChatMessage { get; }
        public TriggerBinding<string> SetLocalChatMessage { get; }

        public override LayoutPosition position => LayoutPosition.Right;

        public ChatPanel()
        {
            Chat.Instance = this;

            ChatMessages = new ValueBinding<List<Message>>(Mod.Name, nameof(ChatMessages), new List<Message>(), new ListWriter<Message>(new ValueWriter<Message>()));
            CurrentUsername = new ValueBinding<string>(Mod.Name, nameof(CurrentUsername), GetCurrentUsername());
            LocalChatMessage = new ValueBinding<string>(Mod.Name, nameof(LocalChatMessage), string.Empty);
            SendChatMessage = new TriggerBinding(Mod.Name, nameof(SendChatMessage), () => {
                ChatMessageCommand message = new ChatMessageCommand() {
                    Username = GetCurrentUsername(),
                    Message = LocalChatMessage.value
                };
                CommandInternal.Instance.SendToAll(message);

                CurrentUsername.Update(GetCurrentUsername());
                LocalChatMessage.Update(string.Empty);
            });
            SetLocalChatMessage = new TriggerBinding<string>(Mod.Name, nameof(SetLocalChatMessage), message => {
                LocalChatMessage.Update(message);
            });
        }

        private void PrintMessage(string sender, string msg)
        {
            Log.Debug($"ChatPanel_PrintMessage: {sender} - {msg}");
            ChatMessages.value.Add(new Message(DateTime.Now.ToShortTimeString(), sender, msg));
            ChatMessages.TriggerUpdate();
        }

        /// <summary>
        /// Prints a game message to the ChatPanel
        /// </summary>
        /// <param name="msg">The message.</param>
        public void PrintGameMessage(string msg)
        {
            PrintMessage(Mod.Name, msg);
        }

        /// <summary>
        /// Prints a chat message to the ChatPanel
        /// </summary>
        /// <param name="username">The name of the sending user.</param>
        /// <param name="msg">The message.</param>
        public void PrintChatMessage(string username, string msg)
        {
            PrintMessage(username, msg);
        }

        /// <summary>
        /// Fetches the username for the current player and returns it as a string.
        /// </summary>
        /// <returns>The username of the current player</returns>
        public string GetCurrentUsername()
        {
            return NetworkInterface.Instance.LocalPlayer.Username ?? string.Empty;
        }

        public void WelcomeChatMessage()
        {
            PrintGameMessage("Welcome to Cities: Skylines 2 Multiplayer!");
        }
    }
}
