using System.Collections.Generic;
using CS2M.API.Commands;

namespace CS2M.Commands.Data.Internal
{
    public abstract class PreconditionsDataCommand : CommandBase
    {
        /// <summary>
        ///     What version of the mod the sender has installed.
        /// </summary>
        public System.Version ModVersion { get; set; }

        /// <summary>
        ///     What version of the game the sender is running.
        /// </summary>
        public Colossal.Version GameVersion { get; set; }

        /// <summary>
        ///     A list of mods with multiplayer support that were loaded.
        /// </summary>
        public List<string> Mods { get; set; }

        /// <summary>
        ///     A list of DLCs that were loaded.
        /// </summary>
        public List<int> DlcIds { get; set; }
    }
}
