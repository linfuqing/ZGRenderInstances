using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZG
{
    [BurstCompile, UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct InstanceAnimationSystem : ISystem
    {
        private struct Evaluate
        {
            public bool isEnter;
            
            public float deltaTime;
            
            public Random random;
            
            [ReadOnly]
            public BufferAccessor<InstanceSkinnedMeshRenderer> skinnedMeshRenderers;
            
            [ReadOnly]
            public BufferAccessor<InstanceAnimationEnterClip> enterClips;

            [ReadOnly]
            public NativeArray<InstanceAnimationDefinitionData> definitions;

            public NativeArray<InstanceAnimationStatus> states;
            
            [NativeDisableParallelForRestriction]
            public ComponentLookup<RenderSkinnedData> skinnedDatas;

            public bool Execute(int index)
            {
                var status = states[index];
                if (isEnter && index < enterClips.Length)
                {
                    float randomValue = random.NextFloat(), chance = 0.0f;
                    var enterClips = this.enterClips[index];
                    foreach (var enterClip in enterClips)
                    {
                        chance += enterClip.chance;
                        if (chance > randomValue)
                        {
                            status.clipIndex = enterClip.index;
                            status.time = 0.0f;
                            
                            break;
                        }
                    }
                }
                
                var definition = definitions[index];
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
            public uint hash;
            public float deltaTime;
            
            [ReadOnly]
            public BufferTypeHandle<InstanceSkinnedMeshRenderer> skinnedMeshRendererType;

            [ReadOnly]
            public ComponentTypeHandle<InstanceAnimationDefinitionData> definitionType;

            public ComponentTypeHandle<InstanceAnimationStatus> statusType;
            
            public BufferTypeHandle<InstanceAnimationEnterClip> enterClipType;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<RenderSkinnedData> skinnedDatas;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                Evaluate evaluate;
                evaluate.random = Random.CreateFromIndex(hash ^ (uint)unfilteredChunkIndex);
                evaluate.deltaTime = deltaTime;
                evaluate.skinnedMeshRenderers = chunk.GetBufferAccessor(ref skinnedMeshRendererType);
                evaluate.enterClips = chunk.GetBufferAccessor(ref enterClipType);
                evaluate.definitions = chunk.GetNativeArray(ref definitionType);
                evaluate.states = chunk.GetNativeArray(ref statusType);
                evaluate.skinnedDatas = skinnedDatas;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    evaluate.isEnter = chunk.IsComponentEnabled(ref enterClipType, i);
                    if(!evaluate.Execute(i))
                        chunk.SetComponentEnabled(ref statusType, i, false);

                    if (evaluate.isEnter)
                        chunk.SetComponentEnabled(ref enterClipType, i, false);
                }
            }
        }
        
        private BufferTypeHandle<InstanceSkinnedMeshRenderer> __skinnedMeshRendererType;

        private ComponentTypeHandle<InstanceAnimationDefinitionData> __definitionType;

        private ComponentTypeHandle<InstanceAnimationStatus> __statusType;
            
        private BufferTypeHandle<InstanceAnimationEnterClip> __enterClipType;

        private ComponentLookup<RenderSkinnedData> __skinnedDatas;

        private EntityQuery __group;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __skinnedMeshRendererType = state.GetBufferTypeHandle<InstanceSkinnedMeshRenderer>(true);
            __definitionType = state.GetComponentTypeHandle<InstanceAnimationDefinitionData>(true);
            __statusType = state.GetComponentTypeHandle<InstanceAnimationStatus>();
            __enterClipType = state.GetBufferTypeHandle<InstanceAnimationEnterClip>();
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
            __enterClipType.Update(ref state);
            __skinnedDatas.Update(ref state);

            long hash = math.aslong(SystemAPI.Time.ElapsedTime);
            
            EvaluateEx evaluate;
            evaluate.hash = (uint)(hash >> 32) ^ (uint)hash;
            evaluate.deltaTime = SystemAPI.Time.DeltaTime;
            evaluate.skinnedMeshRendererType = __skinnedMeshRendererType;
            evaluate.definitionType = __definitionType;
            evaluate.statusType = __statusType;
            evaluate.enterClipType = __enterClipType;
            evaluate.skinnedDatas = __skinnedDatas;

            state.Dependency = evaluate.ScheduleParallelByRef(__group, state.Dependency);
        }
    }
}