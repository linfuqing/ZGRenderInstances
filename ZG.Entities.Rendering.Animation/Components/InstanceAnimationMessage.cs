using Unity.Collections;
using Unity.Entities;

public struct InstanceAnimationMessage : IBufferElementData
{
    public FixedString128Bytes clipName;
    public FixedString128Bytes messageName;
    public UnityObjectRef<UnityEngine.Object> messageValue;
}
