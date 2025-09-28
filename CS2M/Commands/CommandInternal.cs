using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CS2M.API;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Helpers;
using CS2M.Mods;
using CS2M.Networking;
using CS2M.Util;
using MessagePack;
using MessagePack.Attributeless;
using MessagePack.Resolvers;
using MessagePack.Unity;
using MessagePack.Unity.Extension;

namespace CS2M.Commands
{
    public class CommandInternal
    {
        public static CommandInternal Instance;

        private readonly ConcurrentDictionary<Type, CommandHandler> _cmdMapping =
            new ConcurrentDictionary<Type, CommandHandler>();

        private MessagePackSerializerOptions _model;

        public CommandInternal()
        {
            Command.ConnectToCSM(SendToAll, SendToServer, SendToClients, GetCommandHandler);
        }

        /// <summary>
        ///     This method is used to send a command to a connected client.
        ///     Does only work if the current game acts as a server.
        /// </summary>
        /// <param name="player">The Player to send the command to.</param>
        /// <param name="command">The command to send.</param>
        public void SendToClient(Player player, CommandBase command)
        {
            NetworkInterface.Instance.SendToClient(player, command);
        }

        /// <summary>
        ///     This method is used to send a command to all connected clients.
        ///     Does only work if the current game acts as a server.
        /// </summary>
        /// <param name="command">The command to send.</param>
        public void SendToClients(CommandBase command)
        {
            // TransactionHandler.StartTransaction(command);
            //
            NetworkInterface.Instance.SendToClients(command);
        }

        // /// <summary>
        // ///     This method is used to send a command to all connected clients except the excluded player.
        // ///     Does only work if the current game acts as a server.
        // /// </summary>
        // /// <param name="command">The command to send.</param>
        // /// <param name="exclude">The player to not send the packet to.</param>
        // public void SendToOtherClients(CommandBase command, Player exclude)
        // {
        //     foreach (CSMPlayer player in MultiplayerManager.Instance.CurrentServer.ConnectedPlayers.Values)
        //     {
        //         if (player.Equals(exclude))
        //             continue;
        //
        //         SendToClient(player, command);
        //     }
        // }

        /// <summary>
        ///     This method is used to send a command to the server.
        ///     Does only work if the current game acts as a client.
        /// </summary>
        /// <param name="command">The command to send.</param>
        public void SendToServer(CommandBase command)
        {
            // if (MultiplayerManager.Instance.CurrentClient.Status == ClientStatus.Disconnected ||
            //     MultiplayerManager.Instance.CurrentClient.Status == ClientStatus.PreConnecting)
            //     return;
            //
            // TransactionHandler.StartTransaction(command);
            //
            NetworkInterface.Instance.SendToServer(command);
        }

        /// <summary>
        ///     This method is used to send a command to a connected partner.
        ///     If the current game acts as a server, the command is sent to all clients.
        ///     If it acts as a client, the command is sent to the server.
        /// </summary>
        /// <param name="command">The command to send.</param>
        public void SendToAll(CommandBase command)
        {
            NetworkInterface.Instance.SendToAll(command);
        }

        /// <summary>
        ///     This method is used to get the handler of given command.
        /// </summary>
        /// <param name="commandType">The Type of a CommandBase subclass.</param>
        /// <returns>The handler for the given command.</returns>
        public CommandHandler GetCommandHandler(Type commandType)
        {
            _cmdMapping.TryGetValue(commandType, out CommandHandler handler);
            return handler;
        }

        /// <summary>
        ///     This method is used to get the handler of given command.
        /// </summary>
        /// <returns>The handler for the given command.</returns>
        public TH GetCommandHandler<T, TH>() where T : CommandBase where TH : CommandHandler<T>
        {
            _cmdMapping.TryGetValue(typeof(T), out CommandHandler handler);
            return (TH)handler;
        }

        public byte[] Serialize(CommandBase command)
        {
            return MessagePackSerializer.Serialize(command, _model);
        }

        public CommandBase Deserialize(byte[] bytes)
        {
            return MessagePackSerializer.Deserialize<CommandBase>(bytes, _model);
        }

        public void RefreshModel()
        {
            _cmdMapping.Clear();
            try
            {
                // Configure MessagePack resolver
                IFormatterResolver resolver = CompositeResolver.Create(
                    // enable extension packages first
                    ColossalResolver.Instance,
                    UnityBlitResolver.Instance,
                    UnityResolver.Instance,
                    StandardResolver.Instance
                );
                MessagePackSerializerOptionsBuilder options =
                    MessagePackSerializerOptions.Standard.WithResolver(resolver).Configure();
                // First, find all CSM classes, then the other mods. This is necessary to
                // ensure that our management packets have always the same ids,
                // as for example the ConnectionRequest and ConnectionResult commands are used
                // to determine the installed mods.
                List<Type> packets = AssemblyHelper.FindClassesInCSM(typeof(CommandHandler)).ToList();
                var assemblies = new List<Assembly>
                {
                    typeof(CommandBase).Assembly,
                    typeof(Mod).Assembly
                };
                foreach (ModConnection connection in ModSupport.Instance.ConnectedMods.OrderBy(mod => mod.Name))
                {
                    assemblies.AddRange(connection.CommandAssemblies);
                    connection.CommandAssemblies.ForEach(input =>
                        packets.AddRange(AssemblyHelper.FindImplementationsInAssembly(input, typeof(CommandHandler))));
                }

                options.BetterGraphOf(typeof(CommandBase), assemblies.ToArray());

                Log.Info($"Initializing data model with {packets.Count} commands...");

                // Create instances of the handlers, initialize mappings and register command subclasses in the protobuf model
                foreach (Type type in packets)
                {
                    var handler = (CommandHandler)Activator.CreateInstance(type);
                    bool added = _cmdMapping.TryAdd(handler.GetDataType(), handler);
                    if (!added)
                    {
                        Log.Debug($"Handler for {handler.GetDataType()} already exists");
                    }
                }

                _model = options.Build();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to initialize data model", ex);
            }
        }
    }
}
