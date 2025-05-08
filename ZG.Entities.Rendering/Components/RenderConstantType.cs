using System;
using Unity.Collections;
using Unity.Entities;

namespace ZG
{
    public struct RenderConstantType : ISharedComponentData, IEquatable<RenderConstantType>
    {
        public FixedString128Bytes bufferName;
        public TypeIndex index;

        public override int GetHashCode()
        {
            return bufferName.GetHashCode() ^ index.GetHashCode();
        }

        public bool Equals(RenderConstantType other)
        {
            return bufferName == other.bufferName &&
                   index.Equals(other.index);
        }
    }
}