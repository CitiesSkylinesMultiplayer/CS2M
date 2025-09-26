using CS2M.API.Commands;

namespace CS2M.Commands
{
    public class WorldTransferCommand : CommandBase
    {
        public byte[] WorldSlice { get; set; }

        public int RemainingBytes { get; set; }

        public bool NewTransfer { get; set; }
    }
}
