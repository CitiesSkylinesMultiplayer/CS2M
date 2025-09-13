using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Game;
using Game.SceneFlow;
using Game.Serialization;
using Game.Settings;
using HarmonyLib;
using Unity.Jobs;
using UnityEngine;
using Hash128 = Colossal.Hash128;
using Version = Game.Version;

namespace CS2M.Helpers
{
    /// <summary>
    ///     This class implements a byte stream of sliced data packets.
    /// </summary>
    public class SlicedPacketStream : Stream
    {
        /// <summary>
        ///     Length of the slices.
        /// </summary>
        private const int SliceLength = 1024;

        private readonly byte[] _sliceBuffer = new byte[SliceLength];

        private readonly List<byte[]> _slices = new List<byte[]>();
        private int _sliceReadOffset;
        private int _sliceWriteOffset;

        private int _streamLength;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _streamLength;

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override void Flush()
        {
            if (_sliceWriteOffset > 0)
            {
                var slice = new byte[_sliceWriteOffset];
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
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public unsafe int Read(byte* pTarget, int bytes)
        {
            // Flush any remaining write bytes
            Flush();

            var readBytes = 0;
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
                int toRead = Math.Min(remain, bytes - readBytes);

                fixed (byte* pSource = slice)
                {
                    // Copy the specified number of bytes from source to target.
                    for (var i = 0; i < toRead; i++)
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

        public bool AppendSlice(byte[] slice)
        {
            if (slice.Length > SliceLength)
            {
                return false;
            }

            if (slice.Length < SliceLength)
            {
                Array.Copy(slice, 0, _sliceBuffer, 0, slice.Length);
                _slices.Add(_sliceBuffer.ToArray());
            }
            else
            {
                _slices.Add(slice);
            }

            _streamLength += SliceLength;
            return true;
        }

        public void Clear()
        {
            _slices.Clear();
            _sliceWriteOffset = 0;
            _sliceWriteOffset = 0;
            _streamLength = 0;
        }
    }

    /// <summary>
    ///     Simple wrapper class around SaveGameData to simulate a save game.
    /// </summary>
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

        public async Task<SlicedPacketStream> SaveGame()
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
            Resources.UnloadUnusedAssets();
            GC.Collect();

            // Save game to packet stream
            var stream = new SlicedPacketStream();
            _saveGameSystem.stream = stream;
            _saveGameSystem.context = new Context(Purpose.SaveGame, Version.current, Hash128.Empty);
            await _saveGameSystem.RunOnce();
            stream.Flush();

            // Re-enable autosave if needed
            SharedSettings.instance.general.autoSave = autoSaveEnabled;
            return stream;
        }

        public async Task<bool> LoadGame(SlicedPacketStream data)
        {
            var saveGame = new SaveWrapper();
            ReadSystemPatch.Stream = data;
            AssetDataPatch.OverrideAssetData = true;
            bool result = await GameManager.instance.Load(GameMode.Game, Purpose.LoadGame, saveGame);
            AssetDataPatch.OverrideAssetData = false;
            ReadSystemPatch.Stream = null;
            return result;
        }

        protected override void OnUpdate()
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    ///     This patch overrides the ReadBytes method that is called while deserializing a save game.
    ///     Instead of loading it from file, the bytes are extracted from our PacketStream.
    /// </summary>
    [HarmonyPatch]
    // ReSharper disable once ClassNeverInstantiated.Global
    internal class ReadSystemPatch
    {
        public static SlicedPacketStream Stream;

        public static unsafe bool Prefix(void* data, int bytes)
        {
            if (Stream == null)
            {
                return true;
            }

            int readBytes = Stream.Read((byte*)data, bytes);
            if (readBytes != bytes)
            {
                throw new IOException("Failed to read from multiplayer stream!");
            }

            return false;
        }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return typeof(StreamBinaryReader).GetMethod("ReadBytes",
                new[] { typeof(void).MakePointerType(), typeof(int) });
            yield return typeof(StreamBinaryReader).GetMethod("ReadBytes",
                new[] { typeof(void).MakePointerType(), typeof(int), typeof(JobHandle).MakeByRefType() });
        }
    }

    /// <summary>
    ///     This patch makes our custom SaveWrapper usable without throwing an exception.
    /// </summary>
    [HarmonyPatch(typeof(AssetData))]
    [HarmonyPatch(nameof(AssetData.GetAsyncReadDescriptor))]
    // ReSharper disable once ClassNeverInstantiated.Global
    internal class AssetDataPatch
    {
        public static bool OverrideAssetData;

        // ReSharper disable once InconsistentNaming
        public static bool Prefix(ref AsyncReadDescriptor __result)
        {
            if (!OverrideAssetData)
            {
                return true;
            }

            __result = new AsyncReadDescriptor("Multiplayer", "multiplayer");
            return false;
        }
    }
}