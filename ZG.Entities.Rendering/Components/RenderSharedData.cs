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
    public struct RenderSharedData : ISharedComponentData, IEquatable<RenderSharedData>, IComparable<RenderSharedData>
    {
        public int subMeshIndex;
        public UnityObjectRef<Mesh> mesh;
        public UnityObjectRef<Material> material;
        public UnityObjectRef<Shader> shader;

        public bool Equals(RenderSharedData other)
        {
            return subMeshIndex == other.subMeshIndex &&
                   mesh.Equals(other.mesh) &&
                   material.Equals(other.material);
        }

        public int CompareTo(RenderSharedData other)
        {
            int result = shader.GetHashCode().CompareTo(other.shader.GetHashCode());
            if (result == 0)
            {
                result = material.GetHashCode().CompareTo(other.material.GetHashCode());
                if (result == 0)
                {
                    result = mesh.GetHashCode().CompareTo(other.mesh.GetHashCode());
                    if (result == 0)
                        result = subMeshIndex.CompareTo(other.subMeshIndex);
                }
            }

            return result;
        }

        public override int GetHashCode()
        {
            return subMeshIndex ^ mesh.GetHashCode() ^ material.GetHashCode();
        }
    }
}
