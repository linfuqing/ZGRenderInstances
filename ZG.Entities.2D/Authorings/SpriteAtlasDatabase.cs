using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.U2D;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

[CreateAssetMenu(fileName = "SpriteAtlasDatabase", menuName = "ZG/Sprite Atlas Database")]
public class SpriteAtlasDatabase : ScriptableObject
{
    public SpriteAtlas[] spriteAtlases;

    private Dictionary<Texture, int> __textureIndices;

    public int GetTextureIndex(Sprite texture)
    {
        if (__textureIndices == null)
        {
            __textureIndices = new Dictionary<Texture, int>();

            Texture spriteTexture;
            Sprite[] sprites;
            foreach (var spriteAtlas in spriteAtlases)
            {
                sprites = new Sprite[spriteAtlas.spriteCount];
                spriteAtlas.GetSprites(sprites);

                foreach (var sprite in sprites)
                {
                    spriteTexture = sprite.texture;
                    if(__textureIndices.ContainsKey(spriteTexture))
                        continue;
                    
                    __textureIndices.Add(spriteTexture, __textureIndices.Count);
                }
            }
        }

        return texture != null && __textureIndices.TryGetValue(texture.texture, out int textureIndex)
            ? textureIndex
            : -1;
    }

    public void Build()
    {
        __textureIndices = new Dictionary<Texture, int>();

        int width = 0, height = 0;
        TextureFormat format = TextureFormat.RGBA32;
        Texture texture;
        Sprite[] sprites;
        foreach (var spriteAtlas in spriteAtlases)
        {
            sprites = new Sprite[spriteAtlas.spriteCount];
            spriteAtlas.GetSprites(sprites);

            foreach (var sprite in sprites)
            {
                texture = sprite.texture;
                if(__textureIndices.ContainsKey(texture))
                    continue;

                __textureIndices.Add(texture, __textureIndices.Count);
                
                width = Mathf.Max(width, texture.width);
                height = Mathf.Max(height, texture.height);
                
                format = sprite.texture.format;
            }
        }

        string path = AssetDatabase.GetAssetPath(this);
        var textures = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);

        int depth = textures.depth;
        if (textures != null && (width != textures.width || height != textures.height || depth != textures.depth))
        {
            DestroyImmediate(textures, true);

            textures = null;
        }

        if (textures == null)
        {
            textures = new Texture2DArray(width, height, depth, format, true);
            
            textures.name = "SpriteAtlas";
            
            AssetDatabase.AddObjectToAsset(textures, path);
        }

        foreach (var pair in __textureIndices)
        {
            //TODO:mip
            Graphics.CopyTexture(pair.Key, 0, 0, textures, pair.Value, 0);
        }
        
        EditorUtility.SetDirty(textures);
    }

    private void OnValidate()
    {
        Build();
    }
}
#endif