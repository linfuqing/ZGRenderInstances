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
        Entity entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

        var sharedMaterial = authoring.sharedMaterial;
        SpriteRenderMaterial material;
        material.time = 10.0f;
        material.value = new WeakObjectReference<Material>(sharedMaterial);
        AddSharedComponent(entity, material);

        var sprite = authoring.sprite;
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
        
        var texture = sprite.texture;
        float rTextureWidth = 1.0f / texture.width, rTextureHeight = 1.0f / texture.height;
        var textureRect = sprite.textureRect;
        instanceData.uvST.x = textureRect.width * rTextureWidth;
        instanceData.uvST.y = textureRect.height * rTextureHeight;

        var textureRectOffset = sprite.textureRectOffset;
        instanceData.uvST.z = (textureRectOffset.x + textureRect.x) * rTextureWidth;
        instanceData.uvST.w = (textureRectOffset.y + textureRect.y) * rTextureHeight;
        
        var textures = sharedMaterial.GetTexture("_Textures") as Texture2DArray;
        var database = textures == null
            ? null
            : AssetDatabase.LoadAssetAtPath<SpriteAtlasDatabase>(AssetDatabase.GetAssetPath(textures));
        instanceData.textureIndex = database == null ? 0 : database.GetTextureIndex(sprite);
        AddComponent(entity, instanceData);
    }
}
#endif
