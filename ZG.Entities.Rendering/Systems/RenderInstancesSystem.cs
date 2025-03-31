using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

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

public struct RenderSharedData : ISharedComponentData, IEquatable<RenderSharedData>
{
    public int subMeshIndex;
    public RenderAsset<Mesh> mesh;
    public RenderAsset<Material> material;

    public override int GetHashCode()
    {
        return subMeshIndex ^ mesh.value.GetHashCode() ^ material.value.GetHashCode();
    }

    public bool Equals(RenderSharedData other)
    {
        return subMeshIndex == other.subMeshIndex && 
               mesh.value.Equals(other.mesh.value) &&
               material.value.Equals(other.material.value);
    }
}

public struct RenderInstance : IComponentData
{
    
}

public class RenderInstances<T> where T : unmanaged, IComponentData
{
    private struct Comparer : IComparer<ArchetypeChunk>
    {
        public SharedComponentTypeHandle<RenderSharedData> sharedDataType;
        
        public int Compare(ArchetypeChunk x, ArchetypeChunk y)
        {
            return x.GetSharedComponentIndex(sharedDataType).CompareTo(y.GetSharedComponentIndex(sharedDataType));
        }
    }

    private RenderAsset<Material>.Manager __materials;
    private RenderAsset<Mesh>.Manager __meshes;
    private ComputeBuffer __computeBuffer;
    private Matrix4x4[] __matrices;

    private readonly ProfilingSampler __profilingSampler = new ProfilingSampler($"Render Instances {nameof(T)}");
    
    public readonly int ConstantBufferID;

    public readonly int TypeSize;

    public const int MAX_INSTANCE_COUNT = 1024;

    public RenderInstances(int constantBufferID)
    {
        __materials = new RenderAsset<Material>.Manager(Allocator.Persistent);
        __meshes = new RenderAsset<Mesh>.Manager(Allocator.Persistent);
        
        if(constantBufferID != 0)
            __computeBuffer =
                new ComputeBuffer(MAX_INSTANCE_COUNT,
                    TypeManager.GetTypeInfo<T>().TypeSize, 
                    ComputeBufferType.Constant, 
                    ComputeBufferMode.Dynamic);
        
        __matrices = new Matrix4x4[MAX_INSTANCE_COUNT];

        ConstantBufferID = constantBufferID;
    }

    public void Dispose()
    {
        __materials.Dispose();
        __meshes.Dispose();
        
        if(__computeBuffer != null)
            __computeBuffer.Dispose();
    }

    public void Apply(
        double time, 
        ref ComponentTypeHandle<T> instanceDataType,
        ref ComponentTypeHandle<LocalToWorld> localToWorldType, 
        in SharedComponentTypeHandle<RenderSharedData> sharedDataType,
        in EntityQuery group, 
        CommandBuffer commandBuffer,
        Mesh mesh)
    {
        using (new ProfilingScope(commandBuffer, __profilingSampler))
        {
            commandBuffer.Clear();

            using (var chunks = group.ToArchetypeChunkArray(Allocator.Temp))
            {
                Comparer comparer;
                comparer.sharedDataType = sharedDataType;

                chunks.Sort(comparer);

                bool isComplete;
                int offset, count, length, sharedIndex, oldSharedIndex = -1, instanceCount = 0;
                RenderSharedData sharedData = default;
                NativeArray<Matrix4x4> matrices;
                NativeArray<T> instanceDatas;
                foreach (var chunk in chunks)
                {
                    sharedIndex = chunk.GetSharedComponentIndex(sharedDataType);
                    if (sharedIndex != oldSharedIndex)
                    {
                        oldSharedIndex = sharedIndex;

                        if (instanceCount > 0)
                        {
                            __Draw(commandBuffer, mesh, sharedData, instanceCount);

                            instanceCount = 0;
                        }
                    }

                    sharedData = chunk.GetSharedComponent(sharedDataType);

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

                        instanceDatas = chunk.GetNativeArray(ref instanceDataType);
                        matrices = chunk.GetNativeArray(ref localToWorldType).Reinterpret<Matrix4x4>();

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
                                __Draw(commandBuffer, mesh, sharedData, instanceCount);

                                instanceCount = 0;
                            }

                            offset += length;
                        }
                    }
                }

                if (instanceCount > 0)
                    __Draw(commandBuffer, mesh, sharedData, instanceCount);
            }
        }
    }

    public void ReleaseTimeoutAssets(double time)
    {
        __materials.ReleaseTimeoutAssets(time);
        __meshes.ReleaseTimeoutAssets(time);
    }

    private void __Draw(CommandBuffer commandBuffer, Mesh mesh, in RenderSharedData sharedData, int instanceCount)
    {
        if(__computeBuffer != null)
            commandBuffer.SetGlobalConstantBuffer(
                __computeBuffer,
                ConstantBufferID,
                0,
                instanceCount * TypeManager.GetTypeInfo<T>().TypeSize);

        commandBuffer.DrawMeshInstanced(
            sharedData.mesh.isCreated ? sharedData.mesh.value.Result : mesh,
            sharedData.subMeshIndex,
            sharedData.material.value.Result,
            0,
            __matrices,
            instanceCount);
    }
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
    [BurstCompile]
    private struct DidChange : IJobChunk
    {
        public uint lastSystemVersion;
        
        [ReadOnly]
        public SharedComponentTypeHandle<RenderSharedData> sharedDataType;
        
        [ReadOnly]
        public ComponentTypeHandle<T> instanceDataType;
        
        [ReadOnly]
        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        public NativeArray<int> result;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!chunk.DidChange(sharedDataType, lastSystemVersion) &&
                !chunk.DidChange(ref instanceDataType, lastSystemVersion) &&
                !chunk.DidChange(ref localToWorldType, lastSystemVersion))
                return;

            result[0] = 1;
        }
    }
    
    public const int MAX_INSTANCE_COUNT = 1024;
    
    private readonly ProfilingSampler __profilingSampler = new ProfilingSampler($"Render {nameof(T)}");
    
    private uint __staticVersion;
    private uint __dynamicVersion;
    private SharedComponentTypeHandle<RenderSharedData> __sharedDataType;
    private ComponentTypeHandle<T> __instanceDataType;
    private ComponentTypeHandle<LocalToWorld> __localToWorldType;
    private EntityQuery __staticGroup;
    private EntityQuery __dynamicGroup;
    private CommandBuffer __staticCommandBuffer;
    private CommandBuffer __dynamicCommandBuffer;
    private RenderInstances<T> __renderInstances;

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
            __staticGroup = builder
                .WithAll<T, RenderSharedData, LocalToWorld, Static>()
                .Build(this);
        
        using (var builder = new EntityQueryBuilder(Allocator.Temp))
            __dynamicGroup = builder
                .WithAll<T, RenderSharedData, LocalToWorld>()
                .WithNone<Static>()
                .Build(this);

        __staticCommandBuffer = RenderCommandBufferPool.Create();
        __dynamicCommandBuffer = RenderCommandBufferPool.Create();

        __renderInstances = new RenderInstances<T>(constantBufferID);
    }

    protected override void OnDestroy()
    {
        RenderCommandBufferPool.Destroy(__staticCommandBuffer);
        RenderCommandBufferPool.Destroy(__dynamicCommandBuffer);
        
        __renderInstances.Dispose();
        
        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        __localToWorldType.Update(this);
        __instanceDataType.Update(this);
        __sharedDataType.Update(this);

        double time = SystemAPI.Time.ElapsedTime;
        __Render(__staticCommandBuffer, __staticGroup, time, ref __staticVersion);
        __Render(__dynamicCommandBuffer, __dynamicGroup, time, ref __dynamicVersion);
        
        __renderInstances.ReleaseTimeoutAssets(time);
    }

    private void __Render(CommandBuffer commandBuffer, in EntityQuery group, double time, ref uint version)
    {
        uint newVersion = (uint)group.GetCombinedComponentOrderVersion(false);
        
        bool isChanged = ChangeVersionUtility.DidChange(newVersion, version);

        version = newVersion;

        group.CompleteDependency();

        if (!isChanged)
        {
            using (var result = new NativeArray<int>(1, Allocator.Temp, NativeArrayOptions.ClearMemory))
            {
                DidChange didChange;
                didChange.lastSystemVersion = LastSystemVersion;
                didChange.sharedDataType = __sharedDataType;
                didChange.instanceDataType = __instanceDataType;
                didChange.localToWorldType = __localToWorldType;
                didChange.result = result;
                didChange.RunByRef(group);

                isChanged = result[0] != 0;
                
                if (!isChanged)
                    return;
            }
        }

        __renderInstances.Apply(
            time, 
            ref __instanceDataType, 
            ref __localToWorldType, 
            __sharedDataType, 
            group,
            commandBuffer, 
            mesh);
    }
}

public partial class RenderInstancesSystem : RenderInstancesSystem<RenderInstance>
{
    public override int constantBufferID => 0;
}
