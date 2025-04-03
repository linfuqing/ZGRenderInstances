using System.Runtime.InteropServices;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


[StructLayout(LayoutKind.Explicit, Size = 64, Pack = 16)]
public struct SpriteRenderInstanceData : IComponentData
{
    [FieldOffset(0)]
    public float4 positionST;

    [FieldOffset(16)]
    public float4 uvST;

    [FieldOffset(32)]
    public float4 color;
    
    [FieldOffset(48)]
    public int textureIndex;
}

public partial class SpriteRenderSystem : RenderInstancesSystem<SpriteRenderInstanceData>
{
    private static readonly int ConstantBufferID = Shader.PropertyToID("UnityInstancing_SpriteInstance");
    
    private Mesh __mesh;

    public static Mesh GenerateQuad()
    {
        Vector3[] vertices =
        {
            new Vector3(1.0f, 1.0f, 0),
            new Vector3(1.0f, 0.0f, 0),
            new Vector3(0.0f, 0.0f, 0),
            new Vector3(0.0f, 1.0f, 0),
        };

        Vector2[] uv =
        {
            new Vector2(1, 1),
            new Vector2(1, 0),
            new Vector2(0, 0),
            new Vector2(0, 1)
        };

        int[] triangles =
        {
            0, 1, 2,
            2, 3, 0
        };

        return new Mesh
        {
            vertices = vertices,
            uv = uv,
            triangles = triangles
        };
    }

    public override int constantBufferID => ConstantBufferID;

    public override Mesh mesh => __mesh;

    protected override void OnCreate()
    {
        base.OnCreate();

        __mesh = GenerateQuad();
    }

    protected override void OnDestroy()
    {
        Object.DestroyImmediate(__mesh);

        __mesh = null;
        
        base.OnDestroy();
    }
}
