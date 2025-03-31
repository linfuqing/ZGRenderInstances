using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
public class SpriteRendererBaker : Baker<SpriteRenderer>
{
    public override void Bake(SpriteRenderer authoring)
    {
        int subMeshIndex = SpriteAtlasDatabase.FindRenderData(
            authoring.sprite, 
            out Mesh mesh, 
            out Material material,
            out int textureIndex, 
            out float releaseTime);

        if (subMeshIndex == -1)
            return;
        
        Entity entity = GetEntity(authoring, TransformUsageFlags.Renderable);

        var sprite = authoring.sprite;
        RenderSharedData renderSharedData;
        renderSharedData.material.releaseTime = releaseTime;
        renderSharedData.material.value = new WeakObjectReference<Material>(material);
        renderSharedData.mesh.releaseTime = releaseTime;
        if (SpritePackingMode.Tight == sprite.packingMode)
        {
            renderSharedData.subMeshIndex = subMeshIndex;
            renderSharedData.mesh.value = new WeakObjectReference<Mesh>(mesh);
        }
        else
        {
            renderSharedData.subMeshIndex = 0;
            renderSharedData.mesh.value = default;
        }

        AddSharedComponent(entity, renderSharedData);

        var rect = sprite.rect;
        var pivot = sprite.pivot;
        SpriteRenderInstanceData instanceData;
        instanceData.positionST.x = rect.width;
        instanceData.positionST.y = rect.height;
        instanceData.positionST.z = rect.x - pivot.x;
        instanceData.positionST.w = rect.y - pivot.y;
        instanceData.positionST /= sprite.pixelsPerUnit;

        switch (sprite.packingRotation)
        {
            case SpritePackingRotation.FlipHorizontal:
                instanceData.positionST.x = -instanceData.positionST.x;
                instanceData.positionST.z = -instanceData.positionST.z;
                break;
            case SpritePackingRotation.FlipVertical:
                instanceData.positionST.y = -instanceData.positionST.y;
                instanceData.positionST.w = -instanceData.positionST.w;
                break;
            case SpritePackingRotation.Rotate180:
                Quaternion rotation = Quaternion.Euler(0, 0, 180);

                instanceData.positionST.xz = (Vector2)(rotation * (Vector2)instanceData.positionST.xz);
                instanceData.positionST.zw = (Vector2)(rotation * (Vector2)instanceData.positionST.zw);
                break;
        }
        
        /*var uv = sprite.uv;
        instanceData.uvST.x = uv[1].x - uv[0].x;
        instanceData.uvST.y = uv[0].y - uv[2].y;
        instanceData.uvST.z = uv[2].x;
        instanceData.uvST.w = uv[2].y;*/

        if (SpritePackingMode.Tight == sprite.packingMode)
            instanceData.uvST = new Vector4(1.0f, 1.0f, 0.0f, 0.0f);
        else
        {
            var texture = sprite.texture;
            float rTextureWidth = 1.0f / texture.width, rTextureHeight = 1.0f / texture.height;
            var textureRect = sprite.textureRect;
            instanceData.uvST.x = textureRect.width * rTextureWidth;
            instanceData.uvST.y = textureRect.height * rTextureHeight;

            var textureRectOffset = sprite.textureRectOffset;
            instanceData.uvST.z = (textureRectOffset.x + textureRect.x) * rTextureWidth;
            instanceData.uvST.w = (textureRectOffset.y + textureRect.y) * rTextureHeight;
        }

        instanceData.textureIndex = textureIndex;
        AddComponent(entity, instanceData);
    }
}
#endif
