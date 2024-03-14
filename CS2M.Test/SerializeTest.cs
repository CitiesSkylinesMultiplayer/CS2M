using System;
using System.IO;
using MessagePack;
using MessagePack.Attributeless;
using MessagePack.Resolvers;
using NUnit.Framework;
using ProtoBuf;
using ProtoBuf.Meta;
using UnityEngine;

namespace CS2M.Test
{
    public class SerializeTest
    {
        [ProtoContract]
        public abstract class CommandBase
        {
            /// <summary>
            ///     The id of the sending player. -1 for the server.
            /// </summary>
            [ProtoMember(1)]
            public int SenderId { get; set; }
        }

        [ProtoContract]
        public class FirstCommand : CommandBase
        {
            [ProtoMember(1)]
            public Vector3 StuffHere;
        }
        
        [ProtoContract]
        public class SecondCommand : CommandBase
        {
            [ProtoMember(1)]
            public Vector2 MoreStuffHere;
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

        private void SerializeDeserialize(CommandBase cmd, MessagePackSerializerOptions options)
        {
            var dateTime = DateTime.Now;
            // Serialize with the typeless API
            byte[] blob = MessagePackSerializer.Serialize(cmd, options);
            var diff = DateTime.Now - dateTime;
            Console.WriteLine("Serialize: " + diff);
            
            dateTime = DateTime.Now;
            object deserialize = MessagePackSerializer.Deserialize<CommandBase>(blob, options);
            diff = DateTime.Now - dateTime;
            Console.WriteLine("Deserialize: " + diff);
            
            Assert.That(deserialize, Is.Not.Null);
            Assert.That(deserialize.GetType(), Is.EqualTo(cmd.GetType()));
        }

        [Test]
        public void MsgPack()
        {
            IFormatterResolver resolver = CompositeResolver.Create(
                // enable extension packages first
                MessagePack.Unity.Extension.UnityBlitResolver.Instance,
                MessagePack.Unity.UnityResolver.Instance,

                // finally use standard resolver
                StandardResolver.Instance
            );
            var options = new MessagePackSerializerOptions(resolver);

            options = options.Configure().GraphOf<CommandBase>(typeof(FirstCommand).Assembly).Build();

            SerializeDeserialize(new FirstCommand
            {
                SenderId = 1,
                StuffHere = new Vector3(1, 2, 3)
            }, options);
            
            SerializeDeserialize(new FirstCommand
            {
                SenderId = 2,
                StuffHere = new Vector3(3, 2, 1)
            }, options);
            
            SerializeDeserialize(new SecondCommand
            {
                SenderId = 3,
                MoreStuffHere = new Vector2(3, 2)
            }, options);
            
            SerializeDeserialize(new SecondCommand
            {
                SenderId = 4,
                MoreStuffHere = new Vector2(3, 2)
            }, options);
        }
    }
}
