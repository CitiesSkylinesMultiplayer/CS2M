using System.Collections.Generic;
using System.Linq;
using Game.Modding;
using Game.SceneFlow;

namespace CS2M.Mods
{
    public enum ModSupportType
    {
        Unknown,
        Unsupported,
        Supported,
        KnownWorking,
    }

    public struct ModSupportStatus
    {
        public ModSupportStatus(string name, string typeName, ModSupportType type, bool clientSide)
        {
            Name = name;
            TypeName = typeName;
            Type = type;
            ClientSide = clientSide;
        }

        public string Name { get; }
        public string TypeName { get; }
        public ModSupportType Type { get; }
        public bool ClientSide { get; }
    }

    public static class ModCompat
    {
        private static readonly string[] ClientSideMods = {  };
        private static readonly string[] IgnoredMods = { "CitiesHarmony.Mod" };

        private static readonly string[] KnownToWork = {  };
        private static readonly string[] UnsupportedMods = {  };

        private static IEnumerable<ModSupportStatus> GetModSupport()
        {
            /*foreach (SteamHelper.DLC dlc in DLCHelper.GetOwnedExpansions().DLCs())
            {
                string name = DLCHelper.GetDlcName(dlc);
                yield return new ModSupportStatus("DLC: " + name, name, DLCHelper.GetSupport(dlc), false);
            }
            foreach (SteamHelper.DLC dlc in DLCHelper.GetOwnedModderPacks().DLCs())
            {
                string name = DLCHelper.GetDlcName(dlc);
                yield return new ModSupportStatus("DLC: " + name, name, DLCHelper.GetSupport(dlc), false);
            }*/

            foreach (ModManager.ModInfo info in GameManager.instance.modManager)
            {
                // Skip disabled mods
                if (!info.isLoaded)
                    continue;
                
                Log.Debug(info.name);
                Log.Debug(info.assemblyFullName);

                foreach (IMod modInstance in info.instances)
                {
                    // Skip CSM itself
                    if (modInstance?.GetType() == typeof(CS2M))
                        continue;

                    string modInstanceName = modInstance?.GetType().ToString();

                    // Skip ignored mods
                    if (IgnoredMods.Contains(modInstanceName))
                        continue;

                    bool isClientSide = ClientSideMods.Contains(modInstanceName);

                    // Mods known to work
                    if (KnownToWork.Contains(modInstanceName))
                    {
                        yield return new ModSupportStatus(info.name, modInstanceName, ModSupportType.KnownWorking, isClientSide);
                        continue;
                    }

                    // Mods with loaded multiplayer support
                    if (ModSupport.Instance.ConnectedMods.Select(mod => mod.ModClass)
                        .Contains(modInstance?.GetType()))
                    {
                        yield return new ModSupportStatus(info.name, modInstanceName, ModSupportType.Supported, isClientSide);
                        continue;
                    }

                    // Decide between unsupported and unknown
                    if (UnsupportedMods.Contains(modInstanceName))
                    {
                        yield return new ModSupportStatus(info.name, modInstanceName, ModSupportType.Unsupported, isClientSide);
                        continue;
                    }

                    yield return new ModSupportStatus(info.name, modInstanceName, ModSupportType.Unknown, isClientSide);
                }
            }
        }

        /*public static void BuildModInfo(UIPanel panel)
        {
            UIScrollablePanel modInfoPanel = panel.Find<UIScrollablePanel>("modInfoPanel");
            if (modInfoPanel != null)
            {
                modInfoPanel.Remove();
            }

            IEnumerable<ModSupportStatus> modSupport = GetModSupport().ToList();
            if (!modSupport.Any())
            {
                panel.width = 360;
                return;
            }

            modInfoPanel = panel.AddUIComponent<UIScrollablePanel>();
            modInfoPanel.name = "modInfoPanel";
            modInfoPanel.width = 340;
            modInfoPanel.height = 500;
            modInfoPanel.clipChildren = true;
            modInfoPanel.position = new Vector2(370, -60);

            panel.AddScrollbar(modInfoPanel);

            panel.width = 720;
            modInfoPanel.CreateLabel("Mod/DLC Support", new Vector2(0, 0), 340, 20);

            Log.Debug($"Mod support: {string.Join(", ", modSupport.Select(m => $"{m.TypeName} ({m.Type})").ToArray())}");
            int y = -50;
            foreach (ModSupportStatus mod in modSupport)
            {
                string modName = mod.Name.Length > 30 ? mod.Name.Substring(0, 30) + "..." : mod.Name;
                UILabel nameLabel = modInfoPanel.CreateLabel($"{modName}", new Vector2(10, y), 340, 20);
                nameLabel.textScale = 0.9f;

                string message;
                Color32 labelColor;
                switch (mod.Type)
                {
                    case ModSupportType.Supported:
                        message = "Supported";
                        labelColor = new Color32(0, 255, 0, 255);
                        break;
                    case ModSupportType.Unsupported:
                        message = "Unsupported";
                        labelColor = new Color32(170, 0, 0, 255);
                        break;
                    case ModSupportType.KnownWorking:
                        message = "Known to work";
                        labelColor = new Color32(160, 255, 0, 255);
                        break;
                    case ModSupportType.Unknown:
                        message = "Unknown";
                        labelColor = new Color32(255, 100, 0, 255);
                        break;
                    default:
                        continue;
                }

                if (mod.ClientSide)
                {
                    message += " (Client side mod)";
                }

                UILabel label = modInfoPanel.CreateLabel(message, new Vector2(10, y - 20), 340, 20);
                label.textColor = labelColor;
                label.textScale = 0.9f;
                y -= 50;
            }
        }*/
    }
}
