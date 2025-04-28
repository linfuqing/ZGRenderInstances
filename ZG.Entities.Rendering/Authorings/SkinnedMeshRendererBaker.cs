using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics.Geometry;
using Unity.Transforms;
using UnityEngine;

#if UNITY_EDITOR
namespace ZG
{
    public class SkinnedMeshRendererAuthoring : MonoBehaviour
    {
        public class SkinnedMeshRendererBaker : Baker<SkinnedMeshRendererAuthoring>
        {
            public static bool Bake(
                IBaker baker,
                in Entity entity,
                SkinnedMeshRenderer skinnedMeshRenderer,
                SkinnedMeshRendererDatabase database,
                out int rendererIndex)
            {
                if (!database.GetSkin(skinnedMeshRenderer, 
                        out var skin, 
                        out rendererIndex))
                    return false;

                RenderConstantType constantType;
                constantType.bufferID = Shader.PropertyToID("UnityInstancing_SkinnedInstance");
                constantType.index = TypeManager.GetTypeIndex<RenderSkinnedData>();

                RenderSkinnedData skinnedData;
                skinnedData.depth = skin.depthIndex;
                skinnedData.pixelOffset = (uint)skin.pixelIndex;

                var materials = skinnedMeshRenderer.sharedMaterials;
                
                RenderSharedData sharedData;
                sharedData.material = database.GetOrCreateMaterial(materials[0]);
                sharedData.mesh = skinnedMeshRenderer.sharedMesh;
                MeshRendererBaker.Bake(baker, entity, skinnedMeshRenderer, sharedData.mesh, 0, (subMeshIndex, entity) =>
                {
                    baker.AddComponent(entity, skinnedData);
                    baker.AddSharedComponent(entity, constantType);

                    sharedData.subMeshIndex = subMeshIndex;
                    sharedData.material = database.GetOrCreateMaterial(materials[subMeshIndex]);
                    baker.SetSharedComponent(entity, sharedData);
                });

                return true;
            }

            public static void Bake(IBaker baker, in Entity entity, GameObject gameObject)
            {
                var skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                int numSkinnedMeshRenderers = skinnedMeshRenderers.Length;
                if (numSkinnedMeshRenderers < 1)
                    return;

                var database = SkinnedMeshRendererDatabase.FindDatabase(gameObject);
                if (database == null)
                    return;

                baker.AddComponent<InstanceAnimationStatus>(entity);
                baker.SetComponentEnabled<InstanceAnimationStatus>(entity, false);

                InstanceAnimationData animation;
                animation.definition = database.CreateAnimationDefinition(Allocator.Persistent);
                baker.AddBlobAsset(ref animation.definition, out _);

                baker.AddComponent(entity, animation);

                var renderers = baker.AddBuffer<InstanceSkinnedMeshRenderer>(entity);
                renderers.ResizeUninitialized(numSkinnedMeshRenderers);

                InstanceSkinnedMeshRenderer renderer;
                Bake(baker, entity, skinnedMeshRenderers[0], database, out renderer.index);
                renderer.entity = entity;
                renderers[0] = renderer;

                if (numSkinnedMeshRenderers > 1)
                {
                    using (var entityArray = new NativeArray<Entity>(numSkinnedMeshRenderers - 1, Allocator.Temp))
                    {
                        Parent parent;
                        parent.Value = entity;

                        LocalTransform localTransform = LocalTransform.Identity;

                        Entity child;
                        baker.CreateAdditionalEntities(entityArray,
                            TransformUsageFlags.Renderable | TransformUsageFlags.Dynamic);
                        for (int i = 1; i < numSkinnedMeshRenderers; ++i)
                        {
                            child = entityArray[i - 1];
                            Bake(baker, child, skinnedMeshRenderers[i], database, out renderer.index);
                            renderer.entity = child;
                            renderers[i] = renderer;

                            baker.AddComponent(child, parent);

                            baker.SetComponent(child, localTransform);
                        }
                    }
                }
            }

            public override void Bake(SkinnedMeshRendererAuthoring authoring)
            {
                Entity entity = GetEntity(authoring, TransformUsageFlags.Renderable);
                Bake(this, entity, authoring._prefab);
            }
        }
        
        [SerializeField]
        internal GameObject _prefab;
    }
}
#endif
