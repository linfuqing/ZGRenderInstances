using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

public struct SpriteRenderMaterial : ISharedComponentData
{
    public float time;
    public WeakObjectReference<Material> value;
}

public struct SpriteRenderInstanceData : IComponentData
{
    public float4 positionST;

    public float4 uvST;
    
    public int textureIndex;
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class SpriteRenderSystem : SystemBase
{
    private SharedComponentTypeHandle<SpriteRenderMaterial> __materialType;
    private ComponentTypeHandle<SpriteRenderInstanceData> __instanceDataType;
    private ComponentTypeHandle<LocalToWorld> __localToWorldType;
    private EntityQuery __group;
    private NativeHashMap<WeakObjectReference<Material>, double> __times;
    private Mesh __mesh;
    private GraphicsBuffer __graphicsBuffer;
    private MaterialPropertyBlock __materialPropertyBlock;
    private CommandBuffer __commandBuffer;
    private Matrix4x4[] __matrices;

    public static CommandBuffer commandBuffer
    {
        get => World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SpriteRenderSystem>()
            .__commandBuffer;
    }
    
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

    protected override void OnCreate()
    {
        base.OnCreate();

        __materialType = GetSharedComponentTypeHandle<SpriteRenderMaterial>();
        __instanceDataType = GetComponentTypeHandle<SpriteRenderInstanceData>(true);
        __localToWorldType = GetComponentTypeHandle<LocalToWorld>(true);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SpriteRenderMaterial, SpriteRenderInstanceData, LocalToWorld>()
                .Build(this);
        
        RequireForUpdate(__group);

        __times = new NativeHashMap<WeakObjectReference<Material>, double>(1, Allocator.Persistent);
        
        __mesh = GenerateQuad();

        __graphicsBuffer =
            new GraphicsBuffer(GraphicsBuffer.Target.Constant, GraphicsBuffer.UsageFlags.None, 1024,
                TypeManager.GetTypeInfo<SpriteRenderInstanceData>().TypeSize);
        
        __materialPropertyBlock = new MaterialPropertyBlock();

        __materialPropertyBlock.SetBuffer("SpriteInstance", __graphicsBuffer);

        __commandBuffer = new CommandBuffer();

        __matrices = new Matrix4x4[1024];
    }

    protected override void OnDestroy()
    {
        __times.Dispose();
        
        Object.DestroyImmediate(__mesh);

        __mesh = null;
        
        __graphicsBuffer.Dispose();
        
        __commandBuffer.Dispose();
        
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        __commandBuffer.Clear();

        __group.CompleteDependency();

        double time = SystemAPI.Time.ElapsedTime;
        using (var chunks = __group.ToArchetypeChunkArray(Allocator.Temp))
        {
            int length;
            NativeArray<Matrix4x4> matrices;
            foreach (var chunk in chunks)
            {
                var material = chunk.GetSharedComponent(__materialType);
                if(!__times.ContainsKey(material.value))
                    material.value.LoadAsync();
                
                __times[material.value] = time + material.time;

                if (ObjectLoadingStatus.Completed == material.value.LoadingStatus)
                {
                    __localToWorldType.Update(this);
                    __instanceDataType.Update(this);
                    __graphicsBuffer.SetData(chunk.GetNativeArray(ref __instanceDataType));

                    matrices = chunk.GetNativeArray(ref __localToWorldType).Reinterpret<Matrix4x4>();

                    length = matrices.Length;
                    NativeArray<Matrix4x4>.Copy(matrices, __matrices, length);
                    
                    __commandBuffer.DrawMeshInstanced(
                        __mesh,
                        0, 
                        material.value.Result, 
                        0, 
                        __matrices, 
                        length, 
                        __materialPropertyBlock);
                }
            }
        }

        if (!__times.IsEmpty)
        {
            UnsafeList<WeakObjectReference<Material>> materialsToRelease = default;
            foreach (var temp in __times)
            {
                if (temp.Value > time)
                    break;

                if (!materialsToRelease.IsCreated)
                    materialsToRelease = new UnsafeList<WeakObjectReference<Material>>(1, Allocator.Temp);
                
                materialsToRelease.Add(temp.Key);
            }

            if (materialsToRelease.IsCreated)
            {
                foreach (var materialToRelease in materialsToRelease)
                {
                    __times.Remove(materialToRelease);
                    
                    materialToRelease.Release();
                }
                
                materialsToRelease.Dispose();
            }
        }
    }
}
