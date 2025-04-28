using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;

namespace ZG
{

    [StructLayout(LayoutKind.Explicit, Size = 64, Pack = 16)]
    public struct SpriteRenderInstanceData : IComponentData
    {
        [FieldOffset(0)] public float4 positionST;

        [FieldOffset(16)] public float4 uvST;

        [FieldOffset(32)] public float4 color;

        [FieldOffset(48)] public float textureIndex;
    }
}
