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
                    __Collect(instanceID, message, ref parameters);

                messages.Clear();
                
                if(parameters.IsCreated)
                    parameters.Clear();

                inputs.SetBufferEnabled(entity, false);
            }

            private void __Collect(int instanceID, in Message message, ref DynamicBuffer<MessageParameter> parameters)
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

        private BufferLookup<Message> __inputs;

        private BufferLookup<MessageParameter> __inputParameters;

        private EntityQuery __group;

        private NativeParallelMultiHashMap<int, Message> __outputs;

        private NativeParallelMultiHashMap<int, MessageParameter> __outputParameters;

        protected override void OnCreate()
        {
            base.OnCreate();

            __entityType = GetEntityTypeHandle();
            __instanceIDType = GetComponentTypeHandle<CopyMatrixToTransformInstanceID>(true);
            __parentType = GetComponentTypeHandle<MessageParent>(true);
            __inputs = GetBufferLookup<Message>();
            __inputParameters = GetBufferLookup<MessageParameter>();

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<CopyMatrixToTransformInstanceID>()
                    .WithAny<Message, MessageParent>()
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
            collect.RunByRef(__group);
            
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