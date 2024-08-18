using System.Collections.Generic;
using System.Linq;
using Colossal.UI.Binding;
using Colossal.PSI.Common;
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

    public readonly struct ModSupportStatus : IJsonWritable
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

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin(this.GetType().FullName);
            writer.PropertyName("name");
            writer.Write(this.Name);
            writer.PropertyName("type_name");
            writer.Write(this.TypeName);
            writer.PropertyName("support");
            writer.Write(this.Type.ToString());
            writer.PropertyName("client_side");
            writer.Write(this.ClientSide);
            writer.TypeEnd();
        }
    }

    public static class ModCompat
    {
        private static readonly string[] ClientSideMods = { "I18NEverywhere.I18NEverywhere" };
        private static readonly string[] ClientSideDlcs = { "DeluxeRelaxRadio" };
        private static readonly string[] IgnoredMods = { };

        private static readonly string[] KnownToWork = {  };
        private static readonly string[] UnsupportedMods = {  };

        public static IEnumerable<ModSupportStatus> GetModSupport()
        {
            foreach (IDlc dlc in PlatformManager.instance.EnumerateDLCs())
            {
                if (PlatformManager.instance.IsDlcOwned(dlc))
                {
                    string name = dlc.backendName ?? dlc.internalName;
                    yield return new ModSupportStatus("DLC: " + name, dlc.internalName, ModSupportType.Supported, ClientSideDlcs.Contains(dlc.internalName));
                }
            }

            foreach (ModManager.ModInfo info in GameManager.instance.modManager)
            {
                // Skip disabled mods
                if (!info.isLoaded)
                    continue;

                foreach (IMod modInstance in info.instances)
                {
                    // Skip CSM itself
                    if (modInstance?.GetType() == typeof(Mod))
                        continue;

                    string modInstanceName = modInstance?.GetType().ToString();

                    // Skip ignored mods
                    if (IgnoredMods.Contains(modInstanceName))
                        continue;

                    bool isClientSide = ClientSideMods.Contains(modInstanceName);

                    // Mods known to work
                    if (KnownToWork.Contains(modInstanceName))
                    {
                        yield return new ModSupportStatus(info.asset.name, modInstanceName, ModSupportType.KnownWorking, isClientSide);
                        continue;
                    }

                    // Mods with loaded multiplayer support
                    if (ModSupport.Instance.ConnectedMods.Select(mod => mod.ModClass)
                        .Contains(modInstance?.GetType()))
                    {
                        yield return new ModSupportStatus(info.asset.name, modInstanceName, ModSupportType.Supported, isClientSide);
                        continue;
                    }

                    // Decide between unsupported and unknown
                    if (UnsupportedMods.Contains(modInstanceName))
                    {
                        yield return new ModSupportStatus(info.asset.name, modInstanceName, ModSupportType.Unsupported, isClientSide);
                        continue;
                    }

                    yield return new ModSupportStatus(info.asset.name, modInstanceName, ModSupportType.Unknown, isClientSide);
                }
            }
        }

        public static List<ModSupportStatus> GetModSupportList()
        {
            return GetModSupport().ToList();
        }
    }
}
