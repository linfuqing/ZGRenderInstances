using System.Runtime.InteropServices;
using Unity.Entities;

namespace ZG
{
    [StructLayout(LayoutKind.Explicit, Size = 16, Pack = 16)]
    public struct RenderSkinnedData : IComponentData
    {
        [FieldOffset(0)]
        public uint pixelOffset;
        [FieldOffset(4)]
        public float depth;
    }
}
