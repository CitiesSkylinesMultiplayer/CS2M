using CS2M.API.Commands;
using CS2M.Commands.Data.Internal;
using CS2M.Networking;

namespace CS2M.Commands.Handler.Internal
{
    public class PreconditionsErrorHandler : CommandHandler<PreconditionsErrorCommand>
    {
        public PreconditionsErrorHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(PreconditionsErrorCommand command)
        {
            Log.Debug($"[Preconditions Error] {command.Errors.ToString()}");
            
            NetworkInterface.Instance.LocalPlayer.Inactive();
        }
    }
}
