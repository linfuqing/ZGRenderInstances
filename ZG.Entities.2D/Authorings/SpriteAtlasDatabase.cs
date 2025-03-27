using System;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.U2D;

[CreateAssetMenu(fileName = "SpriteAtlasDatabase", menuName = "ZG/Sprite Atlas Database")]
public class SpriteAtlasDatabase : ScriptableObject
{
    public float releaseTime = 10.0f;
    public Shader shader;
    public SpriteAtlas[] spriteAtlases;

    private Dictionary<Texture2D, int> __textureIndices;

    public static int FindRenderData(
        Sprite sprite, 
        out Mesh mesh, 
        out Material material, 
        out int textureIndex, 
        out float releaseTime)
    {
        mesh = null;
        material = null;
        textureIndex = -1;
        releaseTime = 0.0f;
        
        int result;
        SpriteAtlasDatabase database;
        var guids = AssetDatabase.FindAssets("t:SpriteAtlasDatabase");
        foreach (var guid in guids)
        {
            database = AssetDatabase.LoadAssetAtPath<SpriteAtlasDatabase>(AssetDatabase.GUIDToAssetPath(guid));
            result = database.GetRenderData(sprite, out mesh, out material);

            if (result != -1)
            {
                textureIndex = database.GetTextureIndex(sprite);

                releaseTime = database.releaseTime;
                
                return result;
            }
        }

        return -1;
    }
    
    public int GetRenderData(Sprite sprite, out Mesh mesh, out Material material)
    {
        mesh = null;
        material = null;
        
        int result, numSprites;
        var spriteID = sprite.GetSpriteID();
        Sprite[] sprites = null;
        foreach (var spriteAtlas in spriteAtlases)
        {
            if(!spriteAtlas.CanBindTo(sprite))
                continue;
            
            numSprites = spriteAtlas.spriteCount;
            Array.Resize(ref sprites, numSprites);
            spriteAtlas.GetSprites(sprites);

            result = -1;
            for(int i = 0; i < numSprites; ++i)
            {
                if(sprites[i].GetSpriteID() == spriteID)
                {
                    result = i;

                    break;
                }
            }
            UnityEngine.Assertions.Assert.AreNotEqual(-1, result);
            
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(this)))
            {
                if(asset.name != spriteAtlas.name)
                    continue;
                
                if (asset is Mesh meshTemp)
                {
                    mesh = meshTemp;
                    
                    if(material != null)
                        break;
                }
                
                if (asset is Material materialTemp)
                {
                    material = materialTemp;
                    
                    if(mesh != null)
                        break;
                }
            }

            return result;
        }

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

        int width, height, depth, mipmapCount, i, j;
        TextureFormat format = TextureFormat.RGBA32;
        Material material;
        Texture2DArray textureArray;
        Texture2D texture;
        Mesh mesh;
        Sprite[] sprites = null;
        var textures = new List<Texture2D>();
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
            subMeshDescriptors.Clear();
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

                    format = texture.format;

                    textures.Add(texture);
                }
            }

            if (subMeshDescriptors.Count > 0)
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

            depth = textures.Count;
            if (depth > 0)
            {
                textureArray = new Texture2DArray(width, height, depth, format, true);
                textureArray.name = spriteAtlas.name;

                for(i = 0; i < depth; ++i)
                {
                    texture = textures[i];
                    mipmapCount = texture.mipmapCount;
                    for (j = 0; j < mipmapCount; ++j)
                        Graphics.CopyTexture(texture, 0, j, textureArray, i, j);
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
                
                material = null;
                foreach (var asset in assets)
                {
                    if (asset is Material temp && temp.name == spriteAtlas.name)
                    {
                        material = temp;

                        break;
                    }
                }

                if (material == null)
                {
                    material = new Material(shader);
                    material.name = spriteAtlas.name;
                    
                    AssetDatabase.AddObjectToAsset(material, path);
                }
                
                material.mainTexture = textureArray;
                material.enableInstancing = true;
                
                EditorUtility.SetDirty(material);
            }
        }
    }
}

[CustomEditor(typeof(SpriteAtlasDatabase))]
public class SpriteAtlasDatabaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        
        if(GUILayout.Button("Build"))
            ((SpriteAtlasDatabase)target).Build();
    }
}
#endif