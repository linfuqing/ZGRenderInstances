using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.U2D;

#if UNITY_EDITOR
using UnityEditor;

[CreateAssetMenu(fileName = "SpriteAtlasDatabase", menuName = "ZG/Sprite Atlas Database")]
public class SpriteAtlasDatabase : ScriptableObject
{
    public SpriteAtlas[] spriteAtlases;

    public int GetTextureIndex(Sprite sprite)
    {
        int depth = 0;
        foreach (var spriteAtlas in spriteAtlases)
        {
            if (!spriteAtlas.CanBindTo(sprite))
            {
                depth += spriteAtlas.spriteCount;
                
                continue;
            }

            Sprite[] sprites;
            sprites = new Sprite[spriteAtlas.spriteCount];
            spriteAtlas.GetSprites(sprites);
            
            return Array.IndexOf(sprites, sprite) + depth;
        }

        return -1;
    }

    public void Build()
    {
        int width = 0, height = 0, depth = 0;
        TextureFormat format = TextureFormat.RGBA32;
        Sprite[] sprites;
        foreach (var spriteAtlas in spriteAtlases)
        {
            depth += spriteAtlas.spriteCount;
            
            sprites = new Sprite[spriteAtlas.spriteCount];
            spriteAtlas.GetSprites(sprites);

            foreach (var sprite in sprites)
            {
                width = Mathf.Max(width, sprite.texture.width);
                height = Mathf.Max(height, sprite.texture.height);
                
                format = sprite.texture.format;
            }
        }

        string path = AssetDatabase.GetAssetPath(this);
        var textures = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);

        if (textures != null && (width != textures.width || height != textures.height))
        {
            DestroyImmediate(textures);

            textures = null;
        }

        if (textures == null)
        {
            textures = new Texture2DArray(width, height, depth, format, true);
            
            textures.name = "SpriteAtlas";
            
            AssetDatabase.AddObjectToAsset(textures, path);
        }

        depth = 0;
        foreach (var spriteAtlas in spriteAtlases)
        {
            sprites = new Sprite[spriteAtlas.spriteCount];
            spriteAtlas.GetSprites(sprites);

            foreach (var sprite in sprites)
            {
                //TODO:mip
                Graphics.CopyTexture(sprite.texture, 0, 0, textures, depth++, 0);
            }
        }
        
        EditorUtility.SetDirty(textures);
    }

    private void OnValidate()
    {
        Build();
    }
}
#endif