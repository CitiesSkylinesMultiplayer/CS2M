using Colossal.UI.Binding;
using CS2M.API;
using CS2M.Networking;
using Game.UI.InGame;

namespace CS2M.UI
{
    /// <summary>
    /// Displays a chat to the users screen in a pop up bubble. 
    /// Allows a user to send messages to other players and view 
    /// events such as server startup and player connections.
    /// </summary>
    public class ChatPanel : EntityGamePanel, IChat
    {
        public class Message
        {
            public string User { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
        }

        public ValueBinding<string[]> ChatMessages { get; }
        public ValueBinding<string> LocalChatMessage { get; }
        public TriggerBinding<string> SetLocalChatMessage { get; }
        public EventBinding<string> ReceivedChatMessage { get; }

        public override LayoutPosition position => LayoutPosition.Right;

        public ChatPanel()
        {
            Chat.Instance = this;

            ChatMessages = new ValueBinding<string[]>(Mod.Name, nameof(ChatMessages), new string[] { });
            LocalChatMessage = new ValueBinding<string>(Mod.Name, nameof(LocalChatMessage), string.Empty);
            SetLocalChatMessage = new TriggerBinding<string>(Mod.Name, nameof(SetLocalChatMessage), message => {
                LocalChatMessage.Update(message);
            });
            ReceivedChatMessage = new EventBinding<string>(Mod.Name, nameof(ReceivedChatMessage));

            WelcomeChatMessage();
        }

        private void PrintMessage(string sender, string msg)
        {
            Log.Debug($"ChatPanel_PrintMessage: {sender} - {msg}");
            ReceivedChatMessage.Trigger(msg);
        }

        /// <summary>
        /// Prints a game message to the ChatPanel with MessageType.NORMAL.
        /// </summary>
        /// <param name="msg">The message.</param>
        public void PrintGameMessage(string msg)
        {
            PrintGameMessage(Chat.MessageType.Normal, msg);
        }

        /// <summary>
        /// Prints a game message to the ChatPanel
        /// </summary>
        /// <param name="type">The message type.</param>
        /// <param name="msg">The message.</param>
        public void PrintGameMessage(Chat.MessageType type, string msg)
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
            return NetworkInterface.Instance.LocalPlayer.Username;
        }

        public void WelcomeChatMessage()
        {
            PrintGameMessage("Welcome to Cities: Skylines 2 Multiplayer!");
        }
    }
}
