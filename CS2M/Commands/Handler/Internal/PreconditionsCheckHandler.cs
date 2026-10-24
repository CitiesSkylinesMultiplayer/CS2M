using System.Collections.Generic;
using System.Linq;
using CS2M.API.Commands;
using CS2M.Commands.Data.Internal;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.Util;
using LiteNetLib;

namespace CS2M.Commands.Handler.Internal
{
    public class PreconditionsCheckHandler : CommandHandler<PreconditionsCheckCommand>
    {
        // The lists in a check are compared against ours; anything longer than this isn't a real client
        private const int MaxListEntries = 1024;

        public PreconditionsCheckHandler()
        {
            TransactionCmd = false;
            RelayOnServer = false;
        }

        protected override void Handle(PreconditionsCheckCommand command)
        {
        }

        /// <summary>
        ///     Host: a connection asks to join. Only called for a connection that hasn't passed yet
        ///     (NetworkManager drops it otherwise), so the city is sent at most once per join.
        /// </summary>
        public void HandleOnServer(PreconditionsCheckCommand command, NetPeer peer, PeerState state, JoinGuard guard)
        {
            if (state.Registered)
            {
                return;
            }

            Log.Debug("Received Preconditions Check from a joining peer");

            command.Mods = (command.Mods ?? new List<string>()).Take(MaxListEntries).ToList();
            command.DlcIds = (command.DlcIds ?? new List<int>()).Take(MaxListEntries).ToList();
            PreconditionsUtil.Result result = PreconditionsUtil.CheckPreconditions(command);

            // A usable name nobody in the session has (ignoring case); never the mod's own name
            string username = TextRules.ValidUsername(command.Username);
            if (username == null)
            {
                Log.Debug("[Preconditions Check] The username is empty, too long or not allowed.");
                result.Errors |= PreconditionsUtil.Errors.USERNAME_NOT_AVAILABLE;
            }
            else if (NetworkInterface.Instance.IsUsernameTaken(username))
            {
                Log.Debug($"[Preconditions Check] Username '{username}' is already connected.");
                result.Errors |= PreconditionsUtil.Errors.USERNAME_NOT_AVAILABLE;
            }

            // Player limit (the host counts as one): the joiner is told the session is full
            int maxPlayers = CS2M.Networking.ApiServer.GetMaxPlayers();
            if (maxPlayers > 0 && NetworkInterface.Instance.ConnectedPlayerCount >= maxPlayers)
            {
                Log.Info($"[Preconditions Check] A joiner was turned away: the session is full ({maxPlayers} players).");
                result.Errors |= PreconditionsUtil.Errors.SERVER_FULL;
            }

            // Check the password (only if the server has one). The attempt itself is never logged.
            string password = NetworkInterface.Instance.LocalPlayer.GetConnectionPassword();
            if (!string.IsNullOrEmpty(password) && !TextRules.PasswordMatches(password, command.Password))
            {
                Log.Debug("[Preconditions Check] Password check failed.");
                result.Errors |= PreconditionsUtil.Errors.PASSWORD_INCORRECT;
            }

            if (result.Errors == PreconditionsUtil.Errors.NONE)
            {
                guard.RecordSuccess(state.Address);
                NetworkInterface.Instance.LocalPlayer.SendToClient(peer, new PreconditionsSuccessCommand());

                // Add the new player as a connected player (sends the city)
                NetworkInterface.Instance.RegisterPlayer(peer, state, username);
                return;
            }

            state.FailedChecks++;
            bool blocked = guard.RecordFailure(state.Address);
            NetworkInterface.Instance.LocalPlayer.SendToClient(peer, new PreconditionsErrorCommand()
            {
                Errors = result.Errors,
                ModVersion = VersionUtil.GetModVersion(),
                GameVersion = VersionUtil.GetGameVersion(),
                Mods = ModSupport.Instance.RequiredModsForSync,
                DlcIds = DlcCompat.RequiredDLCsForSync,
            });

            if (blocked || state.FailedChecks >= PeerState.MaxFailedChecks)
            {
                NetworkManager.Disconnect(peer, "Too many failed join attempts");
            }
        }
    }
}
