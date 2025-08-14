using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace ZG
{
    [BurstCompile, UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct InstanceAnimationMessageSystem : ISystem
    {
        private struct Apply
        {
            [ReadOnly] 
            public BufferAccessor<InstanceAnimationMessage> animationMessages;
            
            [ReadOnly]
            public NativeArray<InstanceAnimationDefinitionData> animations;
            
            public NativeArray<InstanceAnimationStatus> animationStates;
            
            public BufferAccessor<Message> messages;

            public BufferAccessor<MessageParameter> messageParameters;

            public bool Execute(int index, out int numMessages)
            {
                InstanceAnimationStatus animationStatus;
                animationStatus.clipIndex = -1;
                
                var animation = animations[index];
                var animationMessages = this.animationMessages[index];
                var messages = this.messages[index];
                var messageParameters = index < this.messageParameters.Length ? this.messageParameters[index] : default;
                int i, j, numMessageParameters = messageParameters.IsCreated ? messageParameters.Length : 0;
                numMessages = messages.Length;
                foreach (var animationMessage in animationMessages)
                {
                    for (i = 0; i < numMessages; ++i)
                    {
                        ref var message = ref messages.ElementAt(i);
                        if (message.name == animationMessage.messageName &&
                            message.value == animationMessage.messageValue)
                        {
                            if (message.key != 0)
                            {
                                for (j = 0; j < numMessageParameters; ++j)
                                {
                                    if (messageParameters.ElementAt(j).messageKey != message.key)
                                        continue;

                                    messageParameters.RemoveAt(j--);

                                    --numMessageParameters;
                                }
                            }

                            animationStatus.clipIndex = animation.IndexOfClip(animationMessage.clipName);
                            
                            messages.RemoveAt(i--);

                            --numMessages;
                        }
                    }

                    if (numMessages < 1)
                        break;
                }

                if (animationStatus.clipIndex != -1)
                {
                    animationStatus.time = 0.0f;

                    animationStates[index] = animationStatus;

                    return true;
                }

                return false;
            }
        }
        
        [BurstCompile]
        private struct ApplyEx : IJobChunk
        {
            [ReadOnly] 
            public BufferTypeHandle<InstanceAnimationMessage> animationMessageType;
            
            [ReadOnly]
            public ComponentTypeHandle<InstanceAnimationDefinitionData> animationType;
            
            public ComponentTypeHandle<InstanceAnimationStatus> animationStatusType;
            
            public BufferTypeHandle<Message> messageType;

            public BufferTypeHandle<MessageParameter> messageParameterType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                Apply apply;
                apply.animationMessages = chunk.GetBufferAccessor(ref animationMessageType);
                apply.animations = chunk.GetNativeArray(ref animationType);
                apply.animationStates = chunk.GetNativeArray(ref animationStatusType);
                apply.messages = chunk.GetBufferAccessor(ref messageType);
                apply.messageParameters = chunk.GetBufferAccessor(ref messageParameterType);

                int numMessages;
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    if (apply.Execute(i, out numMessages))
                    {
                        if(numMessages < 1)
                            chunk.SetComponentEnabled(ref messageType, i, false);
                        
                        chunk.SetComponentEnabled(ref animationStatusType, i, true);
                    }
                }
            }
        }
        
        private BufferTypeHandle<InstanceAnimationMessage> __animationMessageType;
            
        private ComponentTypeHandle<InstanceAnimationDefinitionData> __animationType;
            
        private ComponentTypeHandle<InstanceAnimationStatus> __animationStatusType;
            
        private BufferTypeHandle<Message> __messageType;

        private BufferTypeHandle<MessageParameter> __messageParameterType;

        private EntityQuery __group;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __animationMessageType = state.GetBufferTypeHandle<InstanceAnimationMessage>(true);
            __animationType = state.GetComponentTypeHandle<InstanceAnimationDefinitionData>(true);
            __animationStatusType = state.GetComponentTypeHandle<InstanceAnimationStatus>();
            __messageType = state.GetBufferTypeHandle<Message>();
            __messageParameterType = state.GetBufferTypeHandle<MessageParameter>();

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<InstanceAnimationMessage, InstanceAnimationDefinitionData>()
                    .WithPresentRW<InstanceAnimationStatus>()
                    .WithAllRW<Message>()
                    .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            __animationMessageType.Update(ref state);
            __animationType.Update(ref state);
            __animationStatusType.Update(ref state);
            __messageType.Update(ref state);
            __messageParameterType.Update(ref state);
            
            ApplyEx apply;
            apply.animationMessageType = __animationMessageType;
            apply.animationType = __animationType;
            apply.animationStatusType = __animationStatusType;
            apply.messageType = __messageType;
            apply.messageParameterType = __messageParameterType;

            state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);
        }
    }
}