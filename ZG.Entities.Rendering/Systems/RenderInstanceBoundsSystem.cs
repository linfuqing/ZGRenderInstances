using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;

namespace ZG
{
    public struct RenderBoundsWorld : IComponentData
    {
        public MinMaxAABB aabb;
    }

    public struct RenderBoundsWorldChunk : IComponentData
    {
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct Node
        {
            [FieldOffset(0)]
            public int localFlag;
            [FieldOffset(4)]
            public int worldFlag;
            [FieldOffset(8)]
            public v128 entityMask;
        }

        public FixedList512Bytes<Node> nodes;
        
        public MinMaxAABB aabb;

        public const int DEPTH = 3;

        public const int MAX_DEPTH = 8;
        
        public static int GetNodeCount(int depth)
        {
            //++depth;

            return (((1 << (depth + depth)) - 1) & 0x15555);
        }

        public static int GetNodeStartIndexFromLevel(int level)
        {
            return ((1 << (level + level)) - 1) & 0x5555;
        }
        
        public static int GetNodeIndexFromLevelXY(int x, int y, int level)
        {
            /*int count = 1 << level;
            if (x >= count || y >= count)
                return -1;*/

            int nodeStartIndex = GetNodeStartIndexFromLevel(level);
            
            return nodeStartIndex + ((y << level) + x);
        }
        
        private static void FindNodeInfo(
            int minX, 
            int minY, 
            int maxX, 
            int maxY, 
            int depth, 
            out int level, 
            out int x, 
            out int y)
        {
            int patternX = minX ^ maxX,
                patternY = minY ^ maxY,
                bitPattern = math.max(patternX, patternY),
                highBit = bitPattern <= byte.MaxValue ? (32 - math.lzcnt((uint)bitPattern)) : MAX_DEPTH;

            level = math.min(MAX_DEPTH - highBit, depth - 1);
            
            int shift = MAX_DEPTH - level;

            x = maxX >> shift;
            y = maxY >> shift;
        }

        public static void Convert(
            in float3 min, 
            in float3 max, 
            in float3 sourceMin, 
            in float3 sourceMax, 
            out int2 destinationMin, 
            out int2 destinationMax, 
            out int flag)
        {
            float3 source = max - min, destination = math.float3(256.0f, 32.0f, 256.0f), result = destination / source;

            flag = 0;
            int3 targetMin = (int3)math.floor((sourceMin - min) * result), targetMax = (int3)math.floor((sourceMax - min) * result);
            for (int i = targetMin.y; i <= targetMax.y; ++i)
                flag |= 1 << i;

            destinationMin = targetMin.xz;
            destinationMax = targetMax.xz;
        }

        public RenderBoundsWorldChunk(in MinMaxAABB aabb)
        {
            this.aabb = aabb;
            
            nodes = default;

            nodes.Length = GetNodeCount(DEPTH);
        }
        
        public int Add(int entityIndex, in MinMaxAABB aabb)
        {
            Convert(
                this.aabb.Min, 
                this.aabb.Max, 
                aabb.Min, 
                aabb.Max, 
                out var targetMin, 
                out var targetMax,
                out var flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);
            
            FindNodeInfo(
                targetMin.x, 
                targetMin.y, 
                targetMax.x, 
                targetMax.y, 
                DEPTH, 
                out int level, 
                out int x, 
                out int y);

            int nodeIndex = GetNodeIndexFromLevelXY(x, y, level);
            
            ref var node = ref nodes.ElementAt(nodeIndex);

            if (entityIndex < 64)
                node.entityMask.ULong0 |= 1UL << entityIndex;
            else
                node.entityMask.ULong1 |= 1UL << (entityIndex - 64);

            node.localFlag |= flag;
            node.worldFlag |= flag;

            while (level > 0)
            {
                nodeIndex = GetNodeIndexFromLevelXY(x >>= 1, y >>= 1, --level);

                nodes.ElementAt(nodeIndex).worldFlag |= flag;
            }

            return nodeIndex;
        }

        public readonly v128 Search(in MinMaxAABB aabb, in NativeArray<RenderBoundsWorld> bounds)
        {
            v128 result = default;

            Convert(
                this.aabb.Min, 
                this.aabb.Max, 
                aabb.Min, 
                aabb.Max, 
                out var targetMin, 
                out var targetMax, 
                out int flag);

            targetMin = math.max(byte.MinValue, targetMin);
            targetMax = math.min(byte.MaxValue, targetMax);

            bool isNextLevel;
            int level = 0, shift, currentMinX, currentMaxX, currentMinY, currentMaxY, nodeIndex, i, j, k;
            ChunkEntityEnumerator enumerator;
            do
            {
                isNextLevel = false;

                shift = MAX_DEPTH - level;
                currentMinX = targetMin.x >> shift;
                currentMaxX = targetMax.x >> shift;
                currentMinY = targetMin.y >> shift;
                currentMaxY = targetMax.y >> shift;
                for (j = currentMinY; j <= currentMaxY; ++j)
                {
                    for (i = currentMinX; i <= currentMaxX; ++i)
                    {
                        nodeIndex = GetNodeIndexFromLevelXY(i, j, level);

                        ref readonly var node = ref nodes.ElementAt(nodeIndex);

                        if ((node.worldFlag & flag) == 0)
                            continue;
                        
                        isNextLevel = true;
                        
                        if (j == currentMinY || j == currentMaxY || i == currentMinX || i == currentMaxX)
                        {
                            enumerator = new ChunkEntityEnumerator(true, node.entityMask, bounds.Length);
                            while (enumerator.NextEntityIndex(out k))
                            {
                                if(!bounds[k].aabb.Overlaps(aabb))
                                    continue;
                                
                                if (k < 64)
                                    result.ULong0 |= 1UL << k;
                                else
                                    result.ULong1 |= 1UL << (k - 64);
                            }
                        }
                        else
                        {
                            result.ULong0 |= node.entityMask.ULong0;
                            result.ULong1 |= node.entityMask.ULong1;
                        }
                    }
                }

                ++level;
            }
            while (isNextLevel && level < DEPTH);

            return result;
        }
    }

    [BurstCompile, RequireMatchingQueriesForUpdate, UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct RenderInstanceBoundsSystem : ISystem
    {
        private EntityQuery __groupToCreateWorld;
        private EntityQuery __groupToCreateChunk;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreateWorld = builder
                    .WithAll<RenderBounds>()
                    .WithNone<RenderBoundsWorld>()
                    .Build(ref state);
            
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreateChunk = builder
                    .WithAll<RenderBoundsWorld>()
                    .WithNoneChunkComponent<RenderBoundsWorldChunk>()
                    .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            var entityManager = state.EntityManager;
            entityManager.AddComponent<RenderBoundsWorld>(__groupToCreateWorld);
            entityManager.AddComponent(__groupToCreateChunk, ComponentType.ChunkComponent<RenderBoundsWorldChunk>());
        }
    }
}