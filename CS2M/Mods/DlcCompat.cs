using System.Collections.Generic;
using System.Linq;
using Colossal.PSI.Common;

namespace CS2M.Mods
{
    public static class DlcCompat
    {
        private static readonly string[] ClientSideDlcs =
            { "AtmosphericPianoRadio", "DeluxeRelaxRadio", "FeelgoodFunkRadio", "JadeRoadRadio" };

        public static List<int> RequiredDLCsForSync =>
            GetDlcSupport().Where(dlc => !dlc.ClientSide).Select(dlc => dlc.DlcId.id).ToList();

        private static string GetName(IDlc dlc)
        {
            return (dlc.backendName ?? dlc.internalName).Replace("Cities: Skylines II - ", "");
        }

        public static IEnumerable<ModSupportStatus> GetDlcSupport()
        {
            foreach (IDlc dlc in PlatformManager.instance.EnumerateDLCs())
            {
                if (PlatformManager.instance.IsDlcOwned(dlc))
                {
                    yield return new ModSupportStatus("DLC: " + GetName(dlc), dlc.id, ModSupportType.Supported,
                        ClientSideDlcs.Contains(dlc.internalName));
                }
            }
        }

        public static string GetDisplayName(DlcId id)
        {
            foreach (IDlc dlc in PlatformManager.instance.EnumerateDLCs())
            {
                if (dlc.id == id)
                {
                    return GetName(dlc);
                }
            }

            return PlatformManager.instance.GetDlcName(id);
        }
    }
}
