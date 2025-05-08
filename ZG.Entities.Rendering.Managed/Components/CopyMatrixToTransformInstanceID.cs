using Unity.Entities;

namespace ZG
{
    public struct CopyMatrixToTransformInstanceID : ICleanupComponentData
    {
        public bool isSendMessageOnDestroy;
        public int value;
    }
}