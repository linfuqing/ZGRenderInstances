using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Math = Unity.Mathematics.Geometry.Math;
using Plane = UnityEngine.Plane;

namespace ZG
{
    public struct RenderSharedData : ISharedComponentData, IEquatable<RenderSharedData>
    {
        public int subMeshIndex;
        public UnityObjectRef<Mesh> mesh;
        public UnityObjectRef<Material> material;

        public bool Equals(RenderSharedData other)
        {
            return subMeshIndex == other.subMeshIndex &&
                   mesh.Equals(other.mesh) &&
                   material.Equals(other.material);
        }

        public override int GetHashCode()
        {
            return subMeshIndex ^ mesh.GetHashCode() ^ material.GetHashCode();
        }
    }
}
