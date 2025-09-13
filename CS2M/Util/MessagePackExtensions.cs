using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Colossal.Serialization.Entities;
using MessagePack;
using MessagePack.Attributeless;
using MessagePack.Attributeless.Implementation;
using MessagePack.Formatters;
using Unity.Collections;
using Unity.Entities;

namespace CS2M.Util
{
    public static class MessagePackExtensions
    {
        public static MessagePackSerializerOptionsBuilder BetterGraphOf(
            this MessagePackSerializerOptionsBuilder builder, Type self, params Assembly[] assemblies)
        {
            var result = new List<Type>();
            Add(self);

            void Add(Type type)
            {
                if (result.Contains(type)) return;

                if (type.IsConstructedGenericType)
                {
                    foreach (var t in type.GenericTypeArguments) Add(t);
                }

                if (type.IsArray)
                {
                    Add(type.GetElementType());
                    return;
                }

                if (!assemblies.Contains(type.Assembly)) return;

                result.Add(type);
                var children =
                    type.GetProperties()
                        .Where(p => !p.IsIndexed())
                        .Select(p => p.PropertyType)
                        .Distinct()
                        .Where(x => !x.IsEnum);
                var derivations = type.GetSubTypes(assemblies);

                foreach (var t in children.Concat(derivations))
                {
                    Add(t);
                }
            }

            foreach (var t in result)
            {
                if (t.IsAbstract) builder.AllSubTypesOf(t, assemblies);
                else builder.AutoKeyed(t);
            }

            return builder;
        }
    }

    public class ColossalFormatter<T> : IMessagePackFormatter<T> where T : struct, ISerializable
    {
        public void Serialize(
            ref MessagePackWriter writer, T value, MessagePackSerializerOptions options)
        {
            // Create writer and native list for serialization
            var wrappedWriter = new BinaryWriter();
            var byteArray = new NativeList<byte>(0, Allocator.Temp);
            wrappedWriter.Initialize(wrappedWriter.context, byteArray, new NativeArray<Entity>());

            // Do serialization
            value.Serialize(wrappedWriter);

            // Copy result to MessagePack writer
            writer.Write(byteArray.ToArray(Allocator.Temp).ToArray());

            // Dispose native list if necessary
            byteArray.Dispose();
        }

        public T Deserialize(
            ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            options.Security.DepthStep(ref reader);

            // Read byte array from MessagePack reader
            byte[] bytes = reader.ReadBytes().GetValueOrDefault().ToArray();

            // Create reader and native array for deserialization
            var wrappedReader = new BinaryReader();
            var byteArray = new NativeArray<byte>(bytes, Allocator.Temp);
            var position = new NativeReference<int>(0, Allocator.Temp);
            wrappedReader.Initialize(wrappedReader.context, byteArray, position, new NativeArray<Entity>());

            // Create default target element and deserialize
            T elem = default;
            elem.Deserialize(wrappedReader);

            // Dispose native fields if necessary
            byteArray.Dispose();
            position.Dispose();

            reader.Depth--;
            return elem;
        }
    }

    public class ColossalResolver : IFormatterResolver
    {
        // Resolver should be singleton.
        public static readonly IFormatterResolver Instance = new ColossalResolver();

        private ColossalResolver()
        {
        }

        // GetFormatter<T>'s get cost should be minimized so use type cache.
        public IMessagePackFormatter<T> GetFormatter<T>()
        {
            return FormatterCache<T>.Formatter;
        }

        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter;

            // generic's static constructor should be minimized for reduce type generation size!
            // use outer helper method.
            static FormatterCache()
            {
                Formatter = (IMessagePackFormatter<T>)ColossalResolverGetFormatterHelper.GetFormatter(typeof(T));
            }
        }
    }

    internal static class ColossalResolverGetFormatterHelper
    {
        // If type is concrete type, use type-formatter map
        private static readonly Dictionary<Type, object> FormatterMap = new Dictionary<Type, object>();

        internal static object GetFormatter(Type t)
        {
            if (FormatterMap.TryGetValue(t, out object formatter))
            {
                return formatter;
            }

            if (typeof(ISerializable).IsAssignableFrom(t) &&
                t.IsValueType) // Check type constraints for custom serializer
            {
                formatter = Activator.CreateInstance(typeof(ColossalFormatter<>).MakeGenericType(t));
                FormatterMap.Add(t, formatter);
                return formatter;
            }

            // If type can not get, must return null for fallback mechanism.
            return null;
        }
    }
}
