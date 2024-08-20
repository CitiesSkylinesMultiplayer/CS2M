using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CS2M.API;
using CS2M.API.Commands;
using CS2M.API.Networking;
using CS2M.Helpers;
using CS2M.Mods;
using CS2M.Networking;
using MessagePack;
using MessagePack.Attributeless;
using MessagePack.Resolvers;

namespace CS2M.Commands
{
    public class CommandInternal
    {
        public static CommandInternal Instance;

        private readonly Dictionary<Type, CommandHandler> _cmdMapping = new Dictionary<Type, CommandHandler>();

        private MessagePackSerializerOptions _model;

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
        public TH GetCommandHandler<T, TH>() where T : CommandBase where TH: CommandHandler<T>
        {
            _cmdMapping.TryGetValue(typeof(T), out CommandHandler handler);
            return (TH)handler;
        }

        public byte[] Serialize(CommandBase command)
        {
            return MessagePackSerializer.Serialize(command, _model);
        }

        public void RefreshModel()
        {
            _cmdMapping.Clear();
            try
            {
                // First, find all CSM classes, then the other mods. This is necessary to
                // ensure that our management packets have always the same ids,
                // as for example the ConnectionRequest and ConnectionResult commands are used
                // to determine the installed mods.
                IEnumerable<Type> packets = AssemblyHelper.FindClassesInCSM(typeof(CommandHandler));
                foreach (ModConnection connection in ModSupport.Instance.ConnectedMods.OrderBy(mod => mod.Name))
                {
                    foreach (Assembly assembly in connection.CommandAssemblies.OrderBy(assembly => assembly.FullName))
                    {
                        packets = packets.Concat(AssemblyHelper.FindImplementationsInAssembly(assembly, typeof(CommandHandler)));
                    }
                }

                Type[] handlers = packets.ToArray();
                Log.Info($"Initializing data model with {handlers.Length} commands...");

                // Configure MessagePack resolver
                IFormatterResolver resolver = CompositeResolver.Create(
                    // enable extension packages first
                    MessagePack.Unity.Extension.UnityBlitResolver.Instance,
                    MessagePack.Unity.UnityResolver.Instance,
                    StandardResolver.Instance
                );
                var options = MessagePackSerializerOptions.Standard.WithResolver(resolver).Configure();

                // Create instances of the handlers, initialize mappings and register command subclasses in the protobuf model
                foreach (Type type in handlers)
                {
                    CommandHandler handler = (CommandHandler)Activator.CreateInstance(type);
                    _cmdMapping.Add(handler.GetDataType(), handler);

                    // Add subtype to the MsgPack model with all attributes
                    options.SubType(typeof(CommandBase), handler.GetDataType());
                }

                _model = options.Build();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to initialize data model", ex);
            }
        }

        public CommandInternal()
        {
            Command.ConnectToCSM(this.SendToAll, this.SendToServer, this.SendToClients, this.GetCommandHandler);
        }
    }
}
