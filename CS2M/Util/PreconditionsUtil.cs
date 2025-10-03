using System;
using System.Collections.Generic;
using System.Linq;
using CS2M.Commands.Data.Internal;
using CS2M.Mods;

namespace CS2M.Util
{
    public static class PreconditionsUtil
    {
        public static Result CheckPreconditions(PreconditionsDataCommand remote)
        {
            Result result = new Result
            {
                DlcIds = remote.DlcIds,
                Mods = remote.Mods
            };

            // Check to see if the game versions match
            if (!VersionUtil.GetGameVersion().Equals(remote.GameVersion))
            {
                Log.Debug(
                    $"[Preconditions Check] Game versions don't match Local: {VersionUtil.GetGameVersion()} Remote: {remote.GameVersion}");
                result.Errors |= Errors.GAME_VERSION_MISMATCH;
            }

            // Check to see if the mod version matches
            if (!VersionUtil.GetModVersion().Equals(remote.ModVersion))
            {
                Log.Debug(
                    $"[Preconditions Check] Mod versions don't match Local: {VersionUtil.GetModVersion()} Remote: {remote.ModVersion}");
                result.Errors |= Errors.MOD_VERSION_MISMATCH;
            }

            // Check both clients have the same DLCs enabled
            List<int> localDlcs = DlcCompat.RequiredDLCsForSync;
            if (!localDlcs.All(remote.DlcIds.Contains) || localDlcs.Count != remote.DlcIds.Count)
            {
                Log.Debug("[Preconditions Check] DLCs don't match.");
                Log.Debug($"[Preconditions Check] Remote DLCs: {string.Join(", ", remote.DlcIds)}");
                Log.Debug($"[Preconditions Check] Local DLCs: {string.Join(", ", localDlcs)}");
                result.Errors |= Errors.DLCS_MISMATCH;
            }

            // Check both clients have the same Mods enabled
            List<string> localMods = ModSupport.Instance.RequiredModsForSync;
            if (!localMods.All(remote.Mods.Contains) || localMods.Count != remote.Mods.Count)
            {
                Log.Debug("[Preconditions Check] Mods don't match.");
                Log.Debug($"[Preconditions Check] Remote mods: {string.Join(", ", remote.Mods)}");
                Log.Debug($"[Preconditions Check] Local mods: {string.Join(", ", localMods)}");
                result.Errors |= Errors.MODS_MISMATCH;
            }

            return result;
        }

        public class Result
        {
            public Errors Errors { get; set; } = Errors.NONE;
            public List<int> DlcIds { get; set; } = new List<int>();
            public List<string> Mods { get; set; } = new List<string>();

            //TODO: Create methods to get differences for mods and DLCs
        }

        [Flags]
        public enum Errors
        {
            NONE = 0,
            GAME_VERSION_MISMATCH = 1,
            MOD_VERSION_MISMATCH = 2,
            USERNAME_NOT_AVAILABLE = 4,
            PASSWORD_INCORRECT = 8,
            DLCS_MISMATCH = 16,
            MODS_MISMATCH = 32,
        }
    }
}
