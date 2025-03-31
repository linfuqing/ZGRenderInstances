using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Content;
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

public struct RenderSharedData : ISharedComponentData
{
    public int subMeshIndex;
    public RenderAsset<Mesh> mesh;
    public RenderAsset<Material> material;
}

public struct RenderInstance : IComponentData
{
    
}

public static class RenderCommandBufferPool
{
    private static HashSet<CommandBuffer> __commandBuffers = new HashSet<CommandBuffer>();

    public static IReadOnlyCollection<CommandBuffer> commandBuffers => __commandBuffers;

    public static CommandBuffer Create()
    {
        var commandBuffer = new CommandBuffer();
        
        __commandBuffers.Add(commandBuffer);

        return commandBuffer;
    }

    public static bool Destroy(CommandBuffer commandBuffer)
    {
        if (!__commandBuffers.Remove(commandBuffer))
            return false;
        
        commandBuffer.Dispose();

        return true;
    }
}

[UpdateInGroup(typeof(PresentationSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Presentation | WorldSystemFilterFlags.Editor)]
public abstract partial class RenderInstancesSystem<T> : SystemBase where T : unmanaged, IComponentData
{
    private struct Comparer : IComparer<ArchetypeChunk>
    {
        public SharedComponentTypeHandle<RenderSharedData> sharedDataType;
        
        public int Compare(ArchetypeChunk x, ArchetypeChunk y)
        {
            return x.GetSharedComponentIndex(sharedDataType).CompareTo(y.GetSharedComponentIndex(sharedDataType));
        }
    }
    
    public const int MAX_INSTANCE_COUNT = 1024;
    
    private readonly ProfilingSampler __profilingSampler = new ProfilingSampler($"Render {nameof(T)}");
    
    private uint __version;
    private SharedComponentTypeHandle<RenderSharedData> __sharedDataType;
    private ComponentTypeHandle<T> __instanceDataType;
    private ComponentTypeHandle<LocalToWorld> __localToWorldType;
    private EntityQuery __group;
    private RenderAsset<Material>.Manager __materials;
    private RenderAsset<Mesh>.Manager __meshes;
    private ComputeBuffer __computeBuffer;
    private CommandBuffer __commandBuffer;
    private Matrix4x4[] __matrices;

    public abstract int constantBufferID
    {
        get;
    }

    public virtual Mesh mesh
    {
        get;
    }
    
    protected override void OnCreate()
    {
        base.OnCreate();

        __sharedDataType = GetSharedComponentTypeHandle<RenderSharedData>();
        __instanceDataType = GetComponentTypeHandle<T>(true);
        __localToWorldType = GetComponentTypeHandle<LocalToWorld>(true);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __group = builder
                .WithAll<T, RenderSharedData, LocalToWorld>()
                .Build(this);
        
        __materials = new RenderAsset<Material>.Manager(Allocator.Persistent);
        __meshes = new RenderAsset<Mesh>.Manager(Allocator.Persistent);
        
        if(constantBufferID != 0)
            __computeBuffer =
                new ComputeBuffer(MAX_INSTANCE_COUNT,
                    TypeManager.GetTypeInfo<T>().TypeSize, 
                    ComputeBufferType.Constant, 
                    ComputeBufferMode.Dynamic);
        
        __commandBuffer = RenderCommandBufferPool.Create();
        
        __matrices = new Matrix4x4[MAX_INSTANCE_COUNT];
    }

    protected override void OnDestroy()
    {
        __materials.Dispose();
        __meshes.Dispose();
        
        if(__computeBuffer != null)
            __computeBuffer.Dispose();

        RenderCommandBufferPool.Destroy(__commandBuffer);
        
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        uint version = (uint)__group.GetCombinedComponentOrderVersion(false);
        if (!ChangeVersionUtility.DidChange(version, __version))
            return;

        __version = version;

        using (new ProfilingScope(__commandBuffer, __profilingSampler))
        {
            __commandBuffer.Clear();

            __group.CompleteDependency();

            __localToWorldType.Update(this);
            __instanceDataType.Update(this);
            __sharedDataType.Update(this);

            double time = SystemAPI.Time.ElapsedTime;
            using (var chunks = __group.ToArchetypeChunkArray(Allocator.Temp))
            {
                Comparer comparer;
                comparer.sharedDataType = __sharedDataType;

                chunks.Sort(comparer);

                bool isComplete;
                int offset, count, length, sharedIndex, oldSharedIndex = -1, instanceCount = 0;
                RenderSharedData sharedData = default;
                NativeArray<Matrix4x4> matrices;
                NativeArray<T> instanceDatas;
                foreach (var chunk in chunks)
                {
                    sharedIndex = chunk.GetSharedComponentIndex(__sharedDataType);
                    if (sharedIndex != oldSharedIndex)
                    {
                        oldSharedIndex = sharedIndex;

                        if (instanceCount > 0)
                        {
                            __Draw(sharedData, instanceCount);

                            instanceCount = 0;
                        }
                    }

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
                        offset = 0;
                        count = chunk.Count;

                        instanceDatas = chunk.GetNativeArray(ref __instanceDataType);
                        matrices = chunk.GetNativeArray(ref __localToWorldType).Reinterpret<Matrix4x4>();

                        while (count > offset)
                        {
                            length = Mathf.Min(MAX_INSTANCE_COUNT - instanceCount, count - offset);
                            if (__computeBuffer != null)
                                __computeBuffer.SetData(instanceDatas.GetSubArray(offset, length),
                                    0,
                                    instanceCount,
                                    length);

                            NativeArray<Matrix4x4>.Copy(matrices, offset,
                                __matrices, instanceCount, length);

                            instanceCount += length;

                            if (instanceCount == MAX_INSTANCE_COUNT)
                            {
                                __Draw(sharedData, instanceCount);

                                instanceCount = 0;
                            }

                            offset += length;
                        }
                    }
                }

                if (instanceCount > 0)
                    __Draw(sharedData, instanceCount);
            }

            __materials.ReleaseTimeoutAssets(time);
            __meshes.ReleaseTimeoutAssets(time);
        }
    }

    private void __Draw(in RenderSharedData sharedData, int instanceCount)
    {
        if(__computeBuffer != null)
            __commandBuffer.SetGlobalConstantBuffer(
                __computeBuffer,
                constantBufferID,
                0,
                instanceCount * TypeManager.GetTypeInfo<T>().TypeSize);

        __commandBuffer.DrawMeshInstanced(
            sharedData.mesh.isCreated ? sharedData.mesh.value.Result : mesh,
            sharedData.subMeshIndex,
            sharedData.material.value.Result,
            0,
            __matrices,
            instanceCount);
    }
}

public partial class RenderInstancesSystem : RenderInstancesSystem<RenderInstance>
{
    public override int constantBufferID => 0;
}
