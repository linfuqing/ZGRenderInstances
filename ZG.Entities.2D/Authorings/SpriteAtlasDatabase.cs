using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;

[CreateAssetMenu(fileName = "SpriteAtlasDatabase", menuName = "ZG/Sprite Atlas Database")]
public class SpriteAtlasDatabase : ScriptableObject
{
    public SpriteAtlas[] spriteAtlases;

    private Dictionary<Texture2D, int> __textureIndices;

    public int GetSubMesh(Sprite sprite, out Mesh mesh)
    {
        int result;
        Sprite[] sprites;
        foreach (var spriteAtlas in spriteAtlases)
        {
            sprites = new Sprite[spriteAtlas.spriteCount];
            spriteAtlas.GetSprites(sprites);

            result = Array.IndexOf(sprites, sprite);
            if(result == -1)
                continue;

            mesh = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(this)))
            {
                if (asset is Mesh temp && asset.name == spriteAtlas.name)
                {
                    mesh = temp;
                    
                    break;
                }
            }

            return result;
        }

        mesh = null;
        
        return -1;
    }

    public int GetTextureIndex(Sprite sprite)
    {
        if (__textureIndices == null)
        {
            __textureIndices = new Dictionary<Texture2D, int>();

            Texture2D spriteTexture;
            Sprite[] sprites;
            foreach (var spriteAtlas in spriteAtlases)
            {
                sprites = new Sprite[spriteAtlas.spriteCount];
                spriteAtlas.GetSprites(sprites);

                foreach (var spriteTemp in sprites)
                {
                    spriteTexture = spriteTemp.texture;
                    if(__textureIndices.ContainsKey(spriteTexture))
                        continue;
                    
                    __textureIndices.Add(spriteTexture, __textureIndices.Count);
                }
            }
        }

        return sprite != null && __textureIndices.TryGetValue(sprite.texture, out int textureIndex)
            ? textureIndex
            : -1;
    }

    public void Build()
    {
        bool isContains;
        string path = AssetDatabase.GetAssetPath(this);
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (var asset in assets)
        {
            if(asset == this)
                continue;

            isContains = false;
            foreach (var spriteAtlas in spriteAtlases)
            {
                if(spriteAtlas.name != asset.name)
                    continue;

                isContains = true;
                
                break;
            }
            
            if(!isContains)
                AssetDatabase.RemoveObjectFromAsset(asset);
            
            DestroyImmediate(asset, true);
        }
        
        AssetDatabase.SaveAssets();
        
        assets = AssetDatabase.LoadAllAssetsAtPath(path);

        __textureIndices = new Dictionary<Texture2D, int>();

        int width, height, depth, mipmapCount, i;
        TextureFormat format = TextureFormat.RGBA32;
        Texture2DArray textureArray;
        Texture2D texture;
        Mesh mesh;
        Sprite[] sprites = null;
        var textures = new List<Texture>();
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        var subMeshDescriptors = new List<SubMeshDescriptor>();
        foreach (var spriteAtlas in spriteAtlases)
        {
            width = 0;
            height = 0;
            depth = 0;
            textures.Clear();
            
            Array.Resize(ref sprites, spriteAtlas.spriteCount);
            spriteAtlas.GetSprites(sprites);

            vertices.Clear();
            uvs.Clear();
            triangles.Clear();
            foreach (var sprite in sprites)
            {
                foreach (var vertex in sprite.vertices)
                    vertices.Add(new Vector3(vertex.x, vertex.y, 0.0f));
                
                uvs.AddRange(sprite.uv);

                var spriteTriangles = sprite.triangles;
                subMeshDescriptors.Add(new SubMeshDescriptor(triangles.Count, spriteTriangles.Length));
                foreach (var triangle in spriteTriangles)
                    triangles.Add(triangle);
                
                texture = sprite.texture;
                if (__textureIndices.TryAdd(texture, __textureIndices.Count))
                {
                    width = Mathf.Max(width, texture.width);
                    height = Mathf.Max(height, texture.height);

                    ++depth;

                    format = texture.format;

                    textures.Add(texture);
                }
            }

            if (triangles.Count > 0)
            {
                mesh = new Mesh();
                mesh.name = spriteAtlas.name;
                mesh.vertices = vertices.ToArray();
                mesh.uv = uvs.ToArray();
                mesh.triangles = triangles.ToArray();
                mesh.SetSubMeshes(subMeshDescriptors);

                isContains = false;
                foreach (var asset in assets)
                {
                    if (asset is Mesh temp && temp.name == spriteAtlas.name)
                    {
                        EditorUtility.CopySerialized(mesh, temp);

                        EditorUtility.SetDirty(mesh);

                        DestroyImmediate(mesh);

                        mesh = temp;

                        isContains = true;

                        break;
                    }
                }
                
                if (!isContains)
                    AssetDatabase.AddObjectToAsset(mesh, path);
            }

            if (textures.Count > 0)
            {
                textureArray = new Texture2DArray(width, height, depth, format, true);
                textureArray.name = spriteAtlas.name;

                foreach (var textureToCopy in textures)
                {
                    mipmapCount = textureToCopy.mipmapCount;
                    for (i = 0; i < mipmapCount; ++i)
                        Graphics.CopyTexture(textureToCopy, 0, i, textureArray, depth, i);
                }

                isContains = false;
                foreach (var asset in assets)
                {
                    if (asset is Texture2DArray temp && temp.name == spriteAtlas.name)
                    {
                        EditorUtility.CopySerialized(textureArray, temp);

                        EditorUtility.SetDirty(textureArray);

                        DestroyImmediate(textureArray);

                        textureArray = temp;

                        isContains = true;

                        break;
                    }
                }

                if (!isContains)
                    AssetDatabase.AddObjectToAsset(textureArray, path);
            }
        }
    }

    private void OnValidate()
    {
        Build();
    }
}
#endif