using CS2M.API;
using CS2M.API.Commands;
using CS2M.Commands.Data.Internal;
using CS2M.Networking;

namespace CS2M.Commands.Handler.Internal
{
    public class ChatMessageHandler : CommandHandler<ChatMessageCommand>
    {
        public ChatMessageHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(ChatMessageCommand command)
        {
            // The host has already put the sender's registered name on it and cleaned the text; clean it
            // again here so nothing odd reaches the chat panel whatever the host runs
            string message = TextRules.CleanChat(command.Message);
            if (message.Length == 0)
            {
                return;
            }

            string username = TextRules.CleanChat(command.Username, TextRules.MaxUsernameLength);
            if (username.Length == 0)
            {
                username = $"Player {command.SenderId}";
            }
            else if (TextRules.IsReserved(username))
            {
                // Only the mod's own messages show as the mod
                username += " (player)";
            }

            Chat.Instance.PrintChatMessage(username, message);
        }
    }
}
