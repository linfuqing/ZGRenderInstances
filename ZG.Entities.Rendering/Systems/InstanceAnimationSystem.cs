using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace ZG
{
    [BurstCompile, UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct InstanceAnimationSystem : ISystem
    {
        private struct Evaluate
        {
            public float deltaTime;
            
            [ReadOnly]
            public BufferAccessor<InstanceSkinnedMeshRenderer> skinnedMeshRenderers;

            [ReadOnly]
            public NativeArray<InstanceAnimationDefinitionData> definitions;

            public NativeArray<InstanceAnimationStatus> states;
            
            [NativeDisableParallelForRestriction]
            public ComponentLookup<RenderSkinnedData> skinnedDatas;

            public bool Execute(int index)
            {
                var definition = definitions[index];
                var status = states[index];
                bool isPlaying = status.Evaluate(deltaTime, skinnedMeshRenderers[index].AsNativeArray(),
                    ref definition.definition.Value, ref skinnedDatas);

                if (!isPlaying && definition.defaultClipIndex != -1)
                {
                    isPlaying = true;
                    
                    status.clipIndex = definition.defaultClipIndex;
                    status.time = 0.0f;
                }

                states[index] = status;

                return isPlaying;
            }
        }

        [BurstCompile]
        private struct EvaluateEx : IJobChunk
        {
            public float deltaTime;
            
            [ReadOnly]
            public BufferTypeHandle<InstanceSkinnedMeshRenderer> skinnedMeshRendererType;

            [ReadOnly]
            public ComponentTypeHandle<InstanceAnimationDefinitionData> definitionType;

            public ComponentTypeHandle<InstanceAnimationStatus> statusType;
            
            [NativeDisableParallelForRestriction]
            public ComponentLookup<RenderSkinnedData> skinnedDatas;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                Evaluate evaluate;
                evaluate.deltaTime = deltaTime;
                evaluate.skinnedMeshRenderers = chunk.GetBufferAccessor(ref skinnedMeshRendererType);
                evaluate.definitions = chunk.GetNativeArray(ref definitionType);
                evaluate.states = chunk.GetNativeArray(ref statusType);
                evaluate.skinnedDatas = skinnedDatas;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    if(!evaluate.Execute(i))
                        chunk.SetComponentEnabled(ref statusType, i, false);
                }
            }
        }
        
        
        private BufferTypeHandle<InstanceSkinnedMeshRenderer> __skinnedMeshRendererType;

        private ComponentTypeHandle<InstanceAnimationDefinitionData> __definitionType;

        private ComponentTypeHandle<InstanceAnimationStatus> __statusType;
            
        private ComponentLookup<RenderSkinnedData> __skinnedDatas;

        private EntityQuery __group;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __skinnedMeshRendererType = state.GetBufferTypeHandle<InstanceSkinnedMeshRenderer>(true);
            __definitionType = state.GetComponentTypeHandle<InstanceAnimationDefinitionData>(true);
            __statusType = state.GetComponentTypeHandle<InstanceAnimationStatus>();
            __skinnedDatas = state.GetComponentLookup<RenderSkinnedData>();

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<InstanceSkinnedMeshRenderer, InstanceAnimationDefinitionData>()
                    .WithAllRW<InstanceAnimationStatus>()
                    .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            __skinnedMeshRendererType.Update(ref state);
            __definitionType.Update(ref state);
            __statusType.Update(ref state);
            __skinnedDatas.Update(ref state);
            
            EvaluateEx evaluate;
            evaluate.deltaTime = SystemAPI.Time.DeltaTime;
            evaluate.skinnedMeshRendererType = __skinnedMeshRendererType;
            evaluate.definitionType = __definitionType;
            evaluate.statusType = __statusType;
            evaluate.skinnedDatas = __skinnedDatas;

            state.Dependency = evaluate.ScheduleParallelByRef(__group, state.Dependency);
        }
    }
}