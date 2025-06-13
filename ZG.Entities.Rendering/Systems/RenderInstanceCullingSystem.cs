using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using Unity.Transforms;
using Math = Unity.Mathematics.Geometry.Math;

namespace ZG
{
    public struct RenderSingleton : IComponentData, IDisposable
    {
        public uint sharedDataVersion;
        public uint constantTypeVersion;
        public uint queueVersion;
        public NativeList<RenderQueue> queues;
        public NativeList<RenderSharedData> sharedDatas;
        public NativeList<RenderConstantType> constantTypes;
        public NativeHashMap<RenderConstantType, int> constantTypeIndices;
        public NativeHashMap<RenderSharedData, int> sharedDataIndices;

        public void Dispose()
        {
            if (queues.IsCreated)
                queues.Dispose();
            
            if (sharedDatas.IsCreated)
                sharedDatas.Dispose();
            
            if (constantTypes.IsCreated)
                constantTypes.Dispose();

            if (constantTypeIndices.IsCreated)
                constantTypeIndices.Dispose();

            if (sharedDataIndices.IsCreated)
                sharedDataIndices.Dispose();
        }

        public void Update(ref EntityManager entityManager)
        {
            uint queueVersion = (uint)entityManager.GetComponentOrderVersion<RenderQueue>();
            if (ChangeVersionUtility.DidChange(queueVersion, this.queueVersion))
            {
                this.queueVersion = queueVersion;
                
                if (queues.IsCreated)
                    queues.Dispose();

                entityManager.GetAllUniqueSharedComponents(out queues, Allocator.Persistent);
            }
            
            uint sharedDataVersion = (uint)entityManager.GetComponentOrderVersion<RenderSharedData>();
            if (ChangeVersionUtility.DidChange(sharedDataVersion, this.sharedDataVersion))
            {
                this.sharedDataVersion = sharedDataVersion;

                if (sharedDatas.IsCreated)
                    sharedDatas.Dispose();

                entityManager.GetAllUniqueSharedComponents(out sharedDatas, Allocator.Persistent);

                int numSharedDatas = sharedDatas.Length;
                if (sharedDataIndices.IsCreated)
                    sharedDataIndices.Clear();
                else
                    sharedDataIndices = new NativeHashMap<RenderSharedData, int>(numSharedDatas, Allocator.Persistent);
                
                for(int i = 0; i < numSharedDatas; ++i)
                    sharedDataIndices[sharedDatas[i]] = i;
            }
            
            uint constantTypeVersion = (uint)entityManager.GetComponentOrderVersion<RenderConstantType>();
            if (ChangeVersionUtility.DidChange(constantTypeVersion, this.constantTypeVersion))
            {
                this.constantTypeVersion = constantTypeVersion;

                if (constantTypes.IsCreated)
                    constantTypes.Dispose();
                
                entityManager.GetAllUniqueSharedComponents(out constantTypes, Allocator.Persistent);

                int numConstantTypes = constantTypes.Length;
                for (int i = 0; i < numConstantTypes; ++i)
                {
                    if (TypeManager.GetTypeIndexFromStableTypeHash(constantTypes[i].stableTypeHash) == TypeIndex.Null)
                    {
                        constantTypes.RemoveAtSwapBack(i--);

                        --numConstantTypes;
                    }
                }
                
                if (constantTypeIndices.IsCreated)
                    constantTypeIndices.Clear();
                else
                    constantTypeIndices = new NativeHashMap<RenderConstantType, int>(numConstantTypes, Allocator.Persistent);
                
                for(int i = 0; i < numConstantTypes; ++i)
                    constantTypeIndices[constantTypes[i]] = i;
            }
        }
    }

    [BurstCompile, UpdateAfter(typeof(TransformSystemGroup))]
    public partial struct RenderInstanceCullingSystem : ISystem
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ConstantTypeArray
        {
            public const int LENGTH = 128;
            
            public uint version;

            [ReadOnly] public DynamicComponentTypeHandle t0;
            [ReadOnly] public DynamicComponentTypeHandle t1;
            [ReadOnly] public DynamicComponentTypeHandle t2;
            [ReadOnly] public DynamicComponentTypeHandle t3;
            [ReadOnly] public DynamicComponentTypeHandle t4;
            [ReadOnly] public DynamicComponentTypeHandle t5;
            [ReadOnly] public DynamicComponentTypeHandle t6;
            [ReadOnly] public DynamicComponentTypeHandle t7;
            [ReadOnly] public DynamicComponentTypeHandle t8;
            [ReadOnly] public DynamicComponentTypeHandle t9;
            [ReadOnly] public DynamicComponentTypeHandle t10;
            [ReadOnly] public DynamicComponentTypeHandle t11;
            [ReadOnly] public DynamicComponentTypeHandle t12;
            [ReadOnly] public DynamicComponentTypeHandle t13;
            [ReadOnly] public DynamicComponentTypeHandle t14;
            [ReadOnly] public DynamicComponentTypeHandle t15;
            [ReadOnly] public DynamicComponentTypeHandle t16;
            [ReadOnly] public DynamicComponentTypeHandle t17;
            [ReadOnly] public DynamicComponentTypeHandle t18;
            [ReadOnly] public DynamicComponentTypeHandle t19;
            [ReadOnly] public DynamicComponentTypeHandle t20;
            [ReadOnly] public DynamicComponentTypeHandle t21;
            [ReadOnly] public DynamicComponentTypeHandle t22;
            [ReadOnly] public DynamicComponentTypeHandle t23;
            [ReadOnly] public DynamicComponentTypeHandle t24;
            [ReadOnly] public DynamicComponentTypeHandle t25;
            [ReadOnly] public DynamicComponentTypeHandle t26;
            [ReadOnly] public DynamicComponentTypeHandle t27;
            [ReadOnly] public DynamicComponentTypeHandle t28;
            [ReadOnly] public DynamicComponentTypeHandle t29;
            [ReadOnly] public DynamicComponentTypeHandle t30;
            [ReadOnly] public DynamicComponentTypeHandle t31;
            [ReadOnly] public DynamicComponentTypeHandle t32;
            [ReadOnly] public DynamicComponentTypeHandle t33;
            [ReadOnly] public DynamicComponentTypeHandle t34;
            [ReadOnly] public DynamicComponentTypeHandle t35;
            [ReadOnly] public DynamicComponentTypeHandle t36;
            [ReadOnly] public DynamicComponentTypeHandle t37;
            [ReadOnly] public DynamicComponentTypeHandle t38;
            [ReadOnly] public DynamicComponentTypeHandle t39;
            [ReadOnly] public DynamicComponentTypeHandle t40;
            [ReadOnly] public DynamicComponentTypeHandle t41;
            [ReadOnly] public DynamicComponentTypeHandle t42;
            [ReadOnly] public DynamicComponentTypeHandle t43;
            [ReadOnly] public DynamicComponentTypeHandle t44;
            [ReadOnly] public DynamicComponentTypeHandle t45;
            [ReadOnly] public DynamicComponentTypeHandle t46;
            [ReadOnly] public DynamicComponentTypeHandle t47;
            [ReadOnly] public DynamicComponentTypeHandle t48;
            [ReadOnly] public DynamicComponentTypeHandle t49;
            [ReadOnly] public DynamicComponentTypeHandle t50;
            [ReadOnly] public DynamicComponentTypeHandle t51;
            [ReadOnly] public DynamicComponentTypeHandle t52;
            [ReadOnly] public DynamicComponentTypeHandle t53;
            [ReadOnly] public DynamicComponentTypeHandle t54;
            [ReadOnly] public DynamicComponentTypeHandle t55;
            [ReadOnly] public DynamicComponentTypeHandle t56;
            [ReadOnly] public DynamicComponentTypeHandle t57;
            [ReadOnly] public DynamicComponentTypeHandle t58;
            [ReadOnly] public DynamicComponentTypeHandle t59;
            [ReadOnly] public DynamicComponentTypeHandle t60;
            [ReadOnly] public DynamicComponentTypeHandle t61;
            [ReadOnly] public DynamicComponentTypeHandle t62;
            [ReadOnly] public DynamicComponentTypeHandle t63;
            [ReadOnly] public DynamicComponentTypeHandle t64;
            [ReadOnly] public DynamicComponentTypeHandle t65;
            [ReadOnly] public DynamicComponentTypeHandle t66;
            [ReadOnly] public DynamicComponentTypeHandle t67;
            [ReadOnly] public DynamicComponentTypeHandle t68;
            [ReadOnly] public DynamicComponentTypeHandle t69;
            [ReadOnly] public DynamicComponentTypeHandle t70;
            [ReadOnly] public DynamicComponentTypeHandle t71;
            [ReadOnly] public DynamicComponentTypeHandle t72;
            [ReadOnly] public DynamicComponentTypeHandle t73;
            [ReadOnly] public DynamicComponentTypeHandle t74;
            [ReadOnly] public DynamicComponentTypeHandle t75;
            [ReadOnly] public DynamicComponentTypeHandle t76;
            [ReadOnly] public DynamicComponentTypeHandle t77;
            [ReadOnly] public DynamicComponentTypeHandle t78;
            [ReadOnly] public DynamicComponentTypeHandle t79;
            [ReadOnly] public DynamicComponentTypeHandle t80;
            [ReadOnly] public DynamicComponentTypeHandle t81;
            [ReadOnly] public DynamicComponentTypeHandle t82;
            [ReadOnly] public DynamicComponentTypeHandle t83;
            [ReadOnly] public DynamicComponentTypeHandle t84;
            [ReadOnly] public DynamicComponentTypeHandle t85;
            [ReadOnly] public DynamicComponentTypeHandle t86;
            [ReadOnly] public DynamicComponentTypeHandle t87;
            [ReadOnly] public DynamicComponentTypeHandle t88;
            [ReadOnly] public DynamicComponentTypeHandle t89;
            [ReadOnly] public DynamicComponentTypeHandle t90;
            [ReadOnly] public DynamicComponentTypeHandle t91;
            [ReadOnly] public DynamicComponentTypeHandle t92;
            [ReadOnly] public DynamicComponentTypeHandle t93;
            [ReadOnly] public DynamicComponentTypeHandle t94;
            [ReadOnly] public DynamicComponentTypeHandle t95;
            [ReadOnly] public DynamicComponentTypeHandle t96;
            [ReadOnly] public DynamicComponentTypeHandle t97;
            [ReadOnly] public DynamicComponentTypeHandle t98;
            [ReadOnly] public DynamicComponentTypeHandle t99;
            [ReadOnly] public DynamicComponentTypeHandle t100;
            [ReadOnly] public DynamicComponentTypeHandle t101;
            [ReadOnly] public DynamicComponentTypeHandle t102;
            [ReadOnly] public DynamicComponentTypeHandle t103;
            [ReadOnly] public DynamicComponentTypeHandle t104;
            [ReadOnly] public DynamicComponentTypeHandle t105;
            [ReadOnly] public DynamicComponentTypeHandle t106;
            [ReadOnly] public DynamicComponentTypeHandle t107;
            [ReadOnly] public DynamicComponentTypeHandle t108;
            [ReadOnly] public DynamicComponentTypeHandle t109;
            [ReadOnly] public DynamicComponentTypeHandle t110;
            [ReadOnly] public DynamicComponentTypeHandle t111;
            [ReadOnly] public DynamicComponentTypeHandle t112;
            [ReadOnly] public DynamicComponentTypeHandle t113;
            [ReadOnly] public DynamicComponentTypeHandle t114;
            [ReadOnly] public DynamicComponentTypeHandle t115;
            [ReadOnly] public DynamicComponentTypeHandle t116;
            [ReadOnly] public DynamicComponentTypeHandle t117;
            [ReadOnly] public DynamicComponentTypeHandle t118;
            [ReadOnly] public DynamicComponentTypeHandle t119;
            [ReadOnly] public DynamicComponentTypeHandle t120;
            [ReadOnly] public DynamicComponentTypeHandle t121;
            [ReadOnly] public DynamicComponentTypeHandle t122;
            [ReadOnly] public DynamicComponentTypeHandle t123;
            [ReadOnly] public DynamicComponentTypeHandle t124;
            [ReadOnly] public DynamicComponentTypeHandle t125;
            [ReadOnly] public DynamicComponentTypeHandle t126;
            [ReadOnly] public DynamicComponentTypeHandle t127;

            // Need to accept &t0 as input, because 'fixed' must be in the callsite.
            public DynamicComponentTypeHandle this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0: return t0;
                        case 1: return t1;
                        case 2: return t2;
                        case 3: return t3;
                        case 4: return t4;
                        case 5: return t5;
                        case 6: return t6;
                        case 7: return t7;
                        case 8: return t8;
                        case 9: return t9;
                        case 10: return t10;
                        case 11: return t11;
                        case 12: return t12;
                        case 13: return t13;
                        case 14: return t14;
                        case 15: return t15;
                        case 16: return t16;
                        case 17: return t17;
                        case 18: return t18;
                        case 19: return t19;
                        case 20: return t20;
                        case 21: return t21;
                        case 22: return t22;
                        case 23: return t23;
                        case 24: return t24;
                        case 25: return t25;
                        case 26: return t26;
                        case 27: return t27;
                        case 28: return t28;
                        case 29: return t29;
                        case 30: return t30;
                        case 31: return t31;
                        case 32: return t32;
                        case 33: return t33;
                        case 34: return t34;
                        case 35: return t35;
                        case 36: return t36;
                        case 37: return t37;
                        case 38: return t38;
                        case 39: return t39;
                        case 40: return t40;
                        case 41: return t41;
                        case 42: return t42;
                        case 43: return t43;
                        case 44: return t44;
                        case 45: return t45;
                        case 46: return t46;
                        case 47: return t47;
                        case 48: return t48;
                        case 49: return t49;
                        case 50: return t50;
                        case 51: return t51;
                        case 52: return t52;
                        case 53: return t53;
                        case 54: return t54;
                        case 55: return t55;
                        case 56: return t56;
                        case 57: return t57;
                        case 58: return t58;
                        case 59: return t59;
                        case 60: return t60;
                        case 61: return t61;
                        case 62: return t62;
                        case 63: return t63;
                        case 64: return t64;
                        case 65: return t65;
                        case 66: return t66;
                        case 67: return t67;
                        case 68: return t68;
                        case 69: return t69;
                        case 70: return t70;
                        case 71: return t71;
                        case 72: return t72;
                        case 73: return t73;
                        case 74: return t74;
                        case 75: return t75;
                        case 76: return t76;
                        case 77: return t77;
                        case 78: return t78;
                        case 79: return t79;
                        case 80: return t80;
                        case 81: return t81;
                        case 82: return t82;
                        case 83: return t83;
                        case 84: return t84;
                        case 85: return t85;
                        case 86: return t86;
                        case 87: return t87;
                        case 88: return t88;
                        case 89: return t89;
                        case 90: return t90;
                        case 91: return t91;
                        case 92: return t92;
                        case 93: return t93;
                        case 94: return t94;
                        case 95: return t95;
                        case 96: return t96;
                        case 97: return t97;
                        case 98: return t98;
                        case 99: return t99;
                        case 100: return t100;
                        case 101: return t101;
                        case 102: return t102;
                        case 103: return t103;
                        case 104: return t104;
                        case 105: return t105;
                        case 106: return t106;
                        case 107: return t107;
                        case 108: return t108;
                        case 109: return t109;
                        case 110: return t110;
                        case 111: return t111;
                        case 112: return t112;
                        case 113: return t113;
                        case 114: return t114;
                        case 115: return t115;
                        case 116: return t116;
                        case 117: return t117;
                        case 118: return t118;
                        case 119: return t119;
                        case 120: return t120;
                        case 121: return t121;
                        case 122: return t122;
                        case 123: return t123;
                        case 124: return t124;
                        case 125: return t125;
                        case 126: return t126;
                        case 127: return t127;
                        default: return default;
                    }
                }

                set
                {
                    switch (index)
                    {
                        case 0: t0 = value; break;
                        case 1: t1 = value; break;
                        case 2: t2 = value; break;
                        case 3: t3 = value; break;
                        case 4: t4 = value; break;
                        case 5: t5 = value; break;
                        case 6: t6 = value; break;
                        case 7: t7 = value; break;
                        case 8: t8 = value; break;
                        case 9: t9 = value; break;
                        case 10: t10 = value; break;
                        case 11: t11 = value; break;
                        case 12: t12 = value; break;
                        case 13: t13 = value; break;
                        case 14: t14 = value; break;
                        case 15: t15 = value; break;
                        case 16: t16 = value; break;
                        case 17: t17 = value; break;
                        case 18: t18 = value; break;
                        case 19: t19 = value; break;
                        case 20: t20 = value; break;
                        case 21: t21 = value; break;
                        case 22: t22 = value; break;
                        case 23: t23 = value; break;
                        case 24: t24 = value; break;
                        case 25: t25 = value; break;
                        case 26: t26 = value; break;
                        case 27: t27 = value; break;
                        case 28: t28 = value; break;
                        case 29: t29 = value; break;
                        case 30: t30 = value; break;
                        case 31: t31 = value; break;
                        case 32: t32 = value; break;
                        case 33: t33 = value; break;
                        case 34: t34 = value; break;
                        case 35: t35 = value; break;
                        case 36: t36 = value; break;
                        case 37: t37 = value; break;
                        case 38: t38 = value; break;
                        case 39: t39 = value; break;
                        case 40: t40 = value; break;
                        case 41: t41 = value; break;
                        case 42: t42 = value; break;
                        case 43: t43 = value; break;
                        case 44: t44 = value; break;
                        case 45: t45 = value; break;
                        case 46: t46 = value; break;
                        case 47: t47 = value; break;
                        case 48: t48 = value; break;
                        case 49: t49 = value; break;
                        case 50: t50 = value; break;
                        case 51: t51 = value; break;
                        case 52: t52 = value; break;
                        case 53: t53 = value; break;
                        case 54: t54 = value; break;
                        case 55: t55 = value; break;
                        case 56: t56 = value; break;
                        case 57: t57 = value; break;
                        case 58: t58 = value; break;
                        case 59: t59 = value; break;
                        case 60: t60 = value; break;
                        case 61: t61 = value; break;
                        case 62: t62 = value; break;
                        case 63: t63 = value; break;
                        case 64: t64 = value; break;
                        case 65: t65 = value; break;
                        case 66: t66 = value; break;
                        case 67: t67 = value; break;
                        case 68: t68 = value; break;
                        case 69: t69 = value; break;
                        case 70: t70 = value; break;
                        case 71: t71 = value; break;
                        case 72: t72 = value; break;
                        case 73: t73 = value; break;
                        case 74: t74 = value; break;
                        case 75: t75 = value; break;
                        case 76: t76 = value; break;
                        case 77: t77 = value; break;
                        case 78: t78 = value; break;
                        case 79: t79 = value; break;
                        case 80: t80 = value; break;
                        case 81: t81 = value; break;
                        case 82: t82 = value; break;
                        case 83: t83 = value; break;
                        case 84: t84 = value; break;
                        case 85: t85 = value; break;
                        case 86: t86 = value; break;
                        case 87: t87 = value; break;
                        case 88: t88 = value; break;
                        case 89: t89 = value; break;
                        case 90: t90 = value; break;
                        case 91: t91 = value; break;
                        case 92: t92 = value; break;
                        case 93: t93 = value; break;
                        case 94: t94 = value; break;
                        case 95: t95 = value; break;
                        case 96: t96 = value; break;
                        case 97: t97 = value; break;
                        case 98: t98 = value; break;
                        case 99: t99 = value; break;
                        case 100: t100 = value; break;
                        case 101: t101 = value; break;
                        case 102: t102 = value; break;
                        case 103: t103 = value; break;
                        case 104: t104 = value; break;
                        case 105: t105 = value; break;
                        case 106: t106 = value; break;
                        case 107: t107 = value; break;
                        case 108: t108 = value; break;
                        case 109: t109 = value; break;
                        case 110: t110 = value; break;
                        case 111: t111 = value; break;
                        case 112: t112 = value; break;
                        case 113: t113 = value; break;
                        case 114: t114 = value; break;
                        case 115: t115 = value; break;
                        case 116: t116 = value; break;
                        case 117: t117 = value; break;
                        case 118: t118 = value; break;
                        case 119: t119 = value; break;
                        case 120: t120 = value; break;
                        case 121: t121 = value; break;
                        case 122: t122 = value; break;
                        case 123: t123 = value; break;
                        case 124: t124 = value; break;
                        case 125: t125 = value; break;
                        case 126: t126 = value; break;
                        case 127: t127 = value; break;
                    }
                }
            }

            public ConstantTypeArray(in NativeArray<RenderConstantType> constantTypes, ref SystemState systemState)
            {
                this = default;

                int length = 0;

                ComponentType componentType;
                componentType.AccessModeType = ComponentType.AccessMode.ReadOnly;
                foreach (var constantType in constantTypes)
                {
                    componentType.TypeIndex = TypeManager.GetTypeIndexFromStableTypeHash(constantType.stableTypeHash);
                    //if(componentType.TypeIndex == TypeIndex.Null)
                    //    continue;
                    
                    this[length++] = systemState.GetDynamicComponentTypeHandle(componentType);
                }

                if (length == 0)
                {
                    length = 1;
                    
                    componentType.TypeIndex = TypeManager.GetTypeIndex<Disabled>();
                    t0 = systemState.GetDynamicComponentTypeHandle(componentType);
                }

                for (int i = length; i < LENGTH; ++i)
                    this[i] = t0;
            }

            public void Update(in RenderSingleton singleton, ref SystemState systemState)
            {
                if (ChangeVersionUtility.DidChange(singleton.constantTypeVersion, this.version))
                {
                    this = new ConstantTypeArray(singleton.constantTypes.AsArray(), ref systemState);

                    this.version = singleton.constantTypeVersion;
                }
                else
                {
                    DynamicComponentTypeHandle dynamicComponentTypeHandle;
                    for (int i = 0; i < LENGTH; ++i)
                    {
                        dynamicComponentTypeHandle = this[i];
                        dynamicComponentTypeHandle.Update(ref systemState);
                        this[i] = dynamicComponentTypeHandle;
                    }
                }
            }
        }

        private struct Batch : IComparable<Batch>
        {
            public long renderQueue;
            
            public int sharedDataIndex;
            public int constantTypeIndex;

            public int CompareTo(Batch other)
            {
                int result = renderQueue.CompareTo(other.renderQueue);
                if (result != 0)
                    return result;

                result = sharedDataIndex.CompareTo(other.sharedDataIndex);
                if (result != 0)
                    return result;
                
                return constantTypeIndex.CompareTo(other.constantTypeIndex);
            }

            public override int GetHashCode()
            {
                return (int)(renderQueue >> 32) ^ (int)renderQueue ^ sharedDataIndex ^ constantTypeIndex;
            }
        }

        private struct CameraBatch : IEquatable<CameraBatch>
        {
            public Entity entity;
            
            public Batch value;
            
            public bool Equals(CameraBatch other)
            {
                return entity == other.entity && value.CompareTo(other.value) == 0;
            }

            public override int GetHashCode()
            {
                return entity.GetHashCode() ^ value.GetHashCode();
            }
        }

        private struct CameraBatchChunk : IEquatable<CameraBatchChunk>
        {
            public CameraBatch value;

            public int count;
            
            public bool Equals(CameraBatchChunk other)
            {
                return value.Equals(other.value);
            }

            public override int GetHashCode()
            {
                return value.GetHashCode();
            }
        }

        private struct RenderList
        {
            public struct Value
            {
                public int renderIndex;

                public int entityIndex;
            }

            public readonly ArchetypeChunk Chunk;
        
            public FixedList512Bytes<int> values;

            public int length => values.Length;

            public Value this[int index]
            {
                get
                {
                    var value = values[index];
                
                    Value result;
                    result.renderIndex = value >> 8;
                    result.entityIndex = value & 0xFF;
                
                    return result;
                }
            }

            public RenderList(in ArchetypeChunk chunk)
            {
                Chunk = chunk;

                values = default;
            }
        
            public void Add(int entityIndex, int renderIndex)
            {
                values.Add(renderIndex << 8 | entityIndex);
            }
        }

        private struct CameraRenderList
        {
            public RenderList value;

            public int cameraBatchChunkIndex;
        }
        
        [BurstCompile]
        private struct Transform : IJobChunk
        {
            [ReadOnly] 
            public ComponentTypeHandle<LocalToWorld> localToWorldType;

            [ReadOnly] 
            public ComponentTypeHandle<RenderBounds> boundsType;

            public ComponentTypeHandle<RenderBoundsWorld> boundsWorldType;
            
            public ComponentTypeHandle<RenderBoundsWorldChunk> boundsWorldChunkType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                RenderBoundsWorldChunk renderBoundsWorldChunk;
                renderBoundsWorldChunk.aabb = new MinMaxAABB(float.MaxValue, float.MinValue);

                var localToWorlds = chunk.GetNativeArray(ref localToWorldType);
                var bounds = chunk.GetNativeArray(ref boundsType);
                var boundsWorld = chunk.GetNativeArray(ref boundsWorldType);
                RenderBoundsWorld renderBoundsWorld;
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    renderBoundsWorld.aabb = Math.Transform(localToWorlds[i].Value, bounds[i].aabb);
                    
                    renderBoundsWorldChunk.aabb.Encapsulate(renderBoundsWorld.aabb);
                    
                    boundsWorld[i] = renderBoundsWorld;
                }
                
                chunk.SetChunkComponentData(ref boundsWorldChunkType, renderBoundsWorldChunk);
            }
        }

        [BurstCompile]
        private struct Culling : IJobChunk
        {
            [ReadOnly] 
            public NativeHashMap<RenderSharedData, int> sharedDataIndices;

            [ReadOnly] 
            public NativeHashMap<RenderConstantType, int> constantTypeIndices;
            
            [ReadOnly] 
            public ComponentLookup<RenderFrustumPlanes> frustumPlanes;

            [ReadOnly] 
            public NativeArray<Entity> cameraEntities;

            [ReadOnly] 
            public SharedComponentTypeHandle<RenderSharedData> sharedDataType;

            [ReadOnly]
            public SharedComponentTypeHandle<RenderConstantType> constantType;

            [ReadOnly] 
            public SharedComponentTypeHandle<RenderQueue> renderQueueType;
            
            [ReadOnly] 
            public ComponentTypeHandle<RenderBoundsWorldChunk> boundsWorldChunkType;
            
            public NativeParallelMultiHashMap<CameraBatch, ArchetypeChunk>.ParallelWriter chunks;

            public void Execute(
                in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                CameraBatch cameraBatch;
                cameraBatch.value.sharedDataIndex = sharedDataIndices[chunk.GetSharedComponent(sharedDataType)];
                cameraBatch.value.constantTypeIndex = chunk.Has(constantType) ? constantTypeIndices[chunk.GetSharedComponent(constantType)] : -1;

                cameraBatch.value.renderQueue = chunk.Has(renderQueueType) ? chunk.GetSharedComponent(renderQueueType).value : 0L;

                MinMaxAABB aabb;
                RenderFrustumPlanes frustumPlanes;
                foreach (var cameraEntity in cameraEntities)
                {
                    if (!this.frustumPlanes.TryGetComponent(cameraEntity, out frustumPlanes))
                        continue;

                    aabb = chunk.GetChunkComponentData(ref boundsWorldChunkType).aabb;
                    if (RenderFrustumPlanes.IntersectResult.Out ==
                        frustumPlanes.Intersect(aabb.Center, aabb.Extents))
                        continue;
                    
                    cameraBatch.entity = cameraEntity;
                    chunks.Add(cameraBatch, chunk);
                }
            }
        }

        private struct Reset : IJob
        {
            [ReadOnly] 
            public NativeParallelMultiHashMap<CameraBatch, ArchetypeChunk> chunks;
            
            public NativeList<CameraBatchChunk> cameraBatchChunks;

            public void Execute()
            {
                cameraBatchChunks.Clear();

                CameraBatchChunk cameraBatchChunk;
                cameraBatchChunk.count = 0;
                foreach (var pair in chunks)
                {
                    cameraBatchChunk.value = pair.Key;
                    cameraBatchChunks.Add(cameraBatchChunk);
                }

                int count = cameraBatchChunks.AsArray().Unique();
                
                cameraBatchChunks.ResizeUninitialized(count);
            }
        }

        private struct Collect : IJobParallelForDefer
        {
            private struct Entry : IComparable<Entry>
            {
                public float depth;
                
                public int entityIndex;

                public ArchetypeChunk chunk;

                public int CompareTo(Entry other)
                {
                    return depth.CompareTo(other.depth);
                }

                public override int GetHashCode()
                {
                    return depth.GetHashCode();
                }
            }
            
            public NativeArray<CameraBatchChunk> cameraBatchChunks;

            [ReadOnly] 
            public ComponentLookup<RenderFrustumPlanes> frustumPlanes;

            [ReadOnly] 
            public ComponentTypeHandle<RenderBoundsWorld> boundsWorldType;

            [ReadOnly] 
            public NativeParallelMultiHashMap<CameraBatch, ArchetypeChunk> chunks;

            public NativeParallelMultiHashMap<Entity, CameraRenderList>.ParallelWriter renderLists;

            public void Execute(int index)
            {
                var cameraBatchChunk = cameraBatchChunks[index];
                if ((cameraBatchChunk.value.value.renderQueue >> 32) > (int)UnityEngine.Rendering.RenderQueue.GeometryLast)
                    cameraBatchChunk.count = __CullingWithSort(false, index, cameraBatchChunk.value);
                else
                    cameraBatchChunk.count = __CullingWithoutSort(index, cameraBatchChunk.value);

                cameraBatchChunks[index] = cameraBatchChunk;
            }

            private int __CullingWithoutSort(int cameraBatchChunkIndex, in CameraBatch cameraBatch)
            {
                NativeArray<RenderBoundsWorld> boundsWorld;
                MinMaxAABB worldAABB;
                int i, count, renderIndex = 0;
                if (chunks.TryGetFirstValue(cameraBatch, out var chunk, out var iterator))
                {
                    if (!this.frustumPlanes.TryGetComponent(cameraBatch.entity, out var frustumPlanes))
                        return 0;

                    CameraRenderList renderList;
                    renderList.cameraBatchChunkIndex = cameraBatchChunkIndex;
                    do
                    {
                        renderList.value = new RenderList(chunk);
                        
                        boundsWorld = chunk.GetNativeArray(ref boundsWorldType);

                        count = chunk.Count;
                        for (i = 0; i < count; ++i)
                        {
                            worldAABB = boundsWorld[i].aabb;
                            if (RenderFrustumPlanes.IntersectResult.Out ==
                                frustumPlanes.Intersect(worldAABB.Center, worldAABB.Extents))
                                continue;

                            renderList.value.Add(i, renderIndex++);
                        }
                    
                        if(renderList.value.length > 0)
                            renderLists.Add(cameraBatch.entity, renderList);
                    } while (chunks.TryGetNextValue(out chunk, ref iterator));
                }

                return renderIndex;
            }
            
            private int __CullingWithSort(
                bool isLess, 
                int cameraBatchChunkIndex, 
                in CameraBatch cameraBatch)
            {
                if (chunks.TryGetFirstValue(cameraBatch, out var chunk, out var iterator))
                {
                    if (!this.frustumPlanes.TryGetComponent(cameraBatch.entity, out var frustumPlanes))
                        return 0;
                    
                    NativeList<Entry> entries = default;
                    Entry entry;
                    NativeArray<RenderBoundsWorld> boundsWorld;
                    MinMaxAABB worldAABB;
                    int i, count;
                    do
                    {
                        entry.chunk = chunk;

                        boundsWorld = chunk.GetNativeArray(ref boundsWorldType);

                        count = chunk.Count;
                        for (i= 0; i < count; ++i)
                        {
                            worldAABB = boundsWorld[i].aabb;
                            if (RenderFrustumPlanes.IntersectResult.Out ==
                                frustumPlanes.Intersect(worldAABB.Center, worldAABB.Extents))
                                continue;

                            entry.entityIndex = i;

                            entry.depth = frustumPlanes.DepthOf(worldAABB.Center);

                            if (!entries.IsCreated)
                                entries = new NativeList<Entry>(Allocator.Temp);

                            entries.Add(entry);
                        }
                    } while (chunks.TryGetNextValue(out chunk, ref iterator));

                    int renderIndex = 0;
                    if (entries.IsCreated)
                    {
                        entries.Sort();

                        CameraRenderList renderList;
                        renderList.value = default;
                        var renderLists = new UnsafeHashMap<ArchetypeChunk, RenderList>(
                            chunks.CountValuesForKey(cameraBatch), Allocator.Temp);
                        if (isLess)
                        {
                            foreach (var temp in entries)
                            {
                                if (temp.chunk != renderList.value.Chunk)
                                {
                                    if(renderList.value.length > 0)
                                        renderLists[renderList.value.Chunk] = renderList.value;

                                    if(!renderLists.TryGetValue(temp.chunk, out renderList.value))
                                        renderList.value = new RenderList(temp.chunk);
                                }

                                renderList.value.Add(temp.entityIndex, renderIndex++);
                            }
                        }
                        else
                        {
                            int length = entries.Length;
                            for (i = length - 1; i >= 0; --i)
                            {
                                entry = entries[i];
                                if (entry.chunk != renderList.value.Chunk)
                                {
                                    if(renderList.value.length > 0)
                                        renderLists[renderList.value.Chunk] = renderList.value;

                                    if(!renderLists.TryGetValue(entry.chunk, out renderList.value))
                                        renderList.value = new RenderList(entry.chunk);
                                }

                                renderList.value.Add(entry.entityIndex, renderIndex++);
                            }
                        }

                        if(renderList.value.length > 0)
                            renderLists[renderList.value.Chunk] = renderList.value;

                        renderList.cameraBatchChunkIndex = cameraBatchChunkIndex;
                        foreach (var pair in renderLists)
                        {
                            renderList.value = pair.Value;
                            this.renderLists.Add(cameraBatch.entity, renderList);
                        }

                        renderLists.Dispose();

                        entries.Dispose();
                    }

                    return renderIndex;
                }

                return 0;
            }
        }

        private struct Command
        {
            private struct Comparer : System.Collections.Generic.IComparer<CameraRenderList>
            {
                public NativeArray<CameraBatchChunk> cameraBatchChunks;

                public int Compare(CameraRenderList x, CameraRenderList y)
                {
                    return cameraBatchChunks[x.cameraBatchChunkIndex].value.value
                        .CompareTo(cameraBatchChunks[y.cameraBatchChunkIndex].value.value);
                }
            }
            
            public ConstantTypeArray constantTypeArray;
            
            [ReadOnly] 
            public NativeArray<RenderConstantType> constantTypes;

            [ReadOnly]
            public NativeArray<CameraBatchChunk> cameraBatchChunks;

            [ReadOnly]
            public NativeParallelMultiHashMap<Entity, CameraRenderList> renderLists;
            
            [ReadOnly]
            public ComponentTypeHandle<LocalToWorld> localToWorldType;

            [ReadOnly] 
            public NativeArray<Entity> entityArray;
            public BufferAccessor<RenderConstantBuffer> constantBuffers;

            public BufferAccessor<RenderChunk> renderChunks;
            public BufferAccessor<RenderLocalToWorld> renderLocalToWorlds;

            public void Execute(int index)
            {
                if (this.renderLists.TryGetFirstValue(entityArray[index], out var temp, out var iterator))
                {
                    var renderLists = new NativeList<CameraRenderList>(Allocator.Temp);
                    do
                    {
                        renderLists.Add(temp);
                    } while (this.renderLists.TryGetNextValue(out temp, ref iterator));

                    Comparer comparer;
                    comparer.cameraBatchChunks = cameraBatchChunks;
                    renderLists.AsArray().Sort(comparer);
                    
                    var renderLocalToWorlds = this.renderLocalToWorlds[index];
                    renderLocalToWorlds.Clear();
                    
                    var renderChunks = this.renderChunks[index];
                    renderChunks.Clear();
                    
                    RenderChunk renderChunk;
                    renderChunk.sharedDataIndex = -1;
                    renderChunk.constantTypeIndex = -1;
                    renderChunk.constantByteOffset = 0;
                    renderChunk.count = 0;
                    
                    var constantBuffers = this.constantBuffers[index];

                    int i, length, count, 
                        localToWorldsOffset = 0,  
                        constantTypeStride = 0, 
                        cameraBatchChunkIndex = -1;
                    CameraBatchChunk cameraBatchChunk;
                    RenderList.Value previous, next;
                    DynamicComponentTypeHandle constantType = default;
                    NativeArray<RenderLocalToWorld> localToWorlds;
                    NativeArray<byte> destination = default, source;
                    foreach (var renderList in renderLists)
                    {
                        if (renderList.cameraBatchChunkIndex != cameraBatchChunkIndex)
                        {
                            cameraBatchChunkIndex = renderList.cameraBatchChunkIndex;

                            if(renderChunk.count > 0)
                                renderChunks.Add(renderChunk);
                            else
                                UnityEngine.Assertions.Assert.AreEqual(0, renderChunk.constantByteOffset);
                            
                            renderChunk.count = 0;
                            
                            cameraBatchChunk = cameraBatchChunks[renderList.cameraBatchChunkIndex];
                            
                            localToWorldsOffset = renderLocalToWorlds.Length;
                            renderLocalToWorlds.ResizeUninitialized(localToWorldsOffset + cameraBatchChunk.count);

                            renderChunk.sharedDataIndex = cameraBatchChunk.value.value.sharedDataIndex;
                            renderChunk.constantTypeIndex = cameraBatchChunk.value.value.constantTypeIndex;
                            if (renderChunk.constantTypeIndex != -1)
                            {
                                constantType = constantTypeArray[cameraBatchChunk.value.value.constantTypeIndex];

                                constantTypeStride = TypeManager
                                    .GetTypeInfo(
                                        TypeManager.GetTypeIndexFromStableTypeHash(constantTypes[cameraBatchChunk.value.value.constantTypeIndex]
                                            .stableTypeHash))
                                    .TypeSize;
                                
                                destination = constantBuffers[renderChunk.constantTypeIndex].Write(
                                    cameraBatchChunk.count * constantTypeStride, 
                                    out renderChunk.constantByteOffset);
                            }
                        }

                        length = renderList.value.length;
                        
                        if (renderChunk.constantTypeIndex != -1)
                        {
                            source = renderList.value.Chunk.GetDynamicComponentDataArrayReinterpret<byte>(
                                ref constantType, 
                                constantTypeStride);

                            count = 1;
                            previous = renderList.value[0];
                            for(i = 1; i < length; ++i)
                            {
                                next = renderList.value[i];
                                if (next.entityIndex == previous.entityIndex + count &&
                                    next.renderIndex == previous.renderIndex + count)
                                {
                                    ++count;
                                    
                                    continue;
                                }

                                count *= constantTypeStride;

                                destination
                                    .GetSubArray(previous.renderIndex * constantTypeStride, count)
                                    .CopyFrom(source.GetSubArray(previous.entityIndex * constantTypeStride, count));

                                previous = next;
                                count = 1;
                            }
                            
                            count *= constantTypeStride;

                            destination
                                .GetSubArray(previous.renderIndex * constantTypeStride, count)
                                .CopyFrom(source.GetSubArray(previous.entityIndex * constantTypeStride, count));
                        }
                        
                        localToWorlds = renderList.value.Chunk.GetNativeArray(ref localToWorldType)
                            .Reinterpret<RenderLocalToWorld>();
                        
                        count = 1;
                        previous = renderList.value[0];
                        for(i = 1; i < length; ++i)
                        {
                            next = renderList.value[i];
                            if (next.entityIndex == previous.entityIndex + count &&
                                next.renderIndex == previous.renderIndex + count)
                            {
                                ++count;
                                    
                                continue;
                            }

                            renderLocalToWorlds.AsNativeArray()
                                .GetSubArray(localToWorldsOffset + previous.renderIndex, count)
                                .CopyFrom(localToWorlds.GetSubArray(previous.entityIndex, count));

                            previous = next;
                            count = 1;
                        }
                        
                        renderLocalToWorlds.AsNativeArray()
                            .GetSubArray(localToWorldsOffset + previous.renderIndex, count)
                            .CopyFrom(localToWorlds.GetSubArray(previous.entityIndex, count));
                        
                        renderChunk.count += length;
                    }
                    
                    if(renderChunk.count > 0)
                        renderChunks.Add(renderChunk);
                    else
                        UnityEngine.Assertions.Assert.AreEqual(0, renderChunk.constantByteOffset);

                    renderLists.Dispose();
                }
            }
        }

        [BurstCompile]
        private struct CommandEx : IJobChunk
        {
            public ConstantTypeArray constantTypeArray;
            
            [ReadOnly] 
            public NativeArray<RenderConstantType> constantTypes;

            [ReadOnly]
            public NativeArray<CameraBatchChunk> cameraBatchChunks;

            [ReadOnly]
            public NativeParallelMultiHashMap<Entity, CameraRenderList> renderLists;
            
            [ReadOnly]
            public ComponentTypeHandle<LocalToWorld> localToWorldType;

            [ReadOnly] 
            public EntityTypeHandle entityType;
            
            public BufferTypeHandle<RenderConstantBuffer> constantBufferType;

            public BufferTypeHandle<RenderChunk> renderChunkType;
            public BufferTypeHandle<RenderLocalToWorld> renderLocalToWorldType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                Command command;
                command.constantTypes = constantTypes;
                command.constantTypeArray = constantTypeArray;
                command.cameraBatchChunks = cameraBatchChunks;
                command.renderLists = renderLists;
                command.localToWorldType = localToWorldType;
                command.entityArray = chunk.GetNativeArray(entityType);
                command.constantBuffers = chunk.GetBufferAccessor(ref constantBufferType);
                command.renderChunks = chunk.GetBufferAccessor(ref renderChunkType);
                command.renderLocalToWorlds = chunk.GetBufferAccessor(ref renderLocalToWorldType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    command.Execute(i);
            }
        }

        public static readonly SharedStatic<bool> WillCurrentFrameRender = SharedStatic<bool>.GetOrCreate<RenderInstanceCullingSystem>();

        public static readonly int InnerloopBatchCount = 4;
        
        private EntityTypeHandle __entityType;

        private SharedComponentTypeHandle<RenderSharedData> __sharedDataType;

        private SharedComponentTypeHandle<RenderConstantType> __constantType;

        private SharedComponentTypeHandle<RenderQueue> __renderQueueType;
        
        private ComponentTypeHandle<RenderBounds> __boundsType;
        private ComponentTypeHandle<RenderBoundsWorld> __boundsWorldType;
        private ComponentTypeHandle<RenderBoundsWorldChunk> __boundsWorldChunkType;

        private ComponentTypeHandle<LocalToWorld> __localToWorldType;

        private ComponentLookup<RenderFrustumPlanes> __frustumPlanes;

        private BufferTypeHandle<RenderConstantBuffer> __constantBufferType;

        private BufferTypeHandle<RenderChunk> __renderChunkType;
        private BufferTypeHandle<RenderLocalToWorld> __renderLocalToWorldType;
        
        private EntityQuery __groupToTransform;
        private EntityQuery __groupToCulling;
        private EntityQuery __groupToCommand;

        private ConstantTypeArray __constantTypeArray;
        
        private NativeList<CameraBatchChunk> __cameraBatchChunks;

        private NativeParallelMultiHashMap<CameraBatch, ArchetypeChunk> __chunks;

        private NativeParallelMultiHashMap<Entity, CameraRenderList> __renderLists;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __sharedDataType = state.GetSharedComponentTypeHandle<RenderSharedData>();
            __constantType = state.GetSharedComponentTypeHandle<RenderConstantType>();
            __renderQueueType = state.GetSharedComponentTypeHandle<RenderQueue>();
            
            __boundsType = state.GetComponentTypeHandle<RenderBounds>(true);
            
            __boundsWorldType = state.GetComponentTypeHandle<RenderBoundsWorld>();
            __boundsWorldChunkType = state.GetComponentTypeHandle<RenderBoundsWorldChunk>();
            
            __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>(true);
            
            __frustumPlanes = state.GetComponentLookup<RenderFrustumPlanes>(true);
            
            __constantBufferType = state.GetBufferTypeHandle<RenderConstantBuffer>();
            __renderChunkType = state.GetBufferTypeHandle<RenderChunk>();
            __renderLocalToWorldType = state.GetBufferTypeHandle<RenderLocalToWorld>();

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToTransform = builder
                    .WithAll<LocalToWorld, RenderBounds>()
                    .WithAllRW<RenderBoundsWorld>()
                    .WithAllChunkComponentRW<RenderBoundsWorldChunk>()
                    .Build(ref state);
            
            __groupToTransform.AddChangedVersionFilter(ComponentType.ReadOnly<LocalToWorld>());
            __groupToTransform.AddChangedVersionFilter(ComponentType.ReadOnly<RenderBounds>());
            __groupToTransform.AddOrderVersionFilter();
            
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCulling = builder
                    .WithAll<RenderSharedData, RenderBoundsWorld>()
                    .WithAllChunkComponent<RenderBoundsWorldChunk>()
                    .Build(ref state);
            
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCommand = builder
                    .WithAll<RenderFrustumPlanes>()
                    .WithAllRW<RenderConstantBuffer>()
                    .WithAllRW<RenderChunk, RenderLocalToWorld>()
                    .Build(ref state);
            
            state.RequireForUpdate<RenderSingleton>();

            __cameraBatchChunks = new NativeList<CameraBatchChunk>(Allocator.Persistent);

            __chunks = new NativeParallelMultiHashMap<CameraBatch, ArchetypeChunk>(1, Allocator.Persistent);

            __renderLists = new NativeParallelMultiHashMap<Entity, CameraRenderList>(1, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __cameraBatchChunks.Dispose();
            
            __chunks.Dispose();

            __renderLists.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!WillCurrentFrameRender.Data)
                return;
            
            __localToWorldType.Update(ref state);
            __boundsType.Update(ref state);
            __boundsWorldType.Update(ref state);
            __boundsWorldChunkType.Update(ref state);
            
            Transform transform;
            transform.localToWorldType = __localToWorldType;
            transform.boundsType = __boundsType;
            transform.boundsWorldType = __boundsWorldType;
            transform.boundsWorldChunkType = __boundsWorldChunkType;
            var jobHandle = transform.ScheduleParallelByRef(__groupToTransform, state.Dependency);
            
            __frustumPlanes.Update(ref state);
            __sharedDataType.Update(ref state);
            __constantType.Update(ref state);
            __renderQueueType.Update(ref state);

            int capacity = __groupToCulling.CalculateChunkCount() * __groupToCommand.CalculateEntityCount();
            
            __chunks.Clear();
            __chunks.Capacity = math.max(
                __chunks.Capacity, 
                capacity);

            var singleton = SystemAPI.GetSingleton<RenderSingleton>();

            Culling culling;
            culling.sharedDataIndices = singleton.sharedDataIndices;
            culling.constantTypeIndices = singleton.constantTypeIndices;
            culling.frustumPlanes = __frustumPlanes;
            culling.cameraEntities =
                __groupToCommand.ToEntityListAsync(state.WorldUpdateAllocator, out var commandEntitiesJobHandle)
                    .AsDeferredJobArray();
            culling.sharedDataType = __sharedDataType;
            culling.constantType = __constantType;
            culling.renderQueueType = __renderQueueType;
            culling.boundsWorldChunkType = __boundsWorldChunkType;
            culling.chunks = __chunks.AsParallelWriter();
            jobHandle = culling.ScheduleParallelByRef(__groupToCulling,
                JobHandle.CombineDependencies(commandEntitiesJobHandle, jobHandle));

            Reset reset;
            reset.cameraBatchChunks = __cameraBatchChunks;
            reset.chunks = __chunks;
            jobHandle = reset.ScheduleByRef(jobHandle);

            __renderLists.Clear();
            __renderLists.Capacity = math.max(
                __renderLists.Capacity, 
                capacity);
            
            var cameraBatchChunks = __cameraBatchChunks.AsDeferredJobArray();

            Collect collect;
            collect.cameraBatchChunks = cameraBatchChunks;
            collect.frustumPlanes = __frustumPlanes;
            collect.boundsWorldType = __boundsWorldType;
            collect.chunks = __chunks;
            collect.renderLists = __renderLists.AsParallelWriter();
            jobHandle = collect.ScheduleByRef(__cameraBatchChunks, InnerloopBatchCount, jobHandle);

            __entityType.Update(ref state);
            __constantTypeArray.Update(singleton, ref state);
            __constantBufferType.Update(ref state);
            __renderChunkType.Update(ref state);
            __renderLocalToWorldType.Update(ref state);
            
            CommandEx command;
            command.constantTypeArray = __constantTypeArray;
            command.constantTypes = singleton.constantTypes.AsArray();
            command.cameraBatchChunks = cameraBatchChunks;
            command.renderLists = __renderLists;
            command.localToWorldType = __localToWorldType;
            command.entityType = __entityType;
            command.constantBufferType = __constantBufferType;
            command.renderChunkType = __renderChunkType;
            command.renderLocalToWorldType = __renderLocalToWorldType;
            jobHandle = command.ScheduleParallelByRef(__groupToCommand, jobHandle);
            
            state.Dependency = jobHandle;
        }
    }
}