using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Plane = UnityEngine.Plane;

namespace ZG
{
    /// <summary>
    /// Represents frustum planes.
    /// </summary>
    public struct RenderFrustumPlanes : IComponentData
    {
        /// <summary>
        /// Options for an intersection result.
        /// </summary>
        public enum IntersectResult
        {
            /// <summary>
            /// The object is completely outside of the planes.
            /// </summary>
            Out,

            /// <summary>
            /// The object is completely inside of the planes.
            /// </summary>
            In,

            /// <summary>
            /// The object is partially intersecting the planes.
            /// </summary>
            Partial
        };

        private float4 __0;
        private float4 __1;
        private float4 __2;
        private float4 __3;
        private float4 __4;
        private float4 __5;

        public float4 this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:
                        return __0;
                    case 1:
                        return __1;
                    case 2:
                        return __2;
                    case 3:
                        return __3;
                    case 4:
                        return __4;
                    case 5:
                        return __5;
                }

                return default;
            }
        }

        private static readonly Plane[] Planes = new Plane[6];

        /// <summary>
        /// Populates the frustum plane array from the given camera frustum.
        /// </summary>
        /// <param name="camera">The camera to use for calculation.</param>
        public RenderFrustumPlanes(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, Planes);

            var cameraToWorld = camera.cameraToWorldMatrix;
            var eyePos = cameraToWorld.MultiplyPoint(Vector3.zero);
            var viewDir = new float3(cameraToWorld.m02, cameraToWorld.m12, cameraToWorld.m22);
            viewDir = -math.normalizesafe(viewDir);

            // Near Plane
            Planes[4].SetNormalAndPosition(viewDir, eyePos);
            Planes[4].distance -= camera.nearClipPlane;

            // Far plane
            Planes[5].SetNormalAndPosition(-viewDir, eyePos);
            Planes[5].distance += camera.farClipPlane;

            __0 = new float4(
                Planes[0].normal.x,
                Planes[0].normal.y,
                Planes[0].normal.z,
                Planes[0].distance);

            __1 = new float4(
                Planes[1].normal.x,
                Planes[1].normal.y,
                Planes[1].normal.z,
                Planes[1].distance);

            __2 = new float4(
                Planes[2].normal.x,
                Planes[2].normal.y,
                Planes[2].normal.z,
                Planes[2].distance);

            __3 = new float4(
                Planes[3].normal.x,
                Planes[3].normal.y,
                Planes[3].normal.z,
                Planes[3].distance);

            __4 = new float4(
                Planes[4].normal.x,
                Planes[4].normal.y,
                Planes[4].normal.z,
                Planes[4].distance);

            __5 = new float4(
                Planes[5].normal.x,
                Planes[5].normal.y,
                Planes[5].normal.z,
                Planes[5].distance);
        }

        public float DepthOf(in float3 point)
        {
            return (math.dot(__4.xyz, point) + __4.w) / -(__5.w + __4.w);
        }

        /// <summary>
        /// Performs an intersection test between an AABB and 6 culling planes.
        /// </summary>
        /// <param name="cullingPlanes">Planes to make the intersection.</param>
        /// <param name="a">Instance of the AABB to intersect.</param>
        /// <returns>Intersection result</returns>
        public IntersectResult Intersect(in float3 center, in float3 extents)
        {
            int inCount = 0;
            float dist, radius;
            float4 plane;
            for (int i = 0; i < 6; ++i)
            {
                plane = this[i];
                dist = math.dot(plane.xyz, center) + plane.w;
                radius = math.dot(extents, math.abs(plane.xyz));
                if (dist + radius <= 0)
                    return IntersectResult.Out;

                if (dist > radius)
                    ++inCount;
            }

            return (inCount == 6) ? IntersectResult.In : IntersectResult.Partial;
        }
    }

    public struct RenderList : IComponentData
    {
        public const int MAX_INSTANCE_COUNT = 1023;
        public const int MIN_COMPUTE_BUFFER_COUNT = MAX_INSTANCE_COUNT * 32;
        public static readonly Matrix4x4[] Matrices = new Matrix4x4[MAX_INSTANCE_COUNT];
        public static readonly Dictionary<int, List<ComputeBuffer>> ComputeBuffers = new Dictionary<int, List<ComputeBuffer>>();

        public readonly int InstanceID;
        
        private uint __constantTypeVersion;
        private int __constantTypeEntityCount;
        private int __sharedDataCount;
        private NativeHashMap<FixedString128Bytes, int> __bufferIDs;
        private NativeHashMap<int, int> __computeBufferStrideToIndices;
        private NativeList<int> __byteOffsets;
        
#if UNITY_WEBGL
        private NativeList<byte> __bytes;
#endif

        public static int ComputeCount(int sharedDataCount, int constantTypeEntityCount, int alignment, int stride)
        {
            int bytes = math.max(1, math.min(constantTypeEntityCount - 1, sharedDataCount)) * 
                        ((stride + alignment - 1) / alignment) * alignment;

            return (bytes + stride - 1) / stride + constantTypeEntityCount;
            //return constantTypeEntityCount + (sharedDataCount * alignment + stride - 1) / stride;
            //return math.max(constantTypeEntityCount, (alignment + stride - 1) / stride) * sharedDataCount;
        }

        public RenderList(int instanceID, in AllocatorManager.AllocatorHandle allocator)
        {
            InstanceID = instanceID;
            
            var computeBuffers = new List<ComputeBuffer>();
            ComputeBuffers[instanceID] = computeBuffers;
            /*ComputeBuffersHandle = 
                GCHandle.Alloc(
                    computeBuffers,
                    GCHandleType.Pinned)*/;

            __sharedDataCount = 0;
            __constantTypeEntityCount = 0;
            __constantTypeVersion = 0;
            __bufferIDs = new NativeHashMap<FixedString128Bytes, int>(1, allocator);
            __computeBufferStrideToIndices = new NativeHashMap<int, int>(1, allocator);
            __byteOffsets = new NativeList<int>(allocator);
            
#if UNITY_WEBGL
            __bytes = new NativeList<byte>(allocator);
#endif
        }

        public void Dispose()
        {
            var computeBuffers = __GetComputeBuffers();
            if (computeBuffers != null)
            {
                foreach (var computeBuffer in computeBuffers)
                    computeBuffer.Dispose();
            }

            ComputeBuffers.Remove(InstanceID);

            __bufferIDs.Dispose();
            __computeBufferStrideToIndices.Dispose();
            __byteOffsets.Dispose();
            
#if UNITY_WEBGL
            __bytes.Dispose();
#endif
        }

        public void Begin(
            int sharedDataCount, 
            int constantTypeEntityCount, 
            uint constantTypeVersion, 
            in NativeArray<RenderConstantType> constantTypes, 
            ref DynamicBuffer<RenderConstantBuffer> constantBuffers)
        {
            var computeBuffers = __GetComputeBuffers();
            if (computeBuffers == null || constantTypeEntityCount < 1)
                constantBuffers.Clear();
            else
            {
                ComputeBuffer computeBuffer;
                RenderConstantType constantType;
                int i, 
                    stride, 
                    computeBufferIndex, 
                    numConstantTypes = constantTypes.Length, 
                    alignment = SystemInfo.constantBufferOffsetAlignment;
                if (sharedDataCount > __sharedDataCount || 
                    constantTypeEntityCount > __constantTypeEntityCount ||
                    ChangeVersionUtility.DidChange(constantTypeVersion, __constantTypeVersion))
                {
                    __sharedDataCount = Mathf.Max(__sharedDataCount, sharedDataCount);
                    __constantTypeEntityCount = Mathf.Max(__constantTypeEntityCount, constantTypeEntityCount);
                    
                    __constantTypeVersion = constantTypeVersion;
                    
                    int count;
                    for (i = 0; i < numConstantTypes; ++i)
                    {
                        constantType = constantTypes[i];
                        stride = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(constantType.stableTypeHash)).TypeSize;
                        if(stride < 1)
                            continue;

                        count = ComputeCount(__sharedDataCount, __constantTypeEntityCount, alignment, stride);
                        count = Mathf.Max(count, MIN_COMPUTE_BUFFER_COUNT);
                        if (__computeBufferStrideToIndices.TryGetValue(stride, out computeBufferIndex))
                        {
                            computeBuffer = computeBuffers[computeBufferIndex];
                            if (computeBuffer.count < count)
                                computeBuffer.Dispose();
                            else
                                continue;
                        }
                        else
                        {
                            computeBufferIndex = computeBuffers.Count;

                            __computeBufferStrideToIndices[stride] = computeBufferIndex;
                        }

                        Debug.Log($"Create ComputeBuffer(Count: {count}, Stride: {stride})");
                        
                        computeBuffer = new ComputeBuffer(
                            count, 
                            stride, 
                            ComputeBufferType.Constant,
#if UNITY_WEBGL
                            ComputeBufferMode.Dynamic
#else
                            ComputeBufferMode.SubUpdates
#endif
                            );

                        if (computeBufferIndex < computeBuffers.Count)
                            computeBuffers[computeBufferIndex] = computeBuffer;
                        else
                            computeBuffers.Add(computeBuffer);
                    }
                }

                int numComputeBuffers = computeBuffers.Count;
                constantBuffers.Clear();
                constantBuffers.Resize(numConstantTypes + numComputeBuffers, NativeArrayOptions.ClearMemory);

                int byteCount;
#if UNITY_WEBGL
                __byteOffsets.ResizeUninitialized(numComputeBuffers << 1);

                for (i = 0; i < numComputeBuffers; ++i)
                    __byteOffsets[i + numComputeBuffers] = -1;

                byteCount = 0;
                for (i = 0; i < numConstantTypes; ++i)
                {
                    constantType = constantTypes[i];
                    stride = TypeManager
                        .GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(constantType.stableTypeHash)).TypeSize;
                    if (stride < 1)
                        continue;

                    computeBufferIndex = __computeBufferStrideToIndices[stride];
                    ref var byteOffset = ref __byteOffsets.ElementAt(computeBufferIndex + numComputeBuffers);
                    if (byteOffset < 0)
                    {
                        byteOffset = byteCount;
                        
                        byteCount += ComputeCount(sharedDataCount, constantTypeEntityCount, alignment, stride) * stride;
                    }
                }
                
                __bytes.ResizeUninitialized(byteCount);
#else
                __byteOffsets.ResizeUninitialized(numComputeBuffers);
#endif
                for (i = 0; i < numComputeBuffers; ++i)
                    __byteOffsets[i] = -1;

                int computeBufferOffset;
                NativeArray<byte> bytes;
                for (i = 0; i < numConstantTypes; ++i)
                {
                    constantType = constantTypes[i];
                    stride = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(constantType.stableTypeHash)).TypeSize;
                    if (stride < 1)
                    {
                        constantBuffers[i] = default;
                        
                        continue;
                    }

                    computeBufferIndex = __computeBufferStrideToIndices[stride];
                    computeBufferOffset = computeBufferIndex + numConstantTypes;
                    if (!constantBuffers[computeBufferOffset].isCreated)
                    {
                        byteCount = ComputeCount(sharedDataCount, constantTypeEntityCount, alignment, stride) * stride;
                        
#if UNITY_WEBGL
                        bytes = __bytes.AsArray().GetSubArray(__byteOffsets[computeBufferIndex + numComputeBuffers], byteCount);
#else 
                        computeBuffer = computeBuffers[computeBufferIndex];

                        bytes = computeBuffer.BeginWrite<byte>(
                            0, 
                            byteCount);
#endif

                        constantBuffers[computeBufferOffset] = new RenderConstantBuffer(
                            alignment,
                            stride, 
                            computeBufferIndex,
                            ref __byteOffsets,
                            ref bytes);
                    }

                    constantBuffers[i] = constantBuffers[computeBufferOffset];
                }
                constantBuffers.ResizeUninitialized(numConstantTypes);
            }
        }

        public void End()
        {
            var computeBuffers = __GetComputeBuffers();
            if (computeBuffers != null)
            {
                int numComputeBuffers = math.min(computeBuffers.Count, __byteOffsets.Length), byteOffset;
                for (int i = 0; i < numComputeBuffers; ++i)
                {
                    byteOffset = __byteOffsets[i];
                    if(byteOffset >= 0)
#if UNITY_WEBGL
                        computeBuffers[i].SetData(__bytes.AsArray().GetSubArray(__byteOffsets[i + numComputeBuffers], byteOffset));
#else
                        computeBuffers[i].EndWrite<byte>(byteOffset);
#endif
                }
            }
            
            __byteOffsets.Clear();
        }

        public void Apply(
            in NativeArray<RenderSharedData> sharedDatas, 
            in NativeArray<RenderConstantType> constantTypes, 
            in NativeArray<float4x4> localToWorlds,
            in NativeArray<RenderChunk> chunks,
            CommandBuffer commandBuffer)
        {
            End();
            
            var computeBuffers = __GetComputeBuffers();
            RenderSharedData sharedData;
            RenderConstantType constantType;
            int i, bufferID, count, stride, oldStride = 0, times = 0, offset = 0;
            foreach (var chunk in chunks)
            {
                for (i = 0; i < chunk.count; i += count)
                {
                    count = math.min(chunk.count - i, MAX_INSTANCE_COUNT);

                    if (chunk.constantTypeIndex != -1)
                    {
                        constantType = constantTypes[chunk.constantTypeIndex];
                        if (!__bufferIDs.TryGetValue(constantType.bufferName, out bufferID))
                        {
                            bufferID = Shader.PropertyToID(constantType.bufferName.ToString());
                            
                            __bufferIDs[constantType.bufferName] = bufferID;
                        }
                        
                        UnityEngine.Assertions.Assert.AreEqual(0, chunk.constantByteOffset % SystemInfo.constantBufferOffsetAlignment);
                        
                        stride = TypeManager.GetTypeInfo(TypeManager.GetTypeIndexFromStableTypeHash(constantType.stableTypeHash)).TypeSize;
                        commandBuffer.SetGlobalConstantBuffer(
                            computeBuffers[__computeBufferStrideToIndices[stride]],
                            bufferID,
                            chunk.constantByteOffset,
                            chunk.count * stride);

                        if (stride == 64 && oldStride != 64)
                        {
                            if (times > 0)
                                Debug.LogError("WTF???");
                            else
                                ++times;
                        }

                        oldStride = stride;
                    }

                    NativeArray<Matrix4x4>.Copy(
                        localToWorlds.Reinterpret<Matrix4x4>(),
                        offset,
                        Matrices,
                        0,
                        count);

                    offset += count;

                    sharedData = sharedDatas[chunk.sharedDataIndex];
                    commandBuffer.DrawMeshInstanced(
                        sharedData.mesh,
                        sharedData.subMeshIndex,
                        sharedData.material.Value,
                        0,
                        Matrices,
                        count);
                }
            }
        }

        /*public void Apply(
            in Entity entity, 
            ref EntityManager entityManager, 
            CommandBuffer commandBuffer)
        {
            var localToWorlds = entityManager.GetBuffer<RenderLocalToWorld>(entity, true);
            var chunks = entityManager.GetBuffer<RenderChunk>(entity, true);

            Apply(
                localToWorlds.AsNativeArray().Reinterpret<float4x4>(), 
                chunks.AsNativeArray(), 
                commandBuffer);
        }*/

        private List<ComputeBuffer> __GetComputeBuffers()
        {
            return ComputeBuffers[InstanceID];// //ComputeBuffersHandle.Target as List<ComputeBuffer>;
        }
    }

    public struct RenderChunk : IBufferElementData
    {
        public int sharedDataIndex;
        public int constantTypeIndex;
        public int constantByteOffset;
        public int count;
    }

    public struct RenderLocalToWorld : IBufferElementData
    {
        public float4x4 value;
    }

    public class RenderInstanceManager
    {
        private ComponentLookup<RenderFrustumPlanes> __frustumPlanes;
        private ComponentLookup<RenderList> __renderLists;
        private BufferLookup<RenderConstantBuffer> __constantBuffers;
        private BufferLookup<RenderChunk> __chunks;
        private BufferLookup<RenderLocalToWorld> __localToWorlds;
        private Camera[] __cameras;
        
        private readonly EntityArchetype __cameraEntityArchetype;
        private readonly SystemBase __system;
        private readonly Dictionary<object, Entity> __cameraEntities = new Dictionary<object, Entity>();
        private static readonly List<object> __camerasList = new List<object>();

        public bool isBegin
        {
            get;

            private set;
        }

        public RenderInstanceManager(SystemBase system)
        {
            __frustumPlanes = system.GetComponentLookup<RenderFrustumPlanes>();
            __renderLists = system.GetComponentLookup<RenderList>();
            __constantBuffers = system.GetBufferLookup<RenderConstantBuffer>();
            __chunks = system.GetBufferLookup<RenderChunk>();
            __localToWorlds = system.GetBufferLookup<RenderLocalToWorld>();

            var entityManager = system.EntityManager;
            __cameraEntityArchetype = entityManager.CreateArchetype(
                typeof(RenderFrustumPlanes), 
                typeof(RenderList), 
                typeof(RenderConstantBuffer), 
                typeof(RenderChunk),
                typeof(RenderLocalToWorld));

            entityManager.AddComponent<RenderSingleton>(system.SystemHandle);

            __system = system;
        }

        public void Dispose()
        {
            __renderLists.Update(__system);
            
            int numCameraEntities = __cameraEntities.Count;
            var entities = new NativeArray<Entity>(numCameraEntities, Allocator.Temp);
            foreach (var cameraEntity in __cameraEntities.Values)
            {
                __renderLists[cameraEntity].Dispose();

                entities[--numCameraEntities] = cameraEntity;
            }
            
            var entityManager = __system.EntityManager;
            entityManager.DestroyEntity(entities);
            entities.Dispose();
            
            entityManager.GetComponentData<RenderSingleton>(__system.SystemHandle).Dispose();
        }

        public void Begin(int constantTypeEntityCount)
        {
            if (isBegin)
                End();
            
            isBegin = true;

            var entityManager = __system.EntityManager;
            ref var singleton = ref entityManager.GetComponentDataRW<RenderSingleton>(__system.SystemHandle).ValueRW;
            singleton.Update(ref entityManager);
            var constantTypes = singleton.constantTypes.AsArray();
            uint constantTypeVersion = singleton.constantTypeVersion;
            
            int allCamerasCount = Camera.allCamerasCount;
            if (allCamerasCount < 1)
                return;
            
            if(allCamerasCount > (__cameras == null ? 0 : __cameras.Length))
                Array.Resize(ref __cameras, allCamerasCount);
            
            Camera.GetAllCameras(__cameras);

            NativeList<Entity> entities = default;
            
            __camerasList.Clear();

            int i;
            foreach (var cameraObject in __cameraEntities.Keys)
            {
                for (i = 0; i < allCamerasCount; ++i)
                {
                    if ((object)__cameras[i] == cameraObject)
                        break;
                }

                if (i < allCamerasCount)
                    continue;

                __camerasList.Add(cameraObject);
            }

            Entity entity;
            foreach (var cameraToDelete in __camerasList)
            {
                __cameraEntities.Remove(cameraToDelete, out entity);
                
                if (!entities.IsCreated)
                    entities = new NativeList<Entity>(Allocator.Temp);

                entities.Add(entity);
            }

            Camera camera;
            int entityCountToDestroy = entities.IsCreated ? entities.Length : 0, entityCountToCreate = 0;
            for (i = 0; i < allCamerasCount; ++i)
            {
                camera = __cameras[i];
                if(__cameraEntities.ContainsKey(camera))
                    continue;

                if (entityCountToDestroy-- > 0)
                    __cameraEntities[camera] = entities[entityCountToDestroy];

                ++entityCountToCreate;
            }

            if (entityCountToDestroy > 0)
            {
                __renderLists.Update(__system);

                foreach (var entityToDispose in entities)
                    __renderLists[entityToDispose].Dispose();

                entityManager.DestroyEntity(entities.AsArray());
            }

            if (entityCountToCreate > 0)
            {
                if (!entities.IsCreated)
                    entities = new NativeList<Entity>(entityCountToCreate, Allocator.Temp);
                
                entities.ResizeUninitialized(entityCountToCreate);
                
                entityManager.CreateEntity(__cameraEntityArchetype, entities.AsArray());
                
                __renderLists.Update(__system);
                for (i = 0; i < allCamerasCount; ++i)
                {
                    camera = __cameras[i];
                    if(__cameraEntities.ContainsKey(camera))
                        continue;

                    entity = entities[--entityCountToCreate];
                    __cameraEntities[camera] = entity;
                    
                    __renderLists[entity] = new RenderList(camera.GetInstanceID(), Allocator.Persistent);
                }
            }
            else
                __renderLists.Update(__system);

            if (entities.IsCreated)
                entities.Dispose();
            
            __constantBuffers.Update(__system);
            __chunks.Update(__system);
            __localToWorlds.Update(__system);
            __frustumPlanes.Update(__system);

            int sharedDataCount = math.max(singleton.queues.Length, 1) * singleton.sharedDatas.Length;
            DynamicBuffer<RenderConstantBuffer> constantBuffers;
            for (i = 0; i < allCamerasCount; ++i)
            {
                camera = __cameras[i];
                entity = __cameraEntities[camera];

                constantBuffers = __constantBuffers[entity];
                __renderLists.GetRefRW(entity).ValueRW.Begin(
                    sharedDataCount, 
                    constantTypeEntityCount, 
                    constantTypeVersion, 
                    constantTypes, 
                    ref constantBuffers);
                
                __chunks[entity].Clear();
                __localToWorlds[entity].Clear();
                __frustumPlanes[entity] = new RenderFrustumPlanes(camera);
            }
        }

        public void End()
        {
            UnityEngine.Assertions.Assert.IsTrue(isBegin);
            
            __system.EntityManager.CompleteDependencyBeforeRW<RenderConstantBuffer>();

            __renderLists.Update(__system);

            foreach (var cameraEntity in __cameraEntities.Values)
                __renderLists.GetRefRW(cameraEntity).ValueRW.End();
            
            isBegin = false;
        }

        public bool Apply(Camera camera, CommandBuffer commandBuffer)
        {
            if (!__cameraEntities.TryGetValue(camera, out Entity entity))
            {
                if (isBegin)
                    End();
                
                return false;
            }

            var entityManager = __system.EntityManager;
            entityManager.CompleteDependencyBeforeRO<RenderChunk>();
            entityManager.CompleteDependencyBeforeRO<RenderLocalToWorld>();
            
            if (isBegin)
                End();
            else
                __renderLists.Update(__system);

            __chunks.Update(__system);
            __localToWorlds.Update(__system);

            var singleton = entityManager.GetComponentData<RenderSingleton>(__system.SystemHandle);

            __renderLists.GetRefRW(entity).ValueRW.Apply(
                singleton.sharedDatas.AsArray(), 
                singleton.constantTypes.AsArray(), 
                __localToWorlds[entity].AsNativeArray().Reinterpret<float4x4>(), 
                __chunks[entity].AsNativeArray(), 
                commandBuffer);

            return true;
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
    public partial class RenderInstanceSystem : SystemBase
    {
        private EntityQuery __constantTypeGroup;
        private RenderInstanceManager __manager;

        public static bool Apply(Camera camera, CommandBuffer commandBuffer)
        {
            var system = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<RenderInstanceSystem>();
            if (system == null)
                return false;
            
            //if(system.__manager.isBegin)
            //    system.CompleteDependency();

            return system.__manager.Apply(camera, commandBuffer);
        }
        
        protected override void OnCreate()
        {
            base.OnCreate();

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __constantTypeGroup = builder
                    .WithAll<RenderConstantType>()
                    .Build(this);

            __manager = new RenderInstanceManager(this);
        }

        protected override void OnDestroy()
        {
            __manager.Dispose();
            
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            bool willCurrentFrameRender = OnDemandRendering.willCurrentFrameRender;

            RenderInstanceCullingSystem.WillCurrentFrameRender.Data = willCurrentFrameRender;

            if (willCurrentFrameRender)
            {
                int constantTypeEntityCount = __constantTypeGroup.CalculateEntityCount();
                __manager.Begin(constantTypeEntityCount);
            }
        }
    }
}