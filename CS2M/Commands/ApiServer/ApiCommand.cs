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
    public class SerializerOptions : MessagePackSerializerOptions
    {
        public SerializerOptions(IFormatterResolver resolver) : base(resolver)
        {
        }
    }

    public class ApiCommand
    {
        public static ApiCommand Instance;

        private readonly Dictionary<Type, ApiCommandHandler> _apiCmdMapping = new Dictionary<Type, ApiCommandHandler>();

        private MessagePackSerializerOptions _model;

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

                    // finally use standard resolver
                    BuiltinResolver.Instance, // Try Builtin

                    DynamicGenericResolver.Instance, // Try Array, Tuple, Collection, Enum(Generic Fallback)

                    //DynamicUnionResolver.Instance, // Try Union(Interface)
                    DynamicObjectResolver.Instance // Try Object
                );
                var options = new SerializerOptions(resolver).Configure();

                // Create instances of the handlers, initialize mappings and register command subclasses in the protobuf model
                foreach (Type type in apiHandlers)
                {
                    ApiCommandHandler handler = (ApiCommandHandler)Activator.CreateInstance(type);
                    _apiCmdMapping.Add(handler.GetDataType(), handler);

                    // Add subtype to the MsgPack model with all attributes
                    options.SubType(typeof(ApiCommandBase), handler.GetDataType());
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
