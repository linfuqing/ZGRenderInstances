using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

public struct RenderAsset<T> where T : Object
{
    public struct Manager
    {
        private NativeHashMap<WeakObjectReference<T>, double> __times;

        public Manager(in AllocatorManager.AllocatorHandle allocator)
        {
            __times = new NativeHashMap<WeakObjectReference<T>, double>(1, allocator);
        }

        public void Dispose()
        {
            __times.Dispose();
        }

        public void Retain(double time, in RenderAsset<T> asset)
        {
            if(!__times.ContainsKey(asset.value))
                asset.value.LoadAsync();
            
            __times[asset.value] = time + asset.releaseTime;
        }
        
        public void ReleaseTimeoutAssets(double time)
        {
            if (!__times.IsEmpty)
            {
                UnsafeList<WeakObjectReference<T>> objectsToRelease = default;
                foreach (var temp in __times)
                {
                    if (temp.Value > time)
                        break;

                    if (!objectsToRelease.IsCreated)
                        objectsToRelease = new UnsafeList<WeakObjectReference<T>>(1, Allocator.Temp);
                
                    objectsToRelease.Add(temp.Key);
                }

                if (objectsToRelease.IsCreated)
                {
                    foreach (var objectToRelease in objectsToRelease)
                    {
                        __times.Remove(objectToRelease);
                    
                        objectToRelease.Release();
                    }
                
                    objectsToRelease.Dispose();
                }
            }
        }
    }
    
    public float releaseTime;
    public WeakObjectReference<T> value;

    public bool isCreated => !value.Equals(default);

}

public struct SpriteRenderSharedData : ISharedComponentData
{
    public int subMeshIndex;
    public RenderAsset<Mesh> mesh;
    public RenderAsset<Material> material;
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
    public const int MAX_INSTANCE_COUNT = 1024;
    
    private uint __version;
    private SharedComponentTypeHandle<SpriteRenderSharedData> __sharedDataType;
    private ComponentTypeHandle<SpriteRenderInstanceData> __instanceDataType;
    private ComponentTypeHandle<LocalToWorld> __localToWorldType;
    private EntityQuery __group;
    private RenderAsset<Material>.Manager __materials;
    private RenderAsset<Mesh>.Manager __meshes;
    private Mesh __mesh;
    private ComputeBuffer __computeBuffer;
    private CommandBuffer __commandBuffer;
    private Matrix4x4[] __matrices;

    private static readonly int ConstantBufferID = Shader.PropertyToID("UnityInstancing_SpriteInstance");

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

        __sharedDataType = GetSharedComponentTypeHandle<SpriteRenderSharedData>();
        __instanceDataType = GetComponentTypeHandle<SpriteRenderInstanceData>(true);
        __localToWorldType = GetComponentTypeHandle<LocalToWorld>(true);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<SpriteRenderSharedData, SpriteRenderInstanceData, LocalToWorld>()
                .Build(this);
        
        __materials = new RenderAsset<Material>.Manager(Allocator.Persistent);
        __meshes = new RenderAsset<Mesh>.Manager(Allocator.Persistent);
        
        __mesh = GenerateQuad();

        __computeBuffer =
            new ComputeBuffer(MAX_INSTANCE_COUNT,
                TypeManager.GetTypeInfo<SpriteRenderInstanceData>().TypeSize, 
                ComputeBufferType.Constant, 
                ComputeBufferMode.Dynamic);
        
        __commandBuffer = new CommandBuffer();

        __matrices = new Matrix4x4[MAX_INSTANCE_COUNT];
    }

    protected override void OnDestroy()
    {
        __materials.Dispose();
        __meshes.Dispose();
        
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

        __localToWorldType.Update(this);
        __instanceDataType.Update(this);

        double time = SystemAPI.Time.ElapsedTime;
        using (var chunks = __group.ToArchetypeChunkArray(Allocator.Temp))
        {
            bool isComplete;
            int i, count, length;
            SpriteRenderSharedData sharedData;
            NativeArray<Matrix4x4> matrices;
            NativeArray<SpriteRenderInstanceData> instanceDatas;
            foreach (var chunk in chunks)
            {
                sharedData = chunk.GetSharedComponent(__sharedDataType);
                __materials.Retain(time, sharedData.material);

                isComplete = ObjectLoadingStatus.Completed == sharedData.material.value.LoadingStatus;

                if (sharedData.mesh.isCreated)
                {
                    __meshes.Retain(time, sharedData.mesh);
                    
                    isComplete &= ObjectLoadingStatus.Completed == sharedData.mesh.value.LoadingStatus;
                }

                if (isComplete)
                {
                    count = chunk.Count;
                    
                    instanceDatas = chunk.GetNativeArray(ref __instanceDataType);
                    matrices = chunk.GetNativeArray(ref __localToWorldType).Reinterpret<Matrix4x4>();

                    for (i = 0; i < count; i += MAX_INSTANCE_COUNT)
                    {
                        length = Mathf.Min(MAX_INSTANCE_COUNT, count - i);
                        __computeBuffer.SetData(instanceDatas.GetSubArray(i, length));

                        __commandBuffer.SetGlobalConstantBuffer(
                            __computeBuffer,
                            ConstantBufferID,
                            0,
                            length * TypeManager.GetTypeInfo<SpriteRenderInstanceData>().TypeSize);

                        NativeArray<Matrix4x4>.Copy(matrices.GetSubArray(i, length),
                            __matrices, length);

                        __commandBuffer.DrawMeshInstanced(
                            sharedData.mesh.isCreated ? sharedData.mesh.value.Result : __mesh,
                            sharedData.subMeshIndex,
                            sharedData.material.value.Result,
                            0,
                            __matrices,
                            length);
                    }
                }
            }
        }

        __materials.ReleaseTimeoutAssets(time);
        __meshes.ReleaseTimeoutAssets(time);
    }
}
