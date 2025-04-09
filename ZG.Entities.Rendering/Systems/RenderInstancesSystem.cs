using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

public struct RenderSortingOrder : IComponentData, IEquatable<RenderSortingOrder>
{
    public int value;
    
    public override int GetHashCode()
    {
        return value;
    }

    public bool Equals(RenderSortingOrder other)
    {
        return value == other.value;
    }
}

public struct RenderSharedData : ISharedComponentData, IEquatable<RenderSharedData>
{
    public int subMeshIndex;
    public UnityObjectRef<Mesh> mesh;
    public UnityObjectRef<Material> material;

    public override int GetHashCode()
    {
        return subMeshIndex ^ mesh.GetHashCode() ^ material.GetHashCode();
    }

    public bool Equals(RenderSharedData other)
    {
        return subMeshIndex == other.subMeshIndex && 
               mesh.Equals(other.mesh) &&
               material.Equals(other.material);
    }
}

public struct RenderInstance : IComponentData
{
    
}

public class RenderInstances<T> where T : unmanaged, IComponentData
{
    private struct Comparer : IComparer<ArchetypeChunk>
    {
        public ComponentTypeHandle<RenderSortingOrder> sortingOrderType;

        public SharedComponentTypeHandle<RenderSharedData> sharedDataType;
        
        public int Compare(ArchetypeChunk x, ArchetypeChunk y)
        {
            int sortingOrderX = x.HasChunkComponent<RenderSortingOrder>()
                ? x.GetChunkComponentData(ref sortingOrderType).value
                : 0, 
                sortingOrderY = y.HasChunkComponent<RenderSortingOrder>()
                    ? y.GetChunkComponentData(ref sortingOrderType).value
                    : 0;
            
            if(sortingOrderX == sortingOrderY)
                return x.GetSharedComponentIndex(sharedDataType).CompareTo(y.GetSharedComponentIndex(sharedDataType));
            
            return sortingOrderX.CompareTo(sortingOrderY);
        }
    }

    private ComputeBuffer __computeBuffer;
    private Matrix4x4[] __matrices;

    private readonly ProfilingSampler __profilingSampler = new ProfilingSampler($"Render Instances {nameof(T)}");
    
    public readonly int ConstantBufferID;

    public const int MAX_INSTANCE_COUNT = 1024;

    public RenderInstances(int constantBufferID)
    {
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
        if(__computeBuffer != null)
            __computeBuffer.Dispose();
    }

    public void Apply(
        ref ComponentTypeHandle<T> instanceDataType,
        ref ComponentTypeHandle<LocalToWorld> localToWorldType, 
        in ComponentTypeHandle<RenderSortingOrder> sortingOrderType, 
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
                comparer.sortingOrderType = sortingOrderType;
                comparer.sharedDataType = sharedDataType;

                chunks.Sort(comparer);

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

                if (instanceCount > 0)
                    __Draw(commandBuffer, mesh, sharedData, instanceCount);
            }
        }
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
            sharedData.mesh.IsValid() ? sharedData.mesh.Value : mesh,
            sharedData.subMeshIndex,
            sharedData.material.Value,
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
        public ComponentTypeHandle<RenderSortingOrder> sortingOrderType;

        [ReadOnly]
        public ComponentTypeHandle<T> instanceDataType;
        
        [ReadOnly]
        public ComponentTypeHandle<LocalToWorld> localToWorldType;

        public NativeArray<int> result;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!chunk.DidChange(sharedDataType, lastSystemVersion) &&
                !chunk.DidChange(ref sortingOrderType, lastSystemVersion) &&
                !chunk.DidChange(ref instanceDataType, lastSystemVersion) &&
                !chunk.DidChange(ref localToWorldType, lastSystemVersion))
                return;

            result[0] = 1;
        }
    }
    
    //public const int MAX_INSTANCE_COUNT = 1024;
    
    //private readonly ProfilingSampler __profilingSampler = new ProfilingSampler($"Render {nameof(T)}");
    
    private uint __staticVersion;
    private uint __dynamicVersion;
    private SharedComponentTypeHandle<RenderSharedData> __sharedDataType;
    private ComponentTypeHandle<RenderSortingOrder> __sortingOrderType;
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
        __sortingOrderType = GetComponentTypeHandle<RenderSortingOrder>(true);
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
        __sortingOrderType.Update(this);
        __sharedDataType.Update(this);

        double time = SystemAPI.Time.ElapsedTime;
        __Render(__staticCommandBuffer, __staticGroup, time, ref __staticVersion);
        __Render(__dynamicCommandBuffer, __dynamicGroup, time, ref __dynamicVersion);
    }

    private void __Render(CommandBuffer commandBuffer, in EntityQuery group, double time, ref uint version)
    {
        uint newVersion = (uint)group.GetCombinedComponentOrderVersion(false);
        
        bool isChanged = ChangeVersionUtility.DidChange(newVersion, version);

        version = newVersion;

        group.CompleteDependency();

        if (!isChanged)
        {
            using (var result = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory))
            {
                DidChange didChange;
                didChange.lastSystemVersion = LastSystemVersion;
                didChange.sharedDataType = __sharedDataType;
                didChange.sortingOrderType = __sortingOrderType;
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
            ref __instanceDataType, 
            ref __localToWorldType, 
            __sortingOrderType, 
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
