using CS2M.API.Commands.Data.Internal;

namespace CS2M.API.Commands.Handler.Internal
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
