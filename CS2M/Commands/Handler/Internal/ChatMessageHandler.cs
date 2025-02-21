using CS2M.API;
using CS2M.API.Commands;
using CS2M.Commands.Data.Internal;

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
            Chat.Instance.PrintChatMessage(command.Username, command.Message);
        }
    }
}
