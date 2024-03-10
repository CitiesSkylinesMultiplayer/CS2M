using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;

namespace CS2M.API
{
    public struct ModConnection : IComponentData
    {
        /// <summary>
        ///     The name of this mod support instance.
        ///     Used for displaying messages to the user.
        /// </summary>
        public string Name;

        /// <summary>
        ///     If this instance should be enabled, only then
        ///     the register/unregister methods are called and
        ///     the command handlers are registered.
        /// </summary>
        public bool Enabled;

        /// <summary>
        ///     The class type of the supported mod.
        ///     This has to be set if Enabled is true.
        ///     It is used for detecting compatibility.
        /// </summary>
        public Type ModClass;

        /// <summary>
        ///     A list of assemblies to search for command handlers.
        ///     If this instance is enabled, all found commands
        ///     are added to the protocol.
        /// </summary>
        public List<Assembly> CommandAssemblies;
    }
}
