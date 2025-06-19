using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ZG
{
    public readonly struct RenderConstantBuffer : IBufferElementData
    {
        public readonly int Alignment;

        public readonly int Stride;

        public readonly int Index;
        public readonly int Length;
        
        [NativeDisableUnsafePtrRestriction] 
        private readonly unsafe UnsafeList<int>* __byteOffset;

        [NativeDisableUnsafePtrRestriction] 
        private readonly unsafe byte* __bytes;

        public unsafe bool isCreated => __bytes != null;

        public unsafe RenderConstantBuffer(
            int alignment, 
            int stride, 
            int index,
            ref NativeList<int> byteOffset,
            ref NativeArray<byte> bytes)
        {
            Alignment = alignment;

            Stride = stride;

            Index = index;

            Length = bytes.Length;
            
            byteOffset[index] = 0;

            __byteOffset = byteOffset.GetUnsafeList();

            __bytes = (byte*)bytes.GetUnsafePtr();
        }

        public unsafe NativeArray<byte> Write(int stride, int count, out int byteOffset)
        {
            UnityEngine.Assertions.Assert.AreEqual(stride, Stride);
            int numBytes = stride * count, bytesToOffset = (numBytes + Alignment - 1) / Alignment * Alignment, 
                length = Interlocked.Add(ref __byteOffset->ElementAt(Index), bytesToOffset);
            UnityEngine.Assertions.Assert.IsTrue(length <= Length);
            //if(length > Length)
            //    UnityEngine.Debug.LogError($"RenderConstantBuffer: {length} out of {Length}");
            
            byteOffset = length - bytesToOffset;

            if (stride == 64 && byteOffset != 0)
                UnityEngine.Debug.LogError("WTF RCB");

            return CollectionHelper.ConvertExistingDataToNativeArray<byte>(
                __bytes + byteOffset, 
                numBytes, 
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
