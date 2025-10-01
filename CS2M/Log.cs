using Colossal.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace CS2M
{
    public static class Log
    {
        public static ILog Logger { get; } = LogManager.GetLogger(nameof(CS2M))
            .SetShowsErrorsInUI(true).SetEffectiveness(Level.Info).SetLogStackTrace(false);

        public static void SetLoggingLevel(Level loggingLevel)
        {
            Logger.SetEffectiveness(loggingLevel);
        }

        public static void ErrorWithStackTrace(string message)
        {
            Logger.SetLogStackTrace(true);
            Logger.Error(message);
            Logger.SetLogStackTrace(false);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Error(string message)
        {
            Logger.Error(message);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Error(string message, Exception ex)
        {
            Logger.Error(ex, message);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Warn(string message)
        {
            Logger.Warn(message);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Info(string message)
        {
            Logger.Info(message);
            System.Diagnostics.Debug.Print(message);
        }

        public static void Debug(string message)
        {
            Logger.Debug(message);
            System.Diagnostics.Debug.Print(message);
        }

        public static void FindAllMethodsReturning<T>()
        {
            var result = new List<MethodInfo>();
            var targetType = typeof(T);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                foreach (var type in types)
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                    {
                        if (method.ReturnType == targetType)
                        {
                            result.Add(method);
                        }
                    }
                }
            }

            var methods = result;
            foreach (var method in methods)
            {
                Log.Info($"Метод: {method.DeclaringType.FullName}.{method.Name} возвращает ILocalAssetDatabase");
            }

        }
    }
}
