using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ZG
{
    public readonly struct RenderConstantBuffer : IBufferElementData
    {
        private readonly int Alignment;

        private readonly int Index;
        private readonly int Length;
        
        [NativeDisableUnsafePtrRestriction] 
        private readonly unsafe UnsafeList<int>* __byteOffset;

        [NativeDisableUnsafePtrRestriction] 
        private readonly unsafe byte* __bytes;

        public unsafe bool isCreated => __bytes != null;

        public unsafe RenderConstantBuffer(
            int alignment, 
            int index,
            ref NativeList<int> byteOffset,
            ref NativeArray<byte> bytes)
        {
            Alignment = alignment;

            Index = index;

            Length = bytes.Length;
            
            byteOffset[index] = 0;

            __byteOffset = byteOffset.GetUnsafeList();

            __bytes = (byte*)bytes.GetUnsafePtr();
        }

        public unsafe NativeArray<byte> Write(int byteCount, out int offset)
        {
            int bytesToOffset = (byteCount + Alignment - 1) / Alignment * Alignment, 
                length = Interlocked.Add(ref __byteOffset->ElementAt(Index), bytesToOffset);
            UnityEngine.Assertions.Assert.IsTrue(length <= Length);
            offset = length - bytesToOffset;

            return CollectionHelper.ConvertExistingDataToNativeArray<byte>(
                __bytes + offset, 
                byteCount, 
                Allocator.None,
                true);
        }

        public unsafe int Write(in NativeArray<byte> bytes)
        {
            int numBytes = bytes.Length, 
                bytesToOffset = (numBytes + Alignment - 1) / Alignment * Alignment, 
                length = Interlocked.Add(ref __byteOffset->ElementAt(Index), bytesToOffset);
            UnityEngine.Assertions.Assert.IsTrue(length <= Length);
            int offset = length - bytesToOffset;
            UnsafeUtility.MemCpy(__bytes + offset, bytes.GetUnsafeReadOnlyPtr(), numBytes);

            return offset;
        }
    }
}
