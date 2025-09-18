using CS2M.API.Commands;
using CS2M.Commands.Data.Internal;
using CS2M.Networking;

namespace CS2M.Commands.Handler.Internal
{
    public class PreconditionsSuccessHandler : CommandHandler<PreconditionsSuccessCommand>
    {
        public PreconditionsSuccessHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(PreconditionsSuccessCommand command)
        {
            NetworkInterface.Instance.LocalPlayer.WaitingToJoin();
        }
    }
}
