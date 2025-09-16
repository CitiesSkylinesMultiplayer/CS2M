using System.Collections.Generic;
using CS2M.API.Commands;

namespace CS2M.Commands.Data.Internal
{
    public class PreconditionsCheckCommand : CommandBase
    {
        /// <summary>
        ///     The username this user will be playing as, important
        ///     as the server will keep track of this user.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        ///     An optional password if the server is set up to
        ///     require a password.
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        ///     What version of the mod this user has installed. The server needs to check
        ///     that both version match (for obvious reasons, as major updates may have happened).
        /// </summary>
        public System.Version ModVersion { get; set; }

        /// <summary>
        ///     What version of the game is the user running. There might be issues between games,
        ///     so we need to check that these match.
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