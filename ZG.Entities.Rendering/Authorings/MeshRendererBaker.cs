using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics.Geometry;
using Unity.Transforms;
using UnityEngine;

#if UNITY_EDITOR
namespace ZG
{
    public class MeshRendererBaker : Baker<MeshRenderer>
    {
        public override void Bake(MeshRenderer authoring)
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                return;

            var mesh = meshFilter.sharedMesh;

            var entity = GetEntity(authoring, TransformUsageFlags.Renderable);

            RenderSharedData renderSharedData;
            renderSharedData.subMeshIndex = authoring.subMeshStartIndex;
            renderSharedData.mesh = mesh;
            renderSharedData.material = authoring.sharedMaterial;
            AddSharedComponent(entity, renderSharedData);

            var bounds = mesh.GetSubMesh(renderSharedData.subMeshIndex).bounds;
            
            RenderBounds renderBounds;
            renderBounds.aabb = MinMaxAABB.CreateFromCenterAndExtents(bounds.center, bounds.extents);
            AddComponent(entity, renderBounds);

            int count = mesh.subMeshCount - renderSharedData.subMeshIndex - 1;
            if (count > 0)
            {
                using (var entities =
                       new NativeArray<Entity>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                {
                    bool isStatic = IsStatic();
                    var flags = isStatic ? TransformUsageFlags.Renderable : TransformUsageFlags.Dynamic;

                    CreateAdditionalEntities(entities, flags);

                    LocalToWorld localToWorld;
                    localToWorld.Value = authoring.transform.localToWorldMatrix;

                    int materialIndex = 0;
                    var materials = authoring.sharedMaterials;
                    foreach (var entityToRender in entities)
                    {
                        ++renderSharedData.subMeshIndex;

                        renderSharedData.material = materials[++materialIndex];

                        AddSharedComponent(entityToRender, renderSharedData);
                        
                        bounds = mesh.GetSubMesh(renderSharedData.subMeshIndex).bounds;
                        
                        renderBounds.aabb = MinMaxAABB.CreateFromCenterAndExtents(bounds.center, bounds.extents);
                        AddComponent(entityToRender, renderBounds);

                        SetComponent(entityToRender, localToWorld);
                    }

                    if (!IsStatic())
                    {
                        Parent parent;
                        parent.Value = entity;

                        LocalTransform localTransform = LocalTransform.Identity;
                        foreach (var entityToRender in entities)
                        {
                            SetComponent(entityToRender, localTransform);
                            AddComponent(entityToRender, parent);
                        }
                    }
                }
            }
        }
    }
}
#endif