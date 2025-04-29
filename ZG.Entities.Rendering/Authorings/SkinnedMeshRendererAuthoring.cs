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
                DynamicBuffer<InstanceSkinnedMeshRenderer> instanceSkinnedMeshRenderers)
            {
                if (!database.GetSkin(skinnedMeshRenderer, 
                        out var skin, 
                        out int rendererIndex))
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
                    InstanceSkinnedMeshRenderer instanceSkinnedMeshRenderer;
                    instanceSkinnedMeshRenderer.index = rendererIndex;
                    instanceSkinnedMeshRenderer.entity = entity;
                    instanceSkinnedMeshRenderers.Add(instanceSkinnedMeshRenderer);
                    
                    baker.AddComponent(entity, skinnedData);
                    baker.AddSharedComponent(entity, constantType);

                    sharedData.subMeshIndex = subMeshIndex;
                    sharedData.material = database.GetOrCreateMaterial(materials[subMeshIndex]);
                    baker.SetSharedComponent(entity, sharedData);
                });

                return true;
            }

            public static void Bake(IBaker baker, in Entity entity, GameObject gameObject, string defaultClipName)
            {
                var skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                int numSkinnedMeshRenderers = skinnedMeshRenderers.Length;
                if (numSkinnedMeshRenderers < 1)
                    return;

                var database = SkinnedMeshRendererDatabase.FindDatabase(gameObject);
                if (database == null)
                    return;

                InstanceAnimationDefinitionData animation;
                animation.definition = database.CreateAnimationDefinition(Allocator.Persistent);
                baker.AddBlobAsset(ref animation.definition, out _);

                baker.AddComponent(entity, animation);

                var renderers = baker.AddBuffer<InstanceSkinnedMeshRenderer>(entity);

                Bake(baker, entity, skinnedMeshRenderers[0], database, renderers);

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
                            Bake(baker, child, skinnedMeshRenderers[i], database, renderers);

                            baker.AddComponent(child, parent);

                            baker.SetComponent(child, localTransform);
                        }
                    }
                }
                
                
                int clipIndex = -1;
                if (!string.IsNullOrEmpty(defaultClipName))
                {
                    ref var definition = ref animation.definition.Value;
                    int numClips = definition.clips.Length;
                    for (int i = 0; i < numClips; ++i)
                    {
                        ref var clip = ref definition.clips[i];
                        if (clip.name == defaultClipName)
                        {
                            foreach (var renderer in renderers)
                            {
                                if (definition.renderers[renderer.index].IsInClip(i))
                                {
                                    clipIndex = i;
                                    
                                    break;
                                }
                            }

                            if(clipIndex != -1)
                                break;
                        }
                    }
                }

                if (clipIndex == -1)
                {
                    baker.AddComponent<InstanceAnimationStatus>(entity);
                    baker.SetComponentEnabled<InstanceAnimationStatus>(entity, false);
                }
                else
                {
                    InstanceAnimationStatus status;
                    status.clipIndex = clipIndex;
                    status.time = 0.0f;
                    baker.AddComponent(entity, status);
                }

            }

            public override void Bake(SkinnedMeshRendererAuthoring authoring)
            {
                Entity entity = GetEntity(authoring, TransformUsageFlags.Renderable);
                Bake(this, entity, authoring._prefab, authoring._defaultClipName);
            }
        }

        [SerializeField] 
        internal string _defaultClipName = "Idle";
        
        [SerializeField]
        internal GameObject _prefab;
    }
}
#endif
