using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics.Geometry;

namespace ZG
{
    public struct RenderBoundsWorld : IComponentData
    {
        public MinMaxAABB aabb;
    }

    public struct RenderBoundsWorldChunk : IComponentData
    {
        public MinMaxAABB aabb;
    }

    [BurstCompile, RequireMatchingQueriesForUpdate, UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct RenderInstanceBoundsSystem : ISystem
    {
        private EntityQuery __groupToCreateWorld;
        private EntityQuery __groupToCreateChunk;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreateWorld = builder
                    .WithAll<RenderBounds>()
                    .WithNone<RenderBoundsWorld>()
                    .Build(ref state);
            
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreateChunk = builder
                    .WithAll<RenderBoundsWorld>()
                    .WithNoneChunkComponent<RenderBoundsWorldChunk>()
                    .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            var entityManager = state.EntityManager;
            entityManager.AddComponent<RenderBoundsWorld>(__groupToCreateWorld);
            entityManager.AddComponent(__groupToCreateChunk, ComponentType.ChunkComponent<RenderBoundsWorldChunk>());
        }
    }
}