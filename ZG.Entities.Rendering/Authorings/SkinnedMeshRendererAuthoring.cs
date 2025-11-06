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
        [Serializable]
        internal struct RandomClip
        {
            public string name;
            public float chance;
        }
        
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
                    Debug.LogError($"Cannot find skinned mesh renderer {skinnedMeshRenderer.transform.root.name}.", skinnedMeshRenderer);

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
                //sharedData.material = database.GetOrCreateMaterial(materials[0]);
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
                    var material = database.GetOrCreateMaterial(materials[subMeshIndex]);
                    sharedData.material = material;
                    sharedData.shader = material.shader;
                    baker.SetSharedComponent(entity, sharedData);
                    
                    RenderQueue renderQueue;
                    renderQueue.value = (long)material.renderQueue << 32;
                    baker.SetSharedComponent(entity, renderQueue);
                });

                return true;
            }

            public static bool Bake(
                IBaker baker, 
                in Entity entity, 
                GameObject gameObject, 
                string defaultClipName, 
                string enterClipName, 
                out InstanceAnimationDefinitionData animation)
            {
                var skinnedMeshRenderers = gameObject == null ? null : gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                int numSkinnedMeshRenderers = skinnedMeshRenderers == null ? 0 : skinnedMeshRenderers.Length;
                if (numSkinnedMeshRenderers < 1)
                {
                    animation = default;

                    return false;
                }

                var database = SkinnedMeshRendererDatabase.FindDatabase(gameObject);
                if (database == null)
                {
                    animation = default;

                    return false;
                }

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
                    status.clipIndex = enterClipIndex;
                    status.time = 0.0f;
                    baker.AddComponent(entity, status);
                }

                return true;
            }

            public override void Bake(SkinnedMeshRendererAuthoring authoring)
            {
                if (authoring._prefab == null)
                {
                    Debug.LogError($"{authoring} has no prefab!");

                    return;
                }

                Entity entity = GetEntity(authoring, TransformUsageFlags.Renderable);
                if (!Bake(this,
                        entity,
                        authoring._prefab,
                        authoring._defaultClipName,
                        authoring._enterClipName,
                        out var animation))
                    return;
                
                int numEnterRandomClipNames = authoring._enterRandomClips == null ? 0 : authoring._enterRandomClips.Length;
                if (numEnterRandomClipNames > 0)
                {
                    var enterClips = AddBuffer<InstanceAnimationEnterClip>(entity);
                    enterClips.ResizeUninitialized(numEnterRandomClipNames);
                    for (int i = 0; i < numEnterRandomClipNames; ++i)
                    {
                        ref var source = ref authoring._enterRandomClips[i];
                        ref var destination = ref enterClips.ElementAt(i);
                        destination.index = animation.IndexOfClip(source.name);
                        destination.chance = source.chance;
                    }
                }
            }
        }

        [SerializeField] 
        internal string _defaultClipName = "Idle";
        
        [SerializeField] 
        internal string _enterClipName = "Idle";

        [SerializeField] 
        internal RandomClip[] _enterRandomClips;

        [SerializeField]
        internal GameObject _prefab;
    }
}
#endif
