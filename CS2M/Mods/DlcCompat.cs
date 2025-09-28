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

        public static IEnumerable<ModSupportStatus> GetDlcSupport()
        {
            foreach (IDlc dlc in PlatformManager.instance.EnumerateDLCs())
            {
                if (PlatformManager.instance.IsDlcOwned(dlc))
                {
                    string name = dlc.backendName ?? dlc.internalName;
                    yield return new ModSupportStatus("DLC: " + name, dlc.id, ModSupportType.Supported,
                        ClientSideDlcs.Contains(dlc.internalName));
                }
            }
        }
    }
}
