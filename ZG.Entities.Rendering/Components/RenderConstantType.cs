using System;
using Unity.Collections;
using Unity.Entities;

namespace ZG
{
    public struct RenderConstantType : ISharedComponentData, IEquatable<RenderConstantType>
    {
        public FixedString128Bytes bufferName;
        public ulong stableTypeHash;

        public override int GetHashCode()
        {
            return bufferName.GetHashCode() ^ stableTypeHash.GetHashCode();
        }

        public bool Equals(RenderConstantType other)
        {
            return bufferName == other.bufferName &&
                   stableTypeHash.Equals(other.stableTypeHash);
        }
    }
}