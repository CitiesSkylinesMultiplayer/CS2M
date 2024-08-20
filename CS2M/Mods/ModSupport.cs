using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Serialization.Entities;
using CS2M.Commands;
using CS2M.Helpers;
using CS2M.API;
using CS2M.Commands.ApiServer;
using Game;
using Game.SceneFlow;

namespace CS2M.Mods
{
    internal class ModSupport
    {
        private static ModSupport _instance;
        public static ModSupport Instance => _instance ?? (_instance = new ModSupport());

        public List<ModConnection> ConnectedMods { get; } = new List<ModConnection>();

        public List<string> RequiredModsForSync
        {
            get
            {
                return ConnectedNonClientModNames
                    /*.Concat(Singleton<PluginManager>.instance.GetPluginsInfo()
                      .Where(plugin => plugin.isEnabled && plugin.isBuiltin).Select(plugin => plugin.name))
                    .Concat(AssetNames)*/.ToList();
            }
        }

        private IEnumerable<string> ConnectedNonClientModNames
        {
            get
            {
                return ConnectedMods.Where(connection =>
                    connection.ModClass != null
                ).Select(connection => connection.Name).ToList();
            }
        }

        private static IEnumerable<string> AssetNames
        {
            get
            {
                return new List<string>();/*PackageManager.FilterAssets(UserAssetType.CustomAssetMetaData)
                    .Where(asset => asset.isEnabled)
                    .Select(asset => new EntryData(asset))
                    .Select(entry => entry.entryName.Split('(')[0].Trim());*/
            }
        }

        public void Init()
        {
            LoadModConnections();
            GameManager.instance.onGamePreload += OnGamePreload;
            //Singleton<PluginManager>.instance.eventPluginsChanged += LoadModConnections;
            //Singleton<PluginManager>.instance.eventPluginsStateChanged += LoadModConnections;
        }

        private void LoadModConnections()
        {
            ConnectedMods.Clear();
            IEnumerable<Type> handlers = AssemblyHelper.FindClassesInMods(typeof(ModConnection));

            foreach (Type handler in handlers)
            {
                if (handler.IsAbstract)
                {
                    continue;
                }

                ModConnection connectionInstance = (ModConnection)Activator.CreateInstance(handler);

                if (connectionInstance != null)
                {
                    if (connectionInstance.Enabled)
                    {
                        Log.Info($"Mod connected: {connectionInstance.Name}");
                        ConnectedMods.Add(connectionInstance);
                    }
                    else
                    {
                        Log.Debug($"Mod support for {connectionInstance.Name} found but not enabled.");
                    }
                }
                else
                {
                    Log.Warn($"Mod {handler.Assembly} failed to instantiate.");
                }
            }

            // Refresh data model
            CommandInternal.Instance.RefreshModel();
            ApiCommand.Instance.RefreshModel();
        }

        private void OnGamePreload(Purpose purpose, GameMode mode)
        {
            Log.Debug("Purpose: " + purpose + "; GameMode: " + mode);
            if (mode == GameMode.Game)
            {
                // TODO: Decide by mode if the function should be called
                foreach (ModConnection mod in ConnectedMods)
                {
                    mod.RegisterHandlers();
                }
            }
        }

        // TODO:
        private void OnLevelUnloading()
        {
            foreach (ModConnection mod in ConnectedMods)
            {
                mod.UnregisterHandlers();
            }
        }

        public void DestroyConnections()
        {
            ConnectedMods.Clear();
            ConnectedMods.TrimExcess();
            
            GameManager.instance.onGamePreload -= OnGamePreload;
            //Singleton<PluginManager>.instance.eventPluginsChanged -= LoadModConnections;
            //Singleton<PluginManager>.instance.eventPluginsStateChanged -= LoadModConnections;
        }
    }
}
