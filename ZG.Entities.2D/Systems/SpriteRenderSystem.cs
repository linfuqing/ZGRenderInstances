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
[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Presentation | WorldSystemFilterFlags.Editor)]
public partial class SpriteRenderSystem : SystemBase
{
    private uint __version;
    private SharedComponentTypeHandle<SpriteRenderMaterial> __materialType;
    private ComponentTypeHandle<SpriteRenderInstanceData> __instanceDataType;
    private ComponentTypeHandle<LocalToWorld> __localToWorldType;
    private EntityQuery __group;
    private NativeHashMap<WeakObjectReference<Material>, double> __times;
    private Mesh __mesh;
    private ComputeBuffer __computeBuffer;
    private CommandBuffer __commandBuffer;
    private Matrix4x4[] __matrices;

    private static int __constantBufferID = Shader.PropertyToID("UnityInstancing_SpriteInstance");

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
        
        __times = new NativeHashMap<WeakObjectReference<Material>, double>(1, Allocator.Persistent);
        
        __mesh = GenerateQuad();

        __computeBuffer =
            new ComputeBuffer(1024,
                TypeManager.GetTypeInfo<SpriteRenderInstanceData>().TypeSize, 
                ComputeBufferType.Constant, 
                ComputeBufferMode.Dynamic);
        
        __commandBuffer = new CommandBuffer();

        __matrices = new Matrix4x4[1024];
    }

    protected override void OnDestroy()
    {
        __times.Dispose();
        
        Object.DestroyImmediate(__mesh);

        __mesh = null;
        
        __computeBuffer.Dispose();
        
        __commandBuffer.Dispose();
        
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        uint version = (uint)__group.GetCombinedComponentOrderVersion(false);
        if (!ChangeVersionUtility.DidChange(version, __version))
            return;

        __version = version;
        
        __commandBuffer.Clear();

        __group.CompleteDependency();

        double time = SystemAPI.Time.ElapsedTime;
        using (var chunks = __group.ToArchetypeChunkArray(Allocator.Temp))
        {
            int length;
            NativeArray<SpriteRenderInstanceData> instanceDatas;
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

                    instanceDatas = chunk.GetNativeArray(ref __instanceDataType);
                    __computeBuffer.SetData(instanceDatas);

                    __commandBuffer.SetGlobalConstantBuffer(
                        __computeBuffer, 
                        __constantBufferID, 
                        0,
                        instanceDatas.Length * TypeManager.GetTypeInfo<SpriteRenderInstanceData>().TypeSize);

                    matrices = chunk.GetNativeArray(ref __localToWorldType).Reinterpret<Matrix4x4>();

                    length = matrices.Length;
                    NativeArray<Matrix4x4>.Copy(matrices, __matrices, length);
                    
                    __commandBuffer.DrawMeshInstanced(
                        __mesh,
                        0, 
                        material.value.Result, 
                        0, 
                        __matrices, 
                        length);
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
