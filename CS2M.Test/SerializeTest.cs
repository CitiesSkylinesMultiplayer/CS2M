using System;
using CS2M.API.Commands;
using MessagePack;
using MessagePack.Resolvers;
using NUnit.Framework;
using UnityEngine;

namespace CS2M.Test
{
    public class SerializeTest
    {
        public class FirstCommand : CommandBase
        {
            public Vector3 StuffHere;
        }

        [Test]
        public void Test()
        {
            IFormatterResolver resolver = CompositeResolver.Create(
                // enable extension packages first
                MessagePack.Unity.Extension.UnityBlitResolver.Instance,
                MessagePack.Unity.UnityResolver.Instance,

                // finally use standard (default) resolver
                StandardResolver.Instance
            );
            var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);

            // Pass options every time or set as default
            MessagePackSerializer.DefaultOptions = options;

            object mc = new FirstCommand
            {
                SenderId = 1,
                StuffHere = new Vector3(1, 2, 3)
            };

            // Serialize with the typeless API
            byte[] blob = MessagePackSerializer.Typeless.Serialize(mc);

            // Blob has embedded type-assembly information.
            Console.WriteLine(MessagePackSerializer.ConvertToJson(blob));

            object deserialize = MessagePackSerializer.Typeless.Deserialize(blob);

            Assert.That(deserialize, Is.Not.Null);
            Assert.That((deserialize as FirstCommand)?.SenderId, Is.EqualTo(1));

        }
    }
}
