using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Colossal.PSI.Common;
using Colossal.Serialization.Entities;
using CS2M.API.Commands;
using MessagePack;
using MessagePack.Attributeless;
using MessagePack.Attributeless.Implementation;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using NUnit.Framework;
using ProtoBuf;
using ProtoBuf.Meta;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using BinaryReader = Colossal.Serialization.Entities.BinaryReader;
using BinaryWriter = Colossal.Serialization.Entities.BinaryWriter;

namespace CS2M.Test
{
    extern alias UnityCore;

    public static class MessagePackExtensions
    {
        public static MessagePackSerializerOptionsBuilder BetterGraphOf(this MessagePackSerializerOptionsBuilder builder, Type self, params Assembly[] assemblies)
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
    
    
    public class MessagePackExtension<T> : IMessagePackFormatter<T> where T: ISerializable, new()
    {
        public void Serialize(
            ref MessagePackWriter writer, T value, MessagePackSerializerOptions options)
        {
            var wrappedWriter = new BinaryWriter();
            var byteArray = new NativeList<byte>(0, UnityCore::Unity.Collections.Allocator.Persistent);
            wrappedWriter.Initialize(wrappedWriter.context, byteArray, new UnityCore::Unity.Collections.NativeArray<Entity>());
            
            value.Serialize(wrappedWriter);
            writer.Write(wrappedWriter.ToBytes());
            
            byteArray.Dispose();
        }

        public T Deserialize(
            ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            options.Security.DepthStep(ref reader);

            var wrappedReader = new BinaryReader();
            byte[] bytes = reader.ReadBytes().GetValueOrDefault().ToBytes();
            var byteArray = new UnityCore::Unity.Collections.NativeArray<byte>(bytes, UnityCore::Unity.Collections.Allocator.Persistent);
            wrappedReader.Initialize(wrappedReader.context, byteArray, new NativeReference<int>(), new UnityCore::Unity.Collections.NativeArray<Entity>());
            
            var elem = new T();
            elem.Deserialize(wrappedReader);
            
            byteArray.Dispose();
            
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
        static readonly Dictionary<Type, object> formatterMap = new Dictionary<Type, object>()
        {
            {typeof(Colossal.Version), new MessagePackExtension<Colossal.Version>()},
            // add more your own custom serializers.
        };

        internal static object GetFormatter(Type t)
        {
            object formatter;
            if (formatterMap.TryGetValue(t, out formatter))
            {
                return formatter;
            }

            // If type can not get, must return null for fallback mechanism.
            return null;
        }
    }
    
    public class SerializeTest
    {
        [ProtoContract]
        public class FirstCommand : CommandBase
        {
            [ProtoMember(1)]
            public Vector3 StuffHere { get; set; }
            [ProtoMember(2)]
            public int DLC { get; set; }
        }
        
        [ProtoContract]
        public class SecondCommand : CommandBase
        {
            [ProtoMember(1)]
            public Vector2 MoreStuffHere { get; set; }
            public int Version { get; set; }
        }

        private void ProtoBufSerializeDeserialize(CommandBase cmd, RuntimeTypeModel model)
        {
            byte[] result;

            using (MemoryStream stream = new MemoryStream())
            {
                var dateTime = DateTime.Now;
                model.Serialize(stream, cmd);
                result = stream.ToArray();
                var diff = DateTime.Now - dateTime;
                Console.WriteLine("Serialize: " + diff);
            }
            
            CommandBase deserialize;

            using (MemoryStream stream = new MemoryStream(result))
            {
                var dateTime = DateTime.Now;
                deserialize = (CommandBase)model.Deserialize(stream, null, typeof(CommandBase));
                var diff = DateTime.Now - dateTime;
                Console.WriteLine("Deserialize: " + diff);
            }

            Assert.That(deserialize, Is.Not.Null);
            Assert.That(deserialize.GetType(), Is.EqualTo(cmd.GetType()));
        }

        [Test]
        public void ProtoBufNet()
        {
            RuntimeTypeModel model = RuntimeTypeModel.Create();
            model[typeof(Vector3)].SetSurrogate(typeof(Vector3Surrogate));
            model[typeof(Vector2)].SetSurrogate(typeof(Vector2Surrogate));
            
            model.Add(typeof(CommandBase), true);
            MetaType baseCmd = model[typeof(CommandBase)];
            
            baseCmd.AddSubType(100, typeof(FirstCommand));
            baseCmd.AddSubType(101, typeof(SecondCommand));
            model.Add(typeof(FirstCommand), true);
            model.Add(typeof(SecondCommand), true);
            
            model.CompileInPlace();
            
            ProtoBufSerializeDeserialize(new FirstCommand
            {
                SenderId = 1,
                StuffHere = new Vector3(1, 2, 3)
            }, model);
            
            ProtoBufSerializeDeserialize(new FirstCommand
            {
                SenderId = 2,
                StuffHere = new Vector3(3, 2, 1)
            }, model);
            
            ProtoBufSerializeDeserialize(new SecondCommand
            {
                SenderId = 3,
                MoreStuffHere = new Vector2(3, 2)
            }, model);
            
            ProtoBufSerializeDeserialize(new SecondCommand
            {
                SenderId = 4,
                MoreStuffHere = new Vector2(3, 2)
            }, model);
        }

        private CommandBase SerializeDeserialize(CommandBase cmd, MessagePackSerializerOptions options)
        {
            var dateTime = DateTime.Now;
            // Serialize with the typeless API
            byte[] blob = MessagePackSerializer.Serialize(cmd, options);
            var diff = DateTime.Now - dateTime;
            Console.WriteLine("Serialize: " + diff);
            
            dateTime = DateTime.Now;
            var deserialize = MessagePackSerializer.Deserialize<CommandBase>(blob, options);
            diff = DateTime.Now - dateTime;
            Console.WriteLine("Deserialize: " + diff);
            
            Assert.That(deserialize, Is.Not.Null);
            Assert.That(deserialize.GetType(), Is.EqualTo(cmd.GetType()));
            return deserialize;
        }

        

        [Test]
        public void MsgPack()
        {
            IFormatterResolver resolver = CompositeResolver.Create(
                // enable extension packages first
                ColossalResolver.Instance,
                MessagePack.Unity.Extension.UnityBlitResolver.Instance,
                MessagePack.Unity.UnityResolver.Instance,

                // finally use standard resolver
                StandardResolver.Instance
            );
            var options = MessagePackSerializerOptions.Standard.WithResolver(resolver).Configure();

            var assemblies = new List<Assembly>()
            {
                typeof(CommandBase).Assembly,
                typeof(FirstCommand).Assembly,
            };
            var model = options.BetterGraphOf(typeof(CommandBase), assemblies.ToArray()).Build();
            Console.WriteLine(model.Resolver.GetFormatter<Colossal.Version>());

            var first = (FirstCommand) SerializeDeserialize(new FirstCommand
            {
                SenderId = 1,
                StuffHere = new Vector3(1, 2, 3),
                DLC = DlcId.BaseGame.id
            }, model);
            Assert.That(first.SenderId, Is.EqualTo(1));
            Assert.That(first.StuffHere, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(first.DLC, Is.EqualTo(DlcId.BaseGame.id));
            
            SerializeDeserialize(new FirstCommand
            {
                SenderId = 2,
                StuffHere = new Vector3(3, 2, 1),
            }, model);
            
            var second = (SecondCommand) SerializeDeserialize(new SecondCommand
            {
                SenderId = 3,
                MoreStuffHere = new Vector2(3, 2),
                Version = new Colossal.Version(1, 3, 3).buildVersion
            }, model);
            Assert.That(second.SenderId, Is.EqualTo(3));
            Assert.That(second.MoreStuffHere, Is.EqualTo(new Vector2(3, 2)));
            Assert.That(second.Version, Is.EqualTo(new Colossal.Version(1, 3, 3).buildVersion));
            
            SerializeDeserialize(new SecondCommand
            {
                SenderId = 4,
                MoreStuffHere = new Vector2(3, 2)
            }, model);
        }
    }
}
