using System;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
namespace ZG
{
    public class InstanceAnimationMessageAuthoring : MonoBehaviour
    {
        class Baker : Baker<InstanceAnimationMessageAuthoring>
        {
            public override void Bake(InstanceAnimationMessageAuthoring authoring)
            {
                int numMessageClips = authoring._messageClips == null ? 0 : authoring._messageClips.Length;
                if (numMessageClips < 1)
                    return;
                
                Entity entity = GetEntity(authoring, TransformUsageFlags.None);

                var messages = AddBuffer<InstanceAnimationMessage>(entity);
                messages.ResizeUninitialized(numMessageClips);
                for (int i = 0; i < numMessageClips; ++i)
                {
                    ref var source = ref authoring._messageClips[i];
                    ref var destination = ref messages.ElementAt(i);
                    destination.clipName = source.name;
                    destination.messageName = source.messageName;
                    destination.messageValue = source.messageValue;
                }
            }
        }

        [Serializable]
        internal struct MessageClip
        {
            public string name;
            public string messageName;
            public Object messageValue;
        }

        [SerializeField] 
        internal MessageClip[] _messageClips;
    }
}
#endif