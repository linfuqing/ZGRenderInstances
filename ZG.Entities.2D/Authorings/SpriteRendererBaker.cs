using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Entities.Content;
using Unity.Mathematics.Geometry;
using UnityEngine;

#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

namespace ZG
{
    public class SpriteRendererBaker : Baker<SpriteRenderer>
    {
        public override void Bake(SpriteRenderer authoring)
        {
            int subMeshIndex = SpriteAtlasDatabase.FindRenderData(
                authoring.sprite,
                out Mesh mesh,
                out Material material,
                out int textureIndex);

            if (subMeshIndex == -1)
                return;

            Entity entity = GetEntity(authoring, TransformUsageFlags.Renderable);

            var localBounds = authoring.localBounds;
            RenderBounds renderBounds;
            renderBounds.aabb = new MinMaxAABB(localBounds.min, localBounds.max);
            AddComponent(entity, renderBounds);
            
            RenderQueue renderQueue;
            renderQueue.value = (long)authoring.sortingOrder | ((long)material.renderQueue << 32);
            AddSharedComponent(entity, renderQueue);

            var sprite = authoring.sprite;
            RenderSharedData renderSharedData;
            renderSharedData.material = material;
            if (SpritePackingMode.Tight == sprite.packingMode)
            {
                renderSharedData.subMeshIndex = subMeshIndex;
                renderSharedData.mesh = mesh;
            }
            else
            {
                renderSharedData.subMeshIndex = 0;
                renderSharedData.mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                    "Packages/cn.com.zerogame.unitypackages.render-instances/ZG.Entities.2D/Meshes/QuadMesh.asset");
            }

            AddSharedComponent(entity, renderSharedData);

            SpriteRenderInstanceData instanceData;

            if (SpritePackingMode.Tight == sprite.packingMode)
            {
                instanceData.positionST = instanceData.uvST = new Vector4(1.0f, 1.0f, 0.0f, 0.0f);

                /*var rect = sprite.rect;
                instanceData.positionST.x = rect.width;
                instanceData.positionST.y = rect.height;
                instanceData.positionST.z = rect.x;
                instanceData.positionST.w = rect.y;*/
            }
            else
            {
                var texture = sprite.texture;
                float rTextureWidth = 1.0f / texture.width, rTextureHeight = 1.0f / texture.height;

                var textureRect = sprite.textureRect;
                instanceData.uvST.x = textureRect.width * rTextureWidth;
                instanceData.uvST.y = textureRect.height * rTextureHeight;

                instanceData.uvST.z = textureRect.x * rTextureWidth;
                instanceData.uvST.w = textureRect.y * rTextureHeight;

                if (sprite.packed)
                {
                    switch (sprite.packingRotation)
                    {
                        case SpritePackingRotation.FlipHorizontal:
                            instanceData.uvST.z += instanceData.uvST.x;
                            instanceData.uvST.x = -instanceData.uvST.x;
                            break;
                        case SpritePackingRotation.FlipVertical:
                            instanceData.uvST.w += instanceData.uvST.y;
                            instanceData.uvST.y = -instanceData.uvST.y;
                            break;
                        case SpritePackingRotation.Rotate180:
                            instanceData.uvST.z += instanceData.uvST.x;
                            instanceData.uvST.w += instanceData.uvST.y;
                            instanceData.uvST.x = -instanceData.uvST.x;
                            instanceData.uvST.y = -instanceData.uvST.y;
                            break;
                    }
                }

                var textureRectOffset = sprite.textureRectOffset;
                instanceData.positionST.x = textureRect.width;
                instanceData.positionST.y = textureRect.height;
                instanceData.positionST.z = textureRectOffset.x;
                instanceData.positionST.w = textureRectOffset.y;
            }

            var pivot = sprite.pivot;
            instanceData.positionST.z -= pivot.x;
            instanceData.positionST.w -= pivot.y;
            instanceData.positionST /= sprite.pixelsPerUnit;

            if (authoring.flipX)
            {
                instanceData.positionST.x = -instanceData.positionST.x;
                instanceData.positionST.z = -instanceData.positionST.z;
            }

            if (authoring.flipY)
            {
                instanceData.positionST.y = -instanceData.positionST.y;
                instanceData.positionST.w = -instanceData.positionST.w;
            }

            instanceData.color = (Vector4)authoring.color;
            instanceData.textureIndex = textureIndex;
            AddComponent(entity, instanceData);

            RenderConstantType constantType;
            constantType.index = TypeManager.GetTypeIndex<SpriteRenderInstanceData>();
            constantType.bufferName = "UnityInstancing_SpriteInstance";
            AddSharedComponent(entity, constantType);
        }
    }
}
#endif
