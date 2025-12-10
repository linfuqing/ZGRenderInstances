using Unity.Collections;
using UnityEngine;

public sealed class MessageListener : MonoBehaviour
{
    public static NativeParallelMultiHashMap<FixedString32Bytes, int> instanceIDs;

    private FixedString32Bytes __name;
    
    private int __instanceID;

    void Awake()
    {
        __name = name;
        __instanceID = transform.GetInstanceID();
        
        if (!instanceIDs.IsCreated)
            instanceIDs = new NativeParallelMultiHashMap<FixedString32Bytes, int>(1, Allocator.Persistent);
        
        instanceIDs.Add(__name, __instanceID);
    }

    void OnDestroy()
    {
        if (instanceIDs.IsCreated)
        {
            if (instanceIDs.TryGetFirstValue(__name, out int instanceID, out var iterator))
            {
                do
                {
                    if (instanceID == __instanceID)
                    {
                        instanceIDs.Remove(iterator);

                        if (instanceIDs.IsEmpty)
                        {
                            instanceIDs.Dispose();

                            instanceIDs = default;
                        }

                        break;
                    }
                }while(instanceIDs.TryGetNextValue(out instanceID, ref iterator));
            }
        }
    }
}
