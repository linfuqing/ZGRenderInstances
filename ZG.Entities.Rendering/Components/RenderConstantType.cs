using System;
using Unity.Entities;

namespace ZG
{
    public struct RenderConstantType : ISharedComponentData, IEquatable<RenderConstantType>
    {
        public int bufferID;
        public TypeIndex index;

        public override int GetHashCode()
        {
            return bufferID ^ index.GetHashCode();
        }

        public bool Equals(RenderConstantType other)
        {
            return bufferID == other.bufferID &&
                   index.Equals(other.index);
        }
    }
}