using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Modding;
using Game.SceneFlow;

namespace CS2M.Helpers
{
    public static class AssemblyHelper
    {
        private static IEnumerable<Assembly> GetEnabledAssemblies()
        {
            return GameManager.instance.modManager.Where(info => info.asset.isEnabled)
                .Select(info => info.asset.assembly).ToList();
        }

        /// <summary>
        /// Searches all the mod assemblies for class definitions that implement the given Type.
        /// </summary>
        public static IEnumerable<Type> FindClassesInMods(Type typeToSearchFor)
        {
            return GetEnabledAssemblies().SelectMany(assembly => FindImplementationsInAssembly(assembly, typeToSearchFor));
        }

        /// <summary>
        /// Searches CSM Assembly for class definitions that implement the given Type.
        /// </summary>
        public static IEnumerable<Type> FindClassesInCSM(Type typeToSearchFor)
        {
            // Only use own assembly, others have to be registered as mods
            Assembly assembly = typeof(CS2M).Assembly;

            IEnumerable<Type> handlers = FindImplementationsInAssembly(assembly, typeToSearchFor);
            foreach (Type handler in handlers)
            {
                yield return handler;
            }
        }

        /// <summary>
        /// Finds all the implementations of the given type in the given Assembly.
        /// </summary>
        public static IEnumerable<Type> FindImplementationsInAssembly(Assembly assembly, Type typeToSearchFor)
        {
            Type[] types = Type.EmptyTypes;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                // ignored
            }

            foreach (Type type in types)
            {
                bool isValid = false;
                try
                {
                    isValid = typeToSearchFor.IsAssignableFrom(type) && type.IsClass && !type.IsAbstract;
                }
                catch
                {
                    // ignored
                }

                if (isValid)
                {
                    yield return type;
                }
            }
        }
    }
}
