using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using UnityEngine;
using UnityEngine.Rendering;
using Plane = UnityEngine.Plane;

namespace ZG
{
    /// <summary>
    /// Represents frustum planes.
    /// </summary>
    public readonly struct RenderFrustumPlanes : IComponentData
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

        private readonly float4 __0;
        private readonly float4 __1;
        private readonly float4 __2;
        private readonly float4 __3;
        private readonly float4 __4;
        private readonly float4 __5;

        public readonly MinMaxAABB AABB;

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
            
            float ncp = camera.nearClipPlane;
            
            Planes[4].distance -= ncp;

            // Far plane
            Planes[5].SetNormalAndPosition(-viewDir, eyePos);
            
            float fcp = camera.farClipPlane;  

            Planes[5].distance += fcp;

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
            
            float yf = math.tan(camera.fieldOfView/2 * Mathf.Deg2Rad), xf = yf * camera.aspect;
            Matrix4x4 localToWorld = camera.transform.localToWorldMatrix;
            Vector3 f0 = localToWorld * new Vector3(-xf, -yf, 1), 
                f1 = localToWorld * new Vector3(-xf,  yf, 1), 
                f2 = localToWorld * new Vector3( xf, -yf, 1), 
                f3 = localToWorld * new Vector3( xf,  yf, 1), 
                cpt = localToWorld.GetPosition(), 
                farLeftBottom = cpt + fcp * f0, 
                farLeftTop = cpt + fcp * f1, 
                farRightBottom = cpt + fcp * f2, 
                farRightTop = cpt + fcp * f3, 
                nearLeftBottom = cpt + ncp * f0, 
                nearLeftTop = cpt + ncp * f1, 
                nearRightBottom = cpt + ncp * f2, 
                nearRightTop = cpt + ncp * f3; 
            
            AABB = MinMaxAABB.CreateFromCenterAndExtents(
                (float3)(farLeftBottom + farLeftTop + farRightBottom + farRightTop + nearLeftBottom + nearLeftTop + nearRightBottom + nearRightTop) / 8.0f, 
                float3.zero);
            
            AABB.Encapsulate(farLeftBottom);
            AABB.Encapsulate(farLeftTop);
            AABB.Encapsulate(farRightBottom);
            AABB.Encapsulate(farRightTop);
            AABB.Encapsulate(nearLeftBottom);
            AABB.Encapsulate(nearLeftTop);
            AABB.Encapsulate(nearRightBottom);
            AABB.Encapsulate(nearRightTop);
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
        //https://discussions.unity.com/t/gpu-instancing-limited-to-128-per-call-on-adreno-540/737547
#if UNITY_WEBGL
        // Unity's WebGL instancing variants use 32 entries per instancing
        // draw batch in a standard browser.
        public static readonly int MaxInstanceCount = 32;
#else
        public static readonly int MaxInstanceCount = 64;//SystemInfo.maxConstantBufferSize / 128;
#endif
        public static readonly int MinComputeBufferCount = MaxInstanceCount * 4;
        public static readonly Matrix4x4[] Matrices = new Matrix4x4[MaxInstanceCount];
        public static readonly Dictionary<int, List<ComputeBuffer>> ComputeBuffers = new Dictionary<int, List<ComputeBuffer>>();

        public readonly int InstanceID;
        
        private uint __constantTypeVersion;
        private int __constantBufferByteSize;
        private int __constantBufferOffsetAlignment;
        private int __constantTypeEntityCount;
        private int __sharedDataCount;
        private NativeHashMap<FixedString128Bytes, int> __bufferIDs;
        private NativeHashMap<int, int> __computeBufferStrideToIndices;
        private NativeList<int> __byteOffsets;
        private NativeList<byte> __bytes;

        private static int __GreatestCommonDivisor(int x, int y)
        {
            while (y != 0)
            {
                int remainder = x % y;
                x = y;
                y = remainder;
            }

            return x;
        }

        /*public static int ComputeCount(
            int sharedDataCount,
            int constantTypeEntityCount,
            int alignment,
            int stride)
        {
            return ComputeCount(
                sharedDataCount,
                constantTypeEntityCount,
                1,
                alignment,
                stride);
        }*/

        public static int ComputeCount(
            int sharedDataCount,
            int constantTypeEntityCount,
            int constantTypeCountForStride,
            int alignment,
            int stride)
        {
            if (constantTypeEntityCount < 1 || stride < 1)
                return 0;

            alignment = System.Math.Max(alignment, 1);
            sharedDataCount = System.Math.Max(sharedDataCount, 1);
            constantTypeCountForStride = System.Math.Max(constantTypeCountForStride, 1);

            long segmentUpperBound = System.Math.Min(
                    (long)constantTypeEntityCount,
                    (long)sharedDataCount * constantTypeCountForStride),
                maximumPadding = alignment - __GreatestCommonDivisor(alignment, stride),
                byteCount = checked(
                    (long)constantTypeEntityCount * stride +
                    (segmentUpperBound - 1L) * maximumPadding),
                count = checked((byteCount + stride - 1L) / stride);

            return checked((int)count);
        }

        public static int GetConstantBufferByteSize()
        {
            return SystemInfo.maxConstantBufferSize;
        }

        public static int ComputeBindingCapacityCount(
            int constantBufferByteSize,
            int stride)
        {
            if (constantBufferByteSize < 1 || stride < 1)
                return 0;

            return checked((int)(
                ((long)constantBufferByteSize + stride - 1L) /
                stride));
        }

        public static int ComputeRangeTailCount(
            int constantBufferByteSize,
            int stride)
        {
            return math.max(
                ComputeBindingCapacityCount(
                    constantBufferByteSize,
                    stride) -
                1,
                0);
        }

        public static int ComputeBufferCapacityCount(
            int sharedDataCount,
            int constantTypeEntityCount,
            int constantTypeCountForStride,
            int constantBufferByteSize,
            int alignment,
            int stride)
        {
            if (alignment < 1)
                return ComputeBindingCapacityCount(
                    constantBufferByteSize,
                    stride);

            int count = ComputeCount(
                sharedDataCount,
                constantTypeEntityCount,
                constantTypeCountForStride,
                alignment,
                stride);
            if (count < 1)
                return 0;

            return checked(
                count +
                ComputeRangeTailCount(
                    constantBufferByteSize,
                    stride));
        }

        public static int ComputeMaxInstanceCount(
            int constantBufferByteSize,
            int constantBufferOffsetAlignment,
            int stride)
        {
            if (constantBufferByteSize < 1 || stride < 1)
                return 0;

            int count = math.min(
                MaxInstanceCount,
                constantBufferByteSize / stride);
            if (constantBufferOffsetAlignment < 1)
                return count;

            int countAlignment =
                constantBufferOffsetAlignment /
                __GreatestCommonDivisor(
                    constantBufferOffsetAlignment,
                    stride);
            return count / countAlignment * countAlignment;
        }

        public static int ComputeBindingByteOffset(
            int constantBufferOffsetAlignment,
            int sourceByteOffset)
        {
            return constantBufferOffsetAlignment > 0 ?
                sourceByteOffset :
                0;
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
            __constantBufferByteSize = 0;
            __constantBufferOffsetAlignment = 0;
            __bufferIDs = new NativeHashMap<FixedString128Bytes, int>(1, allocator);
            __computeBufferStrideToIndices = new NativeHashMap<int, int>(1, allocator);
            __byteOffsets = new NativeList<int>(allocator);
            __bytes = new NativeList<byte>(allocator);
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
            __bytes.Dispose();
        }

        public void Begin(
            int sharedDataCount, 
            int constantTypeEntityCount, 
            uint constantTypeVersion,
            in NativeArray<RenderConstantType> constantTypes,
            in NativeHashMap<int, int> constantTypeCountsByStride,
            ref DynamicBuffer<RenderConstantBuffer> constantBuffers)
        {
            End();

            var computeBuffers = __GetComputeBuffers();
            if (computeBuffers == null || constantTypeEntityCount < 1)
            {
                constantBuffers.Clear();
                return;
            }

            int constantBufferByteSize = GetConstantBufferByteSize();
            if (!SystemInfo.supportsSetConstantBuffer ||
                constantBufferByteSize < 1)
                throw new NotSupportedException(
                    "The current graphics device does not support constant buffer bindings.");

            int constantBufferOffsetAlignment =
                    SystemInfo.constantBufferOffsetAlignment,
                writeAlignment =
                    math.max(constantBufferOffsetAlignment, 1);
            bool useConstantBufferRange =
                    constantBufferOffsetAlignment > 0,
                useStagingBytes = !useConstantBufferRange;
#if UNITY_WEBGL
            useStagingBytes = true;
#endif

            bool bindingModeChanged =
                constantBufferOffsetAlignment !=
                __constantBufferOffsetAlignment;
            ComputeBuffer computeBuffer;
            RenderConstantType constantType;
            int i,
                stride,
                constantTypeCountForStride,
                computeBufferIndex,
                numConstantTypes = constantTypes.Length;
            if (sharedDataCount > __sharedDataCount ||
                constantTypeEntityCount > __constantTypeEntityCount ||
                ChangeVersionUtility.DidChange(
                    constantTypeVersion,
                    __constantTypeVersion) ||
                constantBufferByteSize != __constantBufferByteSize ||
                bindingModeChanged)
            {
                __sharedDataCount =
                    math.max(__sharedDataCount, sharedDataCount);
                __constantTypeEntityCount =
                    math.max(
                        __constantTypeEntityCount,
                        constantTypeEntityCount);
                __constantTypeVersion = constantTypeVersion;
                __constantBufferByteSize = constantBufferByteSize;
                __constantBufferOffsetAlignment =
                    constantBufferOffsetAlignment;

                int count;
                bool isComputeBufferValid;
                ComputeBufferMode computeBufferMode;
                for (i = 0; i < numConstantTypes; ++i)
                {
                    constantType = constantTypes[i];
                    stride = TypeManager
                        .GetTypeInfo(
                            TypeManager.GetTypeIndexFromStableTypeHash(
                                constantType.stableTypeHash))
                        .TypeSize;
                    if (stride < 1)
                        continue;

                    if (constantBufferByteSize < stride)
                        throw new NotSupportedException(
                            $"Constant buffer size {constantBufferByteSize} is smaller than stride {stride}.");

                    constantTypeCountsByStride.TryGetValue(
                        stride,
                        out constantTypeCountForStride);
                    count = ComputeBufferCapacityCount(
                        __sharedDataCount,
                        __constantTypeEntityCount,
                        constantTypeCountForStride,
                        constantBufferByteSize,
                        constantBufferOffsetAlignment,
                        stride);
                    if (useConstantBufferRange)
                    {
                        count = math.ceilpow2(count);
                        count = math.max(count, MinComputeBufferCount);
                    }

                    if (__computeBufferStrideToIndices.TryGetValue(
                            stride,
                            out computeBufferIndex))
                    {
                        computeBuffer = computeBuffers[computeBufferIndex];
                        isComputeBufferValid =
                            !bindingModeChanged &&
                            (useConstantBufferRange ?
                                computeBuffer.count >= count :
                                computeBuffer.count == count);
                        if (isComputeBufferValid)
                            continue;

                        computeBuffer.Dispose();
                    }
                    else
                    {
                        computeBufferIndex = computeBuffers.Count;
                        __computeBufferStrideToIndices[stride] =
                            computeBufferIndex;
                    }

#if UNITY_WEBGL
                    computeBufferMode = ComputeBufferMode.Dynamic;
#else
                    computeBufferMode = useConstantBufferRange ?
                        ComputeBufferMode.SubUpdates :
                        ComputeBufferMode.Dynamic;
#endif
                    Debug.Log(
                        $"Create ComputeBuffer(Count: {count}, Stride: {stride}, Alignment: {constantBufferOffsetAlignment}, Constant Type Entity Count: {__constantTypeEntityCount}, Shared Data Count: {__sharedDataCount}, Max Instance Count: {MaxInstanceCount}, Constant Buffer Byte Size: {constantBufferByteSize})");

                    computeBuffer = new ComputeBuffer(
                        count,
                        stride,
                        ComputeBufferType.Constant,
                        computeBufferMode);

                    if (computeBufferIndex < computeBuffers.Count)
                        computeBuffers[computeBufferIndex] = computeBuffer;
                    else
                        computeBuffers.Add(computeBuffer);
                }
            }

            int numComputeBuffers = computeBuffers.Count;
            constantBuffers.Clear();
            constantBuffers.ResizeUninitialized(
                numConstantTypes + numComputeBuffers);

            for (i = 0; i < numComputeBuffers; ++i)
                constantBuffers[i + numConstantTypes] = default;

            int byteCount,
                byteCountIndex = numComputeBuffers,
                baseByteOffsetIndex = numComputeBuffers << 1;
            __byteOffsets.ResizeUninitialized(numComputeBuffers * 3);
            for (i = 0; i < numComputeBuffers; ++i)
            {
                __byteOffsets[i] = -1;
                __byteOffsets[i + byteCountIndex] = 0;
                __byteOffsets[i + baseByteOffsetIndex] = -1;
            }

            if (useStagingBytes)
            {
                byteCount = 0;
                for (i = 0; i < numConstantTypes; ++i)
                {
                    constantType = constantTypes[i];
                    stride = TypeManager
                        .GetTypeInfo(
                            TypeManager.GetTypeIndexFromStableTypeHash(
                                constantType.stableTypeHash))
                        .TypeSize;
                    if (stride < 1)
                        continue;

                    computeBufferIndex =
                        __computeBufferStrideToIndices[stride];
                    ref var currentBaseByteOffset =
                        ref __byteOffsets.ElementAt(
                            computeBufferIndex +
                            baseByteOffsetIndex);
                    if (currentBaseByteOffset >= 0)
                        continue;

                    currentBaseByteOffset = byteCount;
                    ref var currentByteCount =
                        ref __byteOffsets.ElementAt(
                            computeBufferIndex +
                            byteCountIndex);

                    constantTypeCountsByStride.TryGetValue(
                        stride,
                        out constantTypeCountForStride);
                    currentByteCount =
                        ComputeCount(
                            sharedDataCount,
                            constantTypeEntityCount,
                            constantTypeCountForStride,
                            writeAlignment,
                            stride) *
                        stride;
                    byteCount += currentByteCount;
                }

                __bytes.ResizeUninitialized(byteCount);
            }
            else
                __bytes.Clear();

            int baseByteOffset,
                computeBufferOffset;
            NativeArray<byte> bytes;
            for (i = 0; i < numConstantTypes; ++i)
            {
                constantType = constantTypes[i];
                stride = TypeManager
                    .GetTypeInfo(
                        TypeManager.GetTypeIndexFromStableTypeHash(
                            constantType.stableTypeHash))
                    .TypeSize;
                if (stride < 1)
                {
                    constantBuffers[i] = default;
                    continue;
                }

                computeBufferIndex =
                    __computeBufferStrideToIndices[stride];
                computeBufferOffset =
                    computeBufferIndex + numConstantTypes;
                if (!constantBuffers[computeBufferOffset].isCreated)
                {
                    if (useStagingBytes)
                    {
                        byteCount =
                            __byteOffsets[
                                computeBufferIndex +
                                byteCountIndex];
                        baseByteOffset =
                            __byteOffsets[
                                computeBufferIndex +
                                baseByteOffsetIndex];
                        bytes = __bytes.AsArray().GetSubArray(
                            baseByteOffset,
                            byteCount);
                        if (useConstantBufferRange)
                            baseByteOffset = 0;
                    }
                    else
                    {
                        constantTypeCountsByStride.TryGetValue(
                            stride,
                            out constantTypeCountForStride);
                        byteCount =
                            ComputeCount(
                                sharedDataCount,
                                constantTypeEntityCount,
                                constantTypeCountForStride,
                                writeAlignment,
                                stride) *
                            stride;
                        __byteOffsets[
                            computeBufferIndex +
                            byteCountIndex] = byteCount;

                        computeBuffer =
                            computeBuffers[computeBufferIndex];
                        bytes = computeBuffer.BeginWrite<byte>(
                            0,
                            byteCount);
                        baseByteOffset = 0;
                    }

                    constantBuffers[computeBufferOffset] =
                        new RenderConstantBuffer(
                            writeAlignment,
                            stride,
                            computeBufferIndex,
                            baseByteOffset,
                            ref __byteOffsets,
                            ref bytes);
                }

                constantBuffers[i] =
                    constantBuffers[computeBufferOffset];
            }

            constantBuffers.ResizeUninitialized(numConstantTypes);
        }

        public void End()
        {
            if (__constantBufferOffsetAlignment < 1)
                return;

            var computeBuffers = __GetComputeBuffers();
            if (computeBuffers != null)
            {
                int numComputeBuffers = math.min(computeBuffers.Count, __byteOffsets.Length), 
                    byteOffset;
                for (int i = 0; i < numComputeBuffers; ++i)
                {
                    byteOffset = __byteOffsets[i];
                    if (byteOffset >= 0)
                    {
                        byteOffset = math.min(byteOffset,
                            __byteOffsets[i + numComputeBuffers]);

#if UNITY_WEBGL
                        computeBuffers[i].SetData(__bytes.AsArray()
                            .GetSubArray(__byteOffsets[i + (numComputeBuffers << 1)], byteOffset));
#else
                        computeBuffers[i].EndWrite<byte>(byteOffset);
#endif
                    }
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
            ComputeBuffer computeBuffer = null;
            RenderSharedData sharedData;
            RenderConstantType constantType;
            int i,
                count,
                stride = 0,
                offset = 0,
                bufferID = 0,
                maxInstanceCount = MaxInstanceCount,
                constantTypeIndex = -1;
            foreach (var chunk in chunks)
            {
                if (chunk.constantTypeIndex != -1 &&
                    chunk.constantTypeIndex != constantTypeIndex)
                {
                    constantTypeIndex = chunk.constantTypeIndex;

                    constantType = constantTypes[constantTypeIndex];
                    if (!__bufferIDs.TryGetValue(
                            constantType.bufferName,
                            out bufferID))
                    {
                        bufferID = Shader.PropertyToID(
                            constantType.bufferName.ToString());
                        __bufferIDs[constantType.bufferName] =
                            bufferID;
                    }

                    stride = TypeManager
                        .GetTypeInfo(
                            TypeManager.GetTypeIndexFromStableTypeHash(
                                constantType.stableTypeHash))
                        .TypeSize;
                    maxInstanceCount = ComputeMaxInstanceCount(
                        __constantBufferByteSize,
                        __constantBufferOffsetAlignment,
                        stride);
                    if (maxInstanceCount < 1)
                        throw new NotSupportedException(
                            $"Constant buffer size {__constantBufferByteSize} is smaller than stride {stride}.");

                    computeBuffer =
                        computeBuffers[
                            __computeBufferStrideToIndices[stride]];
                }

                for (i = 0; i < chunk.count; i += count)
                {
                    count = math.min(
                        chunk.count - i,
                        chunk.constantTypeIndex == -1 ?
                            MaxInstanceCount :
                            maxInstanceCount);

                    if (chunk.constantTypeIndex != -1)
                    {
                        UnityEngine.Assertions.Assert.AreEqual(stride, computeBuffer.stride);

                        int sourceByteOffset =
                                chunk.constantByteOffset + i * stride,
                            populatedByteSize = count * stride,
                            constantBufferByteOffset =
                                ComputeBindingByteOffset(
                                    __constantBufferOffsetAlignment,
                                    sourceByteOffset);
                        UnityEngine.Assertions.Assert.IsFalse(
                            populatedByteSize >
                            __constantBufferByteSize);
                        if (__constantBufferOffsetAlignment < 1)
                        {
                            UnityEngine.Assertions.Assert.IsFalse(
                                sourceByteOffset +
                                populatedByteSize >
                                __bytes.Length);
                            commandBuffer.SetBufferData(
                                computeBuffer,
                                __bytes.AsArray(),
                                sourceByteOffset,
                                0,
                                populatedByteSize);
                        }
                        else
                            UnityEngine.Assertions.Assert.AreEqual(
                                0,
                                constantBufferByteOffset %
                                __constantBufferOffsetAlignment);

                        UnityEngine.Assertions.Assert.IsFalse(
                            constantBufferByteOffset +
                            __constantBufferByteSize >
                            computeBuffer.count * stride);

                        commandBuffer.SetGlobalConstantBuffer(
                            computeBuffer,
                            bufferID,
                            constantBufferByteOffset,
                            __constantBufferByteSize);
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
        private uint __constantTypeCountsVersion;
        private NativeHashMap<int, int> __constantTypeCountsByStride;
        private Camera[] __cameras;
        
        private readonly EntityArchetype __cameraEntityArchetype;
        private readonly SystemBase __system;
        private readonly Dictionary<int, Entity> __cameraEntities = new Dictionary<int, Entity>();
        private static readonly List<int> __camerasInstanceIDs = new List<int>();

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
            __constantTypeCountsByStride =
                new NativeHashMap<int, int>(1, Allocator.Persistent);

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

            __constantTypeCountsByStride.Dispose();
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
            if (ChangeVersionUtility.DidChange(
                    constantTypeVersion,
                    __constantTypeCountsVersion))
            {
                __constantTypeCountsByStride.Clear();

                int numConstantTypes = constantTypes.Length;
                if (__constantTypeCountsByStride.Capacity < numConstantTypes)
                    __constantTypeCountsByStride.Capacity = numConstantTypes;

                int stride, constantTypeCountForStride;
                for (int constantTypeIndex = 0;
                     constantTypeIndex < numConstantTypes;
                     ++constantTypeIndex)
                {
                    stride = TypeManager
                        .GetTypeInfo(
                            TypeManager.GetTypeIndexFromStableTypeHash(
                                constantTypes[constantTypeIndex].stableTypeHash))
                        .TypeSize;
                    if (stride < 1)
                        continue;

                    __constantTypeCountsByStride.TryGetValue(
                        stride,
                        out constantTypeCountForStride);
                    __constantTypeCountsByStride[stride] =
                        constantTypeCountForStride + 1;
                }

                __constantTypeCountsVersion = constantTypeVersion;
            }
            
            int allCamerasCount = Camera.allCamerasCount;
            if (allCamerasCount < 1)
                return;
            
            if(allCamerasCount > (__cameras == null ? 0 : __cameras.Length))
                Array.Resize(ref __cameras, allCamerasCount);
            
            Camera.GetAllCameras(__cameras);

            NativeList<Entity> entities = default;
            
            __camerasInstanceIDs.Clear();

            int i;
            foreach (var cameraInstanceID in __cameraEntities.Keys)
            {
                for (i = 0; i < allCamerasCount; ++i)
                {
                    if (__cameras[i].GetInstanceID() == cameraInstanceID)
                        break;
                }

                if (i < allCamerasCount)
                    continue;

                __camerasInstanceIDs.Add(cameraInstanceID);
            }

            Entity entity;
            foreach (var camerasInstanceID in __camerasInstanceIDs)
            {
                __cameraEntities.Remove(camerasInstanceID, out entity);
                
                if (!entities.IsCreated)
                    entities = new NativeList<Entity>(Allocator.Temp);

                entities.Add(entity);
            }

            Camera camera;
            int instanceID, entityCountToDestroy = entities.IsCreated ? entities.Length : 0, entityCountToCreate = 0;
            for (i = 0; i < allCamerasCount; ++i)
            {
                camera = __cameras[i];
                instanceID = camera.GetInstanceID();
                if(__cameraEntities.ContainsKey(instanceID))
                    continue;

                if (entityCountToDestroy-- > 0)
                    __cameraEntities[instanceID] = entities[entityCountToDestroy];
                else
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
                    instanceID = camera.GetInstanceID();
                    if(__cameraEntities.ContainsKey(instanceID))
                        continue;

                    entity = entities[--entityCountToCreate];
                    __cameraEntities[instanceID] = entity;
                    
                    __renderLists[entity] = new RenderList(instanceID, Allocator.Persistent);
                }
                
                UnityEngine.Assertions.Assert.AreEqual(0, entityCountToCreate);
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
                instanceID = camera.GetInstanceID();
                entity = __cameraEntities[instanceID];

                constantBuffers = __constantBuffers[entity];
                __renderLists.GetRefRW(entity).ValueRW.Begin(
                    sharedDataCount, 
                    constantTypeEntityCount, 
                    constantTypeVersion,
                    constantTypes,
                    __constantTypeCountsByStride,
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

        public bool Apply(int cameraInstanceID, CommandBuffer commandBuffer)
        {
            if (!__cameraEntities.TryGetValue(cameraInstanceID, out Entity entity))
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

        public static bool Apply(int cameraInstanceID, CommandBuffer commandBuffer)
        {
            var system = World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<RenderInstanceSystem>();
            if (system == null)
                return false;
            
            //if(system.__manager.isBegin)
            //    system.CompleteDependency();

            return system.__manager.Apply(cameraInstanceID, commandBuffer);
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
                CompleteDependency();
                
                int constantTypeEntityCount = __constantTypeGroup.CalculateEntityCount();
                __manager.Begin(constantTypeEntityCount);
            }
        }
    }
}
