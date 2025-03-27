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
        var sharedMaterial = authoring.sharedMaterial;
        var textures = sharedMaterial.mainTexture as Texture2DArray;
        var database = textures == null
            ? null
            : AssetDatabase.LoadAssetAtPath<SpriteAtlasDatabase>(AssetDatabase.GetAssetPath(textures));
        
        Entity entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

        var sprite = authoring.sprite;
        SpriteRenderSharedData sharedData;
        sharedData.material.releaseTime = 10.0f;
        sharedData.material.value = new WeakObjectReference<Material>(sharedMaterial);
        sharedData.mesh.releaseTime = 10.0f;
        if (SpritePackingMode.Tight == sprite.packingMode)
        {
            sharedData.subMeshIndex = database.GetSubMesh(sprite, out var mesh);
            sharedData.mesh.value = new WeakObjectReference<Mesh>(mesh);
        }
        else
        {
            sharedData.subMeshIndex = 0;
            sharedData.mesh.value = default;
        }

        AddSharedComponent(entity, sharedData);

        var rect = sprite.rect;
        var pivot = sprite.pivot;
        SpriteRenderInstanceData instanceData;
        instanceData.positionST.x = rect.width;
        instanceData.positionST.y = rect.height;
        instanceData.positionST.z = rect.x - pivot.x;
        instanceData.positionST.w = rect.y - pivot.y;
        instanceData.positionST /= sprite.pixelsPerUnit;

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

        instanceData.textureIndex = database == null ? 0 : database.GetTextureIndex(sprite);
        AddComponent(entity, instanceData);
    }
}
#endif
