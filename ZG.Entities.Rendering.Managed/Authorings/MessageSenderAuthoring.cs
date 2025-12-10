using System;
using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
namespace ZG
{
    public class MessageSenderAuthoring : MonoBehaviour
    {
        [Serializable]
        internal struct Sender
        {
            public string name;
            public string messageName;
            public UnityEngine.Object messageValue;
        }

        class Baker : Baker<MessageSenderAuthoring>
        {
            public override void Bake(MessageSenderAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                var senders = AddBuffer<MessageSender>(entity);
                
                int numSenders = authoring._senders == null ? 0 : authoring._senders.Length;
                senders.ResizeUninitialized(numSenders);
                for (int i = 0; i < numSenders; ++i)
                {
                    ref var source = ref authoring._senders[i];
                    ref var destination = ref senders.ElementAt(i);
                    destination.listenerName = source.name;
                    destination.messageName = source.messageName;
                    destination.messageValue = source.messageValue;
                }

            }
        }

        [SerializeField] 
        internal Sender[] _senders;
    }
}
#endif