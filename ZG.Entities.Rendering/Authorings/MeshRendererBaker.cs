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
        
        public static void Bake(
            IBaker baker, 
            in Entity entity, 
            Renderer renderer, 
            Mesh mesh, 
            int subMeshStartIndex = 0, 
            System.Action<int, Entity> onInit = null)
        {
            //var entity = baker.GetEntity(authoring, TransformUsageFlags.Renderable);
            
            UnityEngine.Assertions.Assert.IsNotNull(renderer, baker.GetName());
            UnityEngine.Assertions.Assert.IsNotNull(mesh, baker.GetName());

            var material = renderer.sharedMaterial;
            
            material.enableInstancing = true;
            
            RenderSharedData renderSharedData;
            renderSharedData.subMeshIndex = subMeshStartIndex;
            renderSharedData.mesh = mesh;
            renderSharedData.material = material;
            renderSharedData.shader = material.shader;
            baker.AddSharedComponent(entity, renderSharedData);

            RenderQueue renderQueue;
            renderQueue.value = (long)material.renderQueue << 32;
            baker.AddSharedComponent(entity, renderQueue);

            var bounds = mesh.GetSubMesh(renderSharedData.subMeshIndex).bounds;
            
            RenderBounds renderBounds;
            renderBounds.aabb = MinMaxAABB.CreateFromCenterAndExtents(bounds.center, bounds.extents);
            baker.AddComponent(entity, renderBounds);

            int count = mesh.subMeshCount - renderSharedData.subMeshIndex - 1;
            if (count > 0)
            {
                using (var entities =
                       new NativeArray<Entity>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                {
                    bool isStatic = baker.IsStatic();
                    var flags = isStatic ? TransformUsageFlags.Renderable : TransformUsageFlags.Dynamic;

                    baker.CreateAdditionalEntities(entities, flags);

                    LocalToWorld localToWorld;
                    localToWorld.Value = renderer.transform.localToWorldMatrix;

                    int materialIndex = 0;
                    var materials = renderer.sharedMaterials;
                    foreach (var entityToRender in entities)
                    {
                        ++renderSharedData.subMeshIndex;

                        material = materials[++materialIndex];
                        
                        renderSharedData.material = material;
                        renderSharedData.shader = material.shader;

                        baker.AddSharedComponent(entityToRender, renderSharedData);

                        renderQueue.value = (long)material.renderQueue << 32;
                        baker.AddSharedComponent(entity, renderQueue);

                        bounds = mesh.GetSubMesh(renderSharedData.subMeshIndex).bounds;

                        renderBounds.aabb = MinMaxAABB.CreateFromCenterAndExtents(bounds.center, bounds.extents);
                        baker.AddComponent(entityToRender, renderBounds);

                        baker.SetComponent(entityToRender, localToWorld);
                    }

                    if (!isStatic)
                    {
                        Parent parent;
                        parent.Value = entity;

                        LocalTransform localTransform = LocalTransform.Identity;
                        foreach (var entityToRender in entities)
                        {
                            baker.SetComponent(entityToRender, localTransform);
                            baker.AddComponent(entityToRender, parent);
                        }
                    }

                    if (onInit != null)
                    {
                        int numEntities = entities.Length;
                        for(int i = 0; i < numEntities; ++i)
                            onInit(i + 1, entities[i]);
                    }
                }
            }
            
            if (onInit != null)
                onInit(0, entity);
        }
        
        public override void Bake(MeshRenderer authoring)
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
                return;
            
            Entity entity = GetEntity(authoring, TransformUsageFlags.Renderable);
            Bake(this, entity, authoring, meshFilter.sharedMesh, authoring.subMeshStartIndex);
        }
    }
}
#endif