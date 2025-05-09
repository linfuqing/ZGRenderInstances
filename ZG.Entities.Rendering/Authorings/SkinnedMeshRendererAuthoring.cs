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
        public class Baker : Baker<SkinnedMeshRendererAuthoring>
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
                {
                    Debug.LogError($"Cannot find skinned mesh renderer {skinnedMeshRenderer}.", skinnedMeshRenderer);

                    return false;
                }

                RenderConstantType constantType;
                constantType.bufferName = "UnityInstancing_SkinnedInstance";
                constantType.stableTypeHash = TypeManager.GetTypeInfo<RenderSkinnedData>().StableTypeHash;

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

            public static void Bake(
                IBaker baker, 
                in Entity entity, 
                GameObject gameObject, 
                string defaultClipName, 
                string enterClipName)
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
                
                ref var definition = ref animation.definition.Value;
                
                animation.clipStartIndex = definition.clips.Length;
                animation.clipCount = 0;
                
                foreach (var renderer in renderers)
                {
                    ref var rendererDefinition = ref definition.renderers[renderer.index];

                    animation.clipStartIndex = Mathf.Min(animation.clipStartIndex, rendererDefinition.clipStartIndex);
                    animation.clipCount =
                        Mathf.Max(animation.clipStartIndex + animation.clipCount,
                            rendererDefinition.clipStartIndex + rendererDefinition.clipCount) -
                        animation.clipStartIndex;
                }
                
                animation.defaultClipIndex = string.IsNullOrEmpty(defaultClipName)
                    ? -1
                    : definition.IndexOfClip(defaultClipName, animation.clipStartIndex, animation.clipCount);
                baker.AddComponent(entity, animation);

                int enterClipIndex = enterClipName == defaultClipName ? animation.defaultClipIndex : 
                    string.IsNullOrEmpty(enterClipName)
                        ? -1
                        : definition.IndexOfClip(enterClipName, animation.clipStartIndex, animation.clipCount);
                if (enterClipIndex == -1)
                {
                    baker.AddComponent<InstanceAnimationStatus>(entity);
                    baker.SetComponentEnabled<InstanceAnimationStatus>(entity, false);
                }
                else
                {
                    InstanceAnimationStatus status;
                    status.clipIndex = animation.defaultClipIndex;
                    status.time = 0.0f;
                    baker.AddComponent(entity, status);
                }
            }

            public override void Bake(SkinnedMeshRendererAuthoring authoring)
            {
                Entity entity = GetEntity(authoring, TransformUsageFlags.Renderable);
                Bake(this, 
                    entity, 
                    authoring._prefab, 
                    authoring._defaultClipName, 
                    authoring._enterClipName);
            }
        }

        [SerializeField] 
        internal string _defaultClipName = "Idle";
        
        [SerializeField] 
        internal string _enterClipName = "Idle";

        [SerializeField]
        internal GameObject _prefab;
    }
}
#endif
