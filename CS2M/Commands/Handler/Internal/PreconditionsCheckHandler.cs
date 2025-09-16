using System.Collections.Generic;
using System.Linq;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Commands.Data.Internal;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.Util;
using LiteNetLib;

namespace CS2M.Commands.Handler.Internal
{
    public class PreconditionsCheckHandler : CommandHandler<PreconditionsCheckCommand>
    {
        public PreconditionsCheckHandler()
        {
            TransactionCmd = false;
        }

        protected override void Handle(PreconditionsCheckCommand command)
        {
        }

        public void HandleOnServer(PreconditionsCheckCommand command, NetPeer peer)
        {
            Log.Debug($"Received Preconditions Check [PeerId: {peer.Id}]");

            // Check to see if the game versions match
            if (!VersionUtil.GetGameVersion().Equals(command.GameVersion))
            {
                Log.Debug($"Preconditions Check [PeerId: {peer.Id}] game versions don't match LOCAL: {VersionUtil.GetGameVersion()} REMOTE: {command.GameVersion}");
                //TODO: Send result
                return;
            }

            // Check to see if the mod version matches
            if (!VersionUtil.GetModVersion().Equals(command.ModVersion))
            {
                Log.Debug($"Preconditions Check [PeerId: {peer.Id}] mod versions don't match LOCAL: {VersionUtil.GetModVersion()} REMOTE: {command.ModVersion}");
                //TODO: Send result
                return;
            }

            // Check the client username to see if anyone on the server already have a username
            if (NetworkInterface.Instance.PlayerListConnected.Any(p => p.Username.Equals(command.Username)))
            {
                Log.Debug($"Preconditions Check [PeerId: {peer.Id}] username '{command.Username}' is already connected.");
                //TODO: Send result
                return;
            }

            // Check the password to see if it matches (only if the server has provided a password).
            if (!string.IsNullOrEmpty(NetworkInterface.Instance.LocalPlayer.GetConnectionPassword()))
            {
                if (!NetworkInterface.Instance.LocalPlayer.GetConnectionPassword().Equals(command.Password))
                {
                    Log.Debug($"Preconditions Check [PeerId: {peer.Id}] password '{command.Password}' is not correct.");
                    //TODO: Send result
                    return;
                }
            }

            // Check both clients have the same DLCs enabled
            if (!new List<int>().All(command.DlcIds.Contains))
            {
                Log.Debug($"Preconditions Check [PeerId: {peer.Id}] dlcs don't match.");
                Log.Debug($"Preconditions Check [PeerId: {peer.Id}] client dlcs: {string.Join(", ", command.DlcIds)}");
                //Log.Debug($"Preconditions Check [Peer: {peer.Id}] server dlcs: {string.Join(", ", )}");
                //TODO: Send result
                return;
            }

            // Check both clients have the same Mods enabled
            if (!ModSupport.Instance.RequiredModsForSync.All(command.Mods.Contains))
            {
                Log.Debug($"Preconditions Check [PeerId: {peer.Id}] mods don't match.");
                Log.Debug($"Preconditions Check [PeerId: {peer.Id}] client mods: {string.Join(", ", command.Mods)}");
                Log.Debug($"Preconditions Check [PeerId: {peer.Id}] server mods: {string.Join(", ", ModSupport.Instance.RequiredModsForSync)}");
                //TODO: Send result
                return;
            }

            // Add the new player as a connected player
            NetworkInterface.Instance.PlayerConnected(new RemotePlayer(peer, command.Username, PlayerType.CLIENT));
        }
    }
}