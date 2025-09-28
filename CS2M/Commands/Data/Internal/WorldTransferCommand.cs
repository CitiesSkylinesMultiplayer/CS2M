using CS2M.API.Commands;

namespace CS2M.Commands.Data.Internal
{
    /// <summary>
    ///     World transfer command contains slices of the save game transferred from server to client
    /// </summary>
    public class WorldTransferCommand : CommandBase
    {
        /// <summary>
        ///     The data of the save game slice in this packet.
        /// </summary>
        public byte[] WorldSlice { get; set; }

        /// <summary>
        ///     Remaining bytes to be transferred after this packet.
        /// </summary>
        public int RemainingBytes { get; set; }

        /// <summary>
        ///     If this is the first packet of a transfer.
        /// </summary>
        public bool NewTransfer { get; set; }
    }
}
