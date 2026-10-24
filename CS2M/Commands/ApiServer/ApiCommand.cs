using System;
using System.Collections.Generic;
using System.Linq;
using CS2M.Commands.ApiServer.Handler;
using CS2M.Helpers;
using CS2M.Networking;
using MessagePack;
using MessagePack.Attributeless;
using MessagePack.Resolvers;

namespace CS2M.Commands.ApiServer
{
    public class ApiCommand
    {
        public static ApiCommand Instance;

        private readonly Dictionary<Type, ApiCommandHandler> _apiCmdMapping = new Dictionary<Type, ApiCommandHandler>();

        private MessagePackSerializerOptions _model;

        // Replies come over UDP from an address anyone can spoof: read them as untrusted data
        private MessagePackSerializerOptions _untrustedModel;

        /// <summary>
        ///     The order the commands are registered in, which gives each its type key on the wire (0, 1, 2...).
        ///     The lobby server (CS2M.Lobby/Protocol.cs) uses the same keys, so only ever add new commands at the end.
        /// </summary>
        private static readonly Type[] ProtocolOrder =
        {
            typeof(PortCheckRequestCommand),
            typeof(PortCheckResultCommand),
            typeof(ServerRegistrationCommand),
            typeof(SessionInfoCommand),
            typeof(ServerUnregisterCommand),
            typeof(SessionDetailsCommand),
        };

        /// <summary>
        ///     This method is used to send a command to the API server.
        ///     Does only work if the current game acts as a server.
        /// </summary>
        /// <param name="command">The command to send.</param>
        public void SendToApiServer(ApiCommandBase command)
        {
            NetworkInterface.Instance.SendToApiServer(command);
        }

        /// <summary>
        ///     This method is used to get the handler of given API command.
        /// </summary>
        /// <param name="commandType">The Type of a ApiCommandBase subclass.</param>
        /// <returns>The handler for the given command.</returns>
        public ApiCommandHandler GetApiCommandHandler(Type commandType)
        {
            _apiCmdMapping.TryGetValue(commandType, out ApiCommandHandler handler);
            return handler;
        }

        /// <summary>
        ///     Serializes the command into a byte array for sending over the network.
        /// </summary>
        /// <returns>A byte array containing the message.</returns>
        public byte[] Serialize(ApiCommandBase command)
        {
            return MessagePackSerializer.Serialize(command, _model);
        }

        /// <summary>
        ///     Deserializes a message received from the API server.
        /// </summary>
        public ApiCommandBase Deserialize(byte[] message)
        {
            return MessagePackSerializer.Deserialize<ApiCommandBase>(message, _untrustedModel);
        }

        public void RefreshModel()
        {
            _apiCmdMapping.Clear();
            try
            {
                // First, setup protocol model for the API server:
                Type[] apiHandlers = AssemblyHelper.FindClassesInCSM(typeof(ApiCommandHandler)).ToArray();
                Log.Info($"Initializing API data model with {apiHandlers.Length} commands...");
                // Configure MessagePack resolver
                IFormatterResolver resolver = CompositeResolver.Create(
                    // enable extension packages first
                    MessagePack.Unity.Extension.UnityBlitResolver.Instance,
                    MessagePack.Unity.UnityResolver.Instance,
                    StandardResolver.Instance
                );
                var options = MessagePackSerializerOptions.Standard.WithResolver(resolver).Configure();

                // Register in a fixed order: the keys must match the API server's, and reflection order isn't stable
                apiHandlers = apiHandlers
                    .OrderBy(type =>
                    {
                        int index = Array.IndexOf(ProtocolOrder, GetCommandType(type));
                        return index < 0 ? int.MaxValue : index;
                    })
                    .ThenBy(type => GetCommandType(type).FullName, StringComparer.Ordinal)
                    .ToArray();

                // Create instances of the handlers, initialize mappings and register command subclasses in the protobuf model
                foreach (Type type in apiHandlers)
                {
                    ApiCommandHandler handler = (ApiCommandHandler)Activator.CreateInstance(type);
                    _apiCmdMapping.Add(handler.GetDataType(), handler);

                    // Add subtype to the MsgPack model with all attributes
                    options.SubType(typeof(ApiCommandBase), handler.GetDataType());
                }

                _model = options.Build();
                _untrustedModel = _model.WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(16));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to initialize data model", ex);
            }
        }

        private static Type GetCommandType(Type handlerType)
        {
            Type type = handlerType;
            while (type != null && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiCommandHandler<>)))
            {
                type = type.BaseType;
            }

            return type?.GetGenericArguments()[0] ?? handlerType;
        }
    }
}
