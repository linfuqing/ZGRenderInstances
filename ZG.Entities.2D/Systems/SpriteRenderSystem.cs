using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct SpriteRenderInstanceData : IComponentData
{
    public float4 positionST;

    public float4 uvST;
    
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
