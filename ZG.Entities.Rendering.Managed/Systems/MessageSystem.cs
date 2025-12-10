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
        private struct Send
        {
            [ReadOnly]
            public NativeParallelMultiHashMap<FixedString32Bytes, int> instanceIDs;

            [ReadOnly]
            public BufferAccessor<MessageSender> senders;

            public BufferAccessor<Message> inputs;

            public BufferAccessor<MessageParameter> inputParameters;

            public NativeParallelMultiHashMap<int, Message> outputs;

            public NativeParallelMultiHashMap<int, MessageParameter> outputParameters;

            public int Execute(int index)
            {
                int instanceID;
                NativeParallelMultiHashMapIterator<FixedString32Bytes> iterator;
                var senders = this.senders[index];
                var parameters = index < inputParameters.Length ? inputParameters[index] : default;
                var messages = inputs[index];
                int numMessages = messages.Length, i;
                bool isSend;
                for (i = 0; i < numMessages; ++i)
                {
                    ref var message = ref messages.ElementAt(i);

                    isSend = false;
                    foreach (var sender in senders)
                    {
                        if (message.name == sender.messageName && message.value == sender.messageValue)
                        {
                            if (instanceIDs.TryGetFirstValue(sender.listenerName, out instanceID, out iterator))
                            {
                                do
                                {
                                    __Collect(instanceID, message, ref parameters, ref outputs,
                                        ref outputParameters);
                                } while (instanceIDs.TryGetNextValue(out instanceID, ref iterator));
                            }

                            isSend = true;
                        }
                    }

                    if (isSend)
                        messages[i--] = messages[--numMessages];
                }
                
                if(numMessages < messages.Length)
                    messages.ResizeUninitialized(numMessages);

                return numMessages;
            }
        }

        [BurstCompile]
        private struct SendEx : IJobChunk
        {
            [ReadOnly]
            public NativeParallelMultiHashMap<FixedString32Bytes, int> instanceIDs;

            [ReadOnly]
            public BufferTypeHandle<MessageSender> senderType;

            public BufferTypeHandle<Message> inputType;

            public BufferTypeHandle<MessageParameter> inputParameterType;

            public NativeParallelMultiHashMap<int, Message> outputs;

            public NativeParallelMultiHashMap<int, MessageParameter> outputParameters;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                Send send;
                send.instanceIDs = instanceIDs;
                send.senders = chunk.GetBufferAccessor(ref senderType);
                send.inputs = chunk.GetBufferAccessor(ref inputType);
                send.inputParameters = chunk.GetBufferAccessor(ref inputParameterType);
                send.outputs = outputs;
                send.outputParameters = outputParameters;
                
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    if(send.Execute(i) < 1)
                        chunk.SetComponentEnabled(ref inputType, i, false);
                }
            }
        }
        
        private struct Collect
        {
            [ReadOnly] 
            public NativeArray<Entity> entityArray;

            [ReadOnly] 
            public NativeArray<CopyMatrixToTransformInstanceID> instanceIDs;

            [ReadOnly]
            public NativeArray<MessageParent> parents;

            public BufferLookup<MessageParameter> inputParameters;

            public BufferLookup<Message> inputs;

            public NativeParallelMultiHashMap<int, Message> outputs;

            public NativeParallelMultiHashMap<int, MessageParameter> outputParameters;

            public void Execute(int index)
            {
                Entity entity = index < parents.Length ? parents[index].entity : entityArray[index];
                if (!inputs.TryGetBuffer(entity, out var messages))
                    return;

                int instanceID = instanceIDs[index].value;
                inputParameters.TryGetBuffer(entity, out var parameters);
                foreach (var message in messages)
                    __Collect(instanceID, message, ref parameters, ref outputs, ref outputParameters);

                messages.Clear();
                
                if(parameters.IsCreated)
                    parameters.Clear();

                inputs.SetBufferEnabled(entity, false);
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly] 
            public EntityTypeHandle entityType;

            [ReadOnly] 
            public ComponentTypeHandle<CopyMatrixToTransformInstanceID> instanceIDType;

            [ReadOnly] 
            public ComponentTypeHandle<MessageParent> parentType;
            
            public BufferLookup<MessageParameter> inputParameters;

            public BufferLookup<Message> inputs;

            public NativeParallelMultiHashMap<int, Message> outputs;

            public NativeParallelMultiHashMap<int, MessageParameter> outputParameters;

            public void Execute(
                in ArchetypeChunk chunk,
                int unfilteredChunkIndex,
                bool useEnabledMask,
                in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.entityArray = chunk.GetNativeArray(entityType);
                collect.instanceIDs = chunk.GetNativeArray(ref instanceIDType);
                collect.parents = chunk.GetNativeArray(ref parentType);
                collect.inputParameters = inputParameters;
                collect.inputs = inputs;
                collect.outputs = outputs;
                collect.outputParameters = outputParameters;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collect.Execute(i);
            }
        }

        private EntityTypeHandle __entityType;

        private ComponentTypeHandle<CopyMatrixToTransformInstanceID> __instanceIDType;

        private ComponentTypeHandle<MessageParent> __parentType;

        private BufferTypeHandle<MessageSender> __senderType;

        private BufferTypeHandle<Message> __inputType;

        private BufferTypeHandle<MessageParameter> __inputParameterType;

        private BufferLookup<Message> __inputs;

        private BufferLookup<MessageParameter> __inputParameters;

        private EntityQuery __childrenGroup;
        private EntityQuery __collectGroup;
        private EntityQuery __sendGroup;

        private NativeParallelMultiHashMap<int, Message> __outputs;

        private NativeParallelMultiHashMap<int, MessageParameter> __outputParameters;

        private static void __Collect(
            int instanceID, 
            in Message message, 
            ref DynamicBuffer<MessageParameter> parameters, 
            ref NativeParallelMultiHashMap<int, Message> outputs, 
            ref NativeParallelMultiHashMap<int, MessageParameter> outputParameters)
        {
            outputs.Add(instanceID, message);

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
        
        protected override void OnCreate()
        {
            base.OnCreate();

            __entityType = GetEntityTypeHandle();
            __instanceIDType = GetComponentTypeHandle<CopyMatrixToTransformInstanceID>(true);
            __parentType = GetComponentTypeHandle<MessageParent>(true);
            __senderType = GetBufferTypeHandle<MessageSender>(true);
            __inputType = GetBufferTypeHandle<Message>();
            __inputParameterType = GetBufferTypeHandle<MessageParameter>(true);
            __inputs = GetBufferLookup<Message>();
            __inputParameters = GetBufferLookup<MessageParameter>();

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __childrenGroup = builder
                    .WithAll<CopyMatrixToTransformInstanceID, MessageParent>()
                    .Build(this);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __collectGroup = builder
                    .WithAll<CopyMatrixToTransformInstanceID, Message>()
                    .Build(this);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __sendGroup = builder
                    .WithAll<MessageSender, Message>()
                    .Build(this);

            //RequireForUpdate(__group);

            __outputs = new NativeParallelMultiHashMap<int, Message>(1, Allocator.Persistent);
            __outputParameters = new NativeParallelMultiHashMap<int, MessageParameter>(1, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            __outputs.Dispose();
            __outputParameters.Dispose();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            var instanceIDs = MessageListener.instanceIDs;
            if (instanceIDs.IsCreated)
            {
                __senderType.Update(this);
                __inputType.Update(this);
                __inputParameterType.Update(this);
                
                SendEx send;
                send.instanceIDs = instanceIDs;
                send.senderType = __senderType;
                send.inputType = __inputType;
                send.inputParameterType = __inputParameterType;
                send.outputs = __outputs;
                send.outputParameters = __outputParameters;
                send.RunByRef(__sendGroup);
            }

            __entityType.Update(this);
            __instanceIDType.Update(this);
            __parentType.Update(this);
            __inputs.Update(this);
            __inputParameters.Update(this);

            CollectEx collect;
            collect.entityType = __entityType;
            collect.instanceIDType = __instanceIDType;
            collect.parentType = __parentType;
            collect.inputs = __inputs;
            collect.inputParameters = __inputParameters;
            collect.outputs = __outputs;
            collect.outputParameters = __outputParameters;
            collect.RunByRef(__childrenGroup);
            collect.RunByRef(__collectGroup);
            
            if (!__outputs.IsEmpty)
            {
                var (keys, count) = __outputs.GetUniqueKeyArray(Allocator.Temp);
                {
                    int key;
                    Transform transform;
                    for(int i = 0; i < count; ++i)
                    {
                        key = keys[i];
                        transform = Resources.InstanceIDToObject(key) as Transform;

                        foreach (var message in __outputs.GetValuesForKey(key))
                            __Send(message, transform);

                        //__instances.Remove(key);
                    }
                }
                keys.Dispose();
                    
                __outputs.Clear();
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
            
            __outputParameters.Clear();
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

                    foreach (var parameter in __outputParameters.GetValuesForKey(message.key))
                        temp.Set(parameter.id, parameter.value);
                }

                __outputParameters.Remove(message.key);
            }

            if (transform != null && transform.gameObject.activeInHierarchy)
            {
                try
                {
                    //UnityEngine.Debug.LogError($"BroadcastMessage {message.name} : {messageValue}");
                    transform.BroadcastMessage(message.name.ToString(), messageValue);
                }
                catch (Exception e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
            }
        }
    }
}