using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using CS2M.UI;
using Game;
using Game.SceneFlow;
using Game.Serialization;
using Game.Settings;
using HarmonyLib;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Hash128 = Colossal.Hash128;

namespace CS2M.Helpers
{
    public class PacketStream : Stream
    {
        public const int SliceLength = 1024;

        private int _streamLength = 0;

        private List<byte[]> _slices = new List<byte[]>();
        private readonly byte[] _sliceBuffer = new byte[SliceLength];
        private int _sliceWriteOffset = 0;
        private int _sliceReadOffset = 0;

        public override void Flush()
        {
            if (_sliceWriteOffset > 0)
            {
                byte[] slice = new byte[_sliceWriteOffset];
                Array.Copy(_sliceBuffer, 0, slice, 0, _sliceWriteOffset);
                _slices.Add(slice);
                _streamLength += _sliceWriteOffset;
                _sliceWriteOffset = 0;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            _streamLength = (int)value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public unsafe int Read(byte* pTarget, int bytes)
        {
            Flush();

            int readBytes = 0;
            while (readBytes < bytes)
            {
                int id = _sliceReadOffset / SliceLength;
                int offset = _sliceReadOffset % SliceLength;
                if (_slices.Count <= id)
                {
                    break;
                }
                byte[] slice = _slices[id];
                int remain = SliceLength - offset;
                int toRead = math.min(remain, bytes - readBytes);

                fixed (byte* pSource = slice)
                {
                    // Copy the specified number of bytes from source to target.
                    for (int i = 0; i < toRead; i++)
                    {
                        pTarget[readBytes + i] = pSource[offset + i];
                    }
                }

                _sliceReadOffset += toRead;
                readBytes += toRead;
            }

            return readBytes;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (offset + count > buffer.Length)
            {
                return;
            }

            while (count > 0)
            {
                int sliceRemain = _sliceBuffer.Length - _sliceWriteOffset;
                int addHere = Math.Min(sliceRemain, count);
                Array.Copy(buffer, offset, _sliceBuffer, _sliceWriteOffset, addHere);
                _sliceWriteOffset += addHere;
                offset += addHere;
                count -= addHere;

                if (_sliceWriteOffset == _sliceBuffer.Length)
                {
                    _slices.Add(_sliceBuffer.ToArray());
                    _sliceWriteOffset = 0;
                }
            }

            _streamLength += count;
        }

        public List<byte[]> GetSlices()
        {
            return _slices;
        }

        public void SetSlices(List<byte[]> slices)
        {
            _slices = slices;
            _sliceReadOffset = 0;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _streamLength;
        public override long Position
        {
            get => _streamLength;
            set => throw new NotImplementedException();
        }
    }

    internal class SaveWrapper : SaveGameData
    {
        public SaveWrapper()
        {
            id = Identifier.None;
        }

        public override string ToString()
        {
            return "Multiplayer Save Game";
        }

        public new AsyncReadDescriptor GetAsyncReadDescriptor()
        {
            return new AsyncReadDescriptor("Multiplayer", "multiplayer");
        }
    }

    public partial class SaveLoadHelper : GameSystemBase
    {
        private SaveGameSystem _saveGameSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            _saveGameSystem = World.GetOrCreateSystemManaged<SaveGameSystem>();
            Enabled = false;
        }

        public async Task<PacketStream> SaveGame()
        {
            // See GameManager::Save
            while (_saveGameSystem.Enabled)
            {
                // TODO: Sleep?
            }
            // Disable auto-save so it doesn't collide with our save process
            bool autoSaveEnabled = SharedSettings.instance.general.autoSave;
            SharedSettings.instance.general.autoSave = false;

            // Cleanup memory
            UnityEngine.Resources.UnloadUnusedAssets();
            GC.Collect();

            // Save game to packet stream
            var stream = new PacketStream();
            _saveGameSystem.stream = stream;
            _saveGameSystem.context = new Context(Purpose.SaveGame, Game.Version.current, Hash128.Empty);
            await _saveGameSystem.RunOnce();
            stream.Flush();

            // Re-enable autosave if needed
            SharedSettings.instance.general.autoSave = autoSaveEnabled;
            return stream;
        }

        public async void LoadGame(PacketStream data)
        {
            SaveWrapper saveGame = new SaveWrapper();
            ReadSystemPatch.Stream = data;
            await GameManager.instance.Load(GameMode.Game, Purpose.LoadGame, saveGame);
            ReadSystemPatch.Stream = null;
        }

        protected override void OnUpdate()
        {
            throw new System.NotImplementedException();
        }
    }

    [HarmonyPatch]
    // ReSharper disable once ClassNeverInstantiated.Global
    internal class ReadSystemPatch
    {
        public static PacketStream Stream;

        public static unsafe void Prefix(void* data, int bytes)
        {
            if (Stream == null)
            {
                return;
            }

            int readBytes = Stream.Read((byte*) data, bytes);
            if (readBytes != bytes)
            {
                throw new IOException("Failed to read from multiplayer stream!");
            }
        }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(StreamBinaryReader).GetMethod("ReadBytes", new[] { typeof(void).MakePointerType(), typeof(int) });
            yield return typeof(StreamBinaryReader).GetMethod("ReadBytes", new[] { typeof(void).MakePointerType(), typeof(int), typeof(JobHandle).MakeByRefType() });
        }
    }
}
