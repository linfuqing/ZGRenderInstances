using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ZG
{
    public readonly struct RenderConstantBuffer : IBufferElementData
    {
        private readonly int Index;
        private readonly int Length;

        [NativeDisableUnsafePtrRestriction] private readonly unsafe UnsafeList<int>* __byteOffset;

        [NativeDisableUnsafePtrRestriction] private readonly unsafe byte* __bytes;

        public unsafe bool isCreated => __bytes != null;

        public unsafe RenderConstantBuffer(
            int index,
            ref NativeList<int> byteOffset,
            ref NativeArray<byte> bytes)
        {
            Index = index;

            Length = bytes.Length;

            byteOffset[index] = 0;

            __byteOffset = byteOffset.GetUnsafeList();

            __bytes = (byte*)bytes.GetUnsafePtr();
        }

        public unsafe int Write(in NativeArray<byte> bytes)
        {
            int numBytes = bytes.Length, length = Interlocked.Add(ref __byteOffset->ElementAt(Index), numBytes);
            UnityEngine.Assertions.Assert.IsTrue(length <= Length);
            int offset = length - numBytes;
            UnsafeUtility.MemCpy(__bytes + offset, bytes.GetUnsafeReadOnlyPtr(), numBytes);

            return offset;
        }
    }
}
