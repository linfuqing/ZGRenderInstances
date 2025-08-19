using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ZG
{
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true),
     UpdateBefore(typeof(BeginInitializationEntityCommandBufferSystem))]
    public partial class MessageSystem : SystemBase
    {
        private struct Collect
        {
            [ReadOnly] 
            public ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs;
            
            [ReadOnly] 
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<MessageParent> parents;

            public BufferAccessor<MessageParameter> inputParameters;

            public BufferAccessor<Message> inputMessages;

            public NativeParallelMultiHashMap<int, Message> outputMessages;

            public NativeParallelMultiHashMap<int, MessageParameter> outputParameters;

            public bool Execute(int index)
            {
                Entity entity = index < parents.Length ? parents[index].entity : entityArray[index];
                if (!instanceIDs.TryGetComponent(entity, out var instanceID))
                    return false;
                
                var parameters = index < inputParameters.Length ? inputParameters[index] : default;
                var messages = inputMessages[index];
                foreach (var message in messages)
                    __Collect(instanceID.value, message, ref parameters);

                messages.Clear();
                
                if(parameters.IsCreated)
                    parameters.Clear();

                return true;
            }

            private void __Collect(int instanceID, in Message message, ref DynamicBuffer<MessageParameter> parameters)
            {
                outputMessages.Add(instanceID, message);

                if (message.key != 0)
                {
                    int numParameters = parameters.IsCreated ? parameters.Length : 0;
                    for (int i = 0; i < numParameters; ++i)
                    {
                        ref var parameter = ref parameters.ElementAt(i);
                        if (parameter.messageKey != message.key)
                            continue;

                        outputParameters.Add(parameter.messageKey, parameter);

                        parameters.RemoveAt(i--);

                        --numParameters;
                    }
                }
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly] 
            public ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs;

            [ReadOnly] 
            public EntityTypeHandle entityType;

            [ReadOnly] 
            public ComponentTypeHandle<MessageParent> parentType;
            
            public BufferTypeHandle<MessageParameter> inputParameterType;

            public BufferTypeHandle<Message> inputMessageType;

            public NativeParallelMultiHashMap<int, Message> outputMessages;

            public NativeParallelMultiHashMap<int, MessageParameter> outputParameters;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.instanceIDs = instanceIDs;
                collect.entityArray = chunk.GetNativeArray(entityType);
                collect.parents = chunk.GetNativeArray(ref parentType);
                collect.inputParameters = chunk.GetBufferAccessor(ref inputParameterType);
                collect.inputMessages = chunk.GetBufferAccessor(ref inputMessageType);
                collect.outputMessages = outputMessages;
                collect.outputParameters = outputParameters;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    if(collect.Execute(i))
                        chunk.SetComponentEnabled(ref inputMessageType, i, false);
                }
            }
        }

        private EntityTypeHandle __entityType;

        private ComponentLookup<CopyMatrixToTransformInstanceID> __instanceIDs;

        private ComponentTypeHandle<MessageParent> __parentType;

        private BufferTypeHandle<Message> __instanceType;

        private BufferTypeHandle<MessageParameter> __parameterType;

        private EntityQuery __group;

        private NativeParallelMultiHashMap<int, Message> __instances;

        private NativeParallelMultiHashMap<int, MessageParameter> __parameters;

        protected override void OnCreate()
        {
            base.OnCreate();

            __instanceIDs = GetComponentLookup<CopyMatrixToTransformInstanceID>(true);
            __parentType = GetComponentTypeHandle<MessageParent>(true);
            __instanceType = GetBufferTypeHandle<Message>();
            __parameterType = GetBufferTypeHandle<MessageParameter>();

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<Message>()
                    .WithAny<MessageParent, CopyMatrixToTransformInstanceID>()
                    .Build(this);

            //RequireForUpdate(__group);

            __instances = new NativeParallelMultiHashMap<int, Message>(1, Allocator.Persistent);
            __parameters = new NativeParallelMultiHashMap<int, MessageParameter>(1, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            __instances.Dispose();
            __parameters.Dispose();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            __entityType.Update(this);
            __instanceIDs.Update(this);
            __parentType.Update(this);
            __instanceType.Update(this);
            __parameterType.Update(this);

            CollectEx collect;
            collect.instanceIDs = __instanceIDs;
            collect.entityType = __entityType;
            collect.parentType = __parentType;
            collect.inputMessageType = __instanceType;
            collect.inputParameterType = __parameterType;
            collect.outputMessages = __instances;
            collect.outputParameters = __parameters;
            collect.RunByRef(__group);
            
            if (!__instances.IsEmpty)
            {
                using (var keys = __instances.GetKeyArray(Allocator.Temp))
                {
                    Transform transform;
                    foreach (var key in keys)
                    {
                        transform = Resources.InstanceIDToObject(key) as Transform;

                        foreach (var message in __instances.GetValuesForKey(key))
                            __Send(message, transform);

                        //__instances.Remove(key);
                    }
                }
                    
                __instances.Clear();
            }

            /*if (!__instances.IsEmpty)
            {
                __parents.Update(this);

                Entities.ForEach((
                        Entity entity,
                        in CopyMatrixToTransformInstanceID instanceID) =>
                    {
                        MessageParent parent = default;
                        if (__instances.TryGetFirstValue(entity, out var message, out var iterator) ||
                            __parents.TryGetComponent(entity, out parent) &&
                            __instances.TryGetFirstValue(parent.entity, out message, out iterator))
                        {
                            var transform = Resources.InstanceIDToObject(instanceID.value) as Transform;
                            do
                            {
                                __Send(message, transform);
                            } while (__instances.TryGetNextValue(out message, ref iterator));

                            __instances.Remove(parent.entity == Entity.Null ? entity : parent.entity);
                        }
                    })
                    .WithAll<CopyMatrixToTransformInstanceID>()
                    .WithAny<Message, MessageParent>()
                    .WithoutBurst()
                    .Run();

                if (!__instances.IsEmpty)
                {
                    using (var keys = __instances.GetKeyArray(Allocator.Temp))
                    {
                        Transform transform;
                        CopyMatrixToTransformInstanceID instanceID;
                        __instanceIDs.Update(this);
                        foreach (var key in keys)
                        {
                            if (!__instanceIDs.TryGetComponent(key, out instanceID))
                                continue;

                            transform = Resources.InstanceIDToObject(instanceID.value) as Transform;

                            foreach (var message in __instances.GetValuesForKey(key))
                                __Send(message, transform);

                            //__instances.Remove(key);
                        }
                    }
                    
                    __instances.Clear();
                }
            }*/
            
            __parameters.Clear();
        }

        private void __Send(in Message message, Transform transform)
        {
            var messageValue = message.value.Value;
            if (message.key == 0)
            {
                if (messageValue is IMessage temp)
                    temp.Clear();
            }
            else
            {
                if (messageValue is IMessage temp)
                {
                    temp.Clear();

                    foreach (var parameter in __parameters.GetValuesForKey(message.key))
                        temp.Set(parameter.id, parameter.value);
                }

                __parameters.Remove(message.key);
            }

            if (transform != null)
            {
                try
                {
                    //UnityEngine.Debug.LogError($"BroadcastMessage {message.name} : {messageValue}");
                    transform.BroadcastMessage(message.name.ToString(), messageValue);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e.InnerException ?? e);
                }
            }
        }
    }
}