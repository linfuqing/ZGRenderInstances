using Unity.Entities;
using Unity.Mathematics.Geometry;

namespace ZG
{
    public struct RenderBounds : IComponentData
    {
        public MinMaxAABB aabb;
    }
}
