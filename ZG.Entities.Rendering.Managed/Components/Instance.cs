using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;

namespace ZG
{
    public struct Instance : IComponentData, IEnableableComponent
    {
        public FixedString32Bytes name;
    }

    public struct InstancePrefab : IBufferElementData, IEnableableComponent
    {
        public EntityPrefabReference reference;
    }
}