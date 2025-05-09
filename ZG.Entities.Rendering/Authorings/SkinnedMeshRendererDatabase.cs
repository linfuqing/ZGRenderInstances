using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Hash128 = UnityEngine.Hash128;

#if UNITY_EDITOR
using System.Linq;
using System;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
namespace ZG
{
    [CreateAssetMenu(menuName = "ZG/Skinned Mesh Renderer Database")]
    public class SkinnedMeshRendererDatabase : ScriptableObject
    {
        [Serializable]
        public struct Clip
        {
            public string name;
            public WrapMode wrapMode;
            public int startFrame;
            public int frameCount;
        }

        [Serializable]
        public struct Skin
        {
            public int depthIndex;
            public int pixelIndex;
            public int pixelCount;
            public int pixelCountPerFrame;
        }
        
        [Serializable]
        internal struct Renderer
        {
            public Skin skin;
            public Hash128 hash;
            
            public int clipIndex;
            public int clipCount;
        }

        public const int BONE_MATRIX_ROW_COUNT = 3;

        [SerializeField] 
        internal int _targetFrameRate = 30;

        [SerializeField] 
        internal int _maxTextureWidth = 2048;
        
        [SerializeField] 
        internal int _maxTextureHeight = 2048;

        [SerializeField] 
        internal string _textureName = "_AnimationMap";

        [SerializeField] 
        internal string _textureTexelSizeName = "_AnimationMap_TexelSize";

        [SerializeField] 
        internal Material _material;

        [SerializeField]
        internal GameObject[] _gameObjects;

        [SerializeField]
        internal Renderer[] _renderers;

        [SerializeField]
        internal Clip[] _clips;

        private Dictionary<Hash128, int> __renderIndices;
        
        public static SkinnedMeshRendererDatabase FindDatabase(GameObject gameObject)
        {
            SkinnedMeshRendererDatabase database;
            var guids = AssetDatabase.FindAssets("t:SkinnedMeshRendererDatabase");
            foreach (var guid in guids)
            {
                database = AssetDatabase.LoadAssetAtPath<SkinnedMeshRendererDatabase>(AssetDatabase.GUIDToAssetPath(guid));

                if(database != null && database._gameObjects != null && Array.IndexOf(database._gameObjects, gameObject) != -1)
                    return database;
            }

            return null;
        }

        public static int CalculatedTexturePixels(float clipLength, int boneLength, int targetFrameRate)
        {
            var boneMatrixCount = BONE_MATRIX_ROW_COUNT * boneLength;

            return boneMatrixCount * (int)(clipLength * targetFrameRate);
        }

        public static Skin CalculatedSkin(
            in ReadOnlySpan<AnimationClip> animationClips, 
            int boneLength,
            int targetFrameRate, 
            int maxTextureWidth, 
            int maxTextureHeight, 
            ref int textureWidth, 
            ref int textureHeight, 
            ref int textureDepth, 
            ref int totalPixels, 
            List<int> textureIndices)
        {
            Skin result;
            result.pixelCount = BONE_MATRIX_ROW_COUNT * boneLength;

            int numClips = animationClips.Length;
            //result.clips = new Clip[numClips];

            //int frameIndex = 1;
            float clipLength;
            AnimationClip animationClip;
            for (int i = 0; i < numClips; ++i)
            {
                animationClip = animationClips[i];
                clipLength = animationClip.length;
                
                result.pixelCount += CalculatedTexturePixels(clipLength, boneLength, targetFrameRate);

                /*ref var clip = ref result.clips[i];

                clip.name = animationClip.name;
                clip.wrapMode = animationClip.wrapMode;
                clip.frameCount = (int)(clipLength * targetFrameRate);
                clip.startFrame = frameIndex;

                frameIndex += clip.frameCount;*/
            }

            if (textureDepth < 2)
            {
                result.depthIndex = textureDepth - 1;
                result.pixelIndex = totalPixels;
                
                totalPixels += result.pixelCount;
                while (textureWidth * textureHeight < totalPixels)
                {
                    if (textureWidth > textureHeight && (textureHeight << 1) <= maxTextureHeight)
                        textureHeight <<= 1;
                    else if ((textureWidth << 1) <= maxTextureWidth)
                        textureWidth <<= 1;
                    else
                    {
                        UnityEngine.Assertions.Assert.IsTrue(textureWidth * textureHeight >= result.pixelCount);

                        textureIndices.Add(totalPixels - result.pixelCount);
                            
                        textureIndices.Add(result.pixelCount);

                        result.pixelIndex = 0;

                        result.depthIndex = textureDepth++;
                        
                        break;
                    }
                }
            }
            else
            {
                int textureSize = textureWidth * textureHeight;
                UnityEngine.Assertions.Assert.IsTrue(textureSize >= result.pixelCount);

                result.pixelIndex = 0;
                
                int i;
                for (i = 0; i < textureDepth; ++i)
                {
                    result.pixelIndex = textureIndices[i];
                    if (textureSize - result.pixelIndex >= result.pixelCount)
                        break;
                }

                if (i < textureDepth)
                {
                    textureIndices[i] += result.pixelCount;

                    result.depthIndex = i;
                }
                else
                {
                    textureIndices.Add(result.pixelCount);

                    result.pixelIndex = 0;
                    
                    result.depthIndex = textureDepth++;
                }
            }

            result.pixelCountPerFrame = boneLength * BONE_MATRIX_ROW_COUNT;
            return result;
        }

        public static void GenerateAnimationTexture(
            IEnumerable<AnimationClip> clips,
            SkinnedMeshRenderer smr,
            GameObject targetObject,
            int targetFrameRate,
            ref Span<Color> pixels)
        {
            int pixelIndex = 0;
            var bones = smr.bones;
            var bindposes = smr.sharedMesh.bindposes;
            //Setup 0 to bindPoses
            foreach (var boneMatrix in bones.Select((b, idx) => b.localToWorldMatrix * bindposes[idx]))
            {
                pixels[pixelIndex++] = new Color(boneMatrix.m00, boneMatrix.m01, boneMatrix.m02, boneMatrix.m03);
                pixels[pixelIndex++] = new Color(boneMatrix.m10, boneMatrix.m11, boneMatrix.m12, boneMatrix.m13);
                pixels[pixelIndex++] = new Color(boneMatrix.m20, boneMatrix.m21, boneMatrix.m22, boneMatrix.m23);
            }

            foreach (var clip in clips)
            {
                var totalFrames = (int)(clip.length * targetFrameRate);
                foreach (var frame in Enumerable.Range(0, totalFrames))
                {
                    clip.SampleAnimation(targetObject, (float)frame / targetFrameRate);

                    foreach (var boneMatrix in bones.Select((b, idx) => b.localToWorldMatrix * bindposes[idx]))
                    {
                        pixels[pixelIndex++] =
                            new Color(boneMatrix.m00, boneMatrix.m01, boneMatrix.m02, boneMatrix.m03);
                        pixels[pixelIndex++] =
                            new Color(boneMatrix.m10, boneMatrix.m11, boneMatrix.m12, boneMatrix.m13);
                        pixels[pixelIndex++] =
                            new Color(boneMatrix.m20, boneMatrix.m21, boneMatrix.m22, boneMatrix.m23);
                    }
                }
            }
        }

        public static Hash128 GenerateSkinHash(
            IEnumerable<AnimationClip> clips,
            SkinnedMeshRenderer smr,
            GameObject targetObject,
            int targetFrameRate, 
            ref Color[] pixels)
        {
            int boneLength = smr.bones.Length, pixelCount = BONE_MATRIX_ROW_COUNT * boneLength;
            foreach (var clip in clips)
                pixelCount += CalculatedTexturePixels(clip.length, boneLength, targetFrameRate);

            if(pixels == null || pixels.Length < pixelCount)
                Array.Resize(ref pixels, pixelCount);
            
            var span = pixels.AsSpan(0, pixelCount);
            GenerateAnimationTexture(clips, smr, targetObject, targetFrameRate, ref span);
            
            return Hash128.Compute(pixels, 0, pixelCount);
        }

        public Hash128 GenerateSkinHash(SkinnedMeshRenderer skinnedMeshRenderer)
        {
            var animator = skinnedMeshRenderer.GetComponentInParent<Animator>(true);
            if (animator == null)
            {
                EditorUtility.DisplayDialog("Warning", "Selected object does not have Animator.", "OK");
                return default;
            }

            Color[] pixels = null;
            return GenerateSkinHash(animator.runtimeAnimatorController.animationClips, skinnedMeshRenderer,
                animator.gameObject, _targetFrameRate, ref pixels);
        }
        
        public void Rebuild(SkinnedMeshRenderer[] skinnedMeshRenderers)
        {
            __renderIndices = new Dictionary<Hash128, int>();

            int textureWidth = 1, 
                textureHeight = 1, 
                textureDepth = 1, 
                totalPixels = 0, 
                clipStartIndex,
                frameIndex;
            Hash128 hash;
            Animator animator;
            Skin skin;
            Clip clip;
            AnimationClip[] animationClips;
            Color[] pixelsTemp = null;
            var textureIndices = new List<int>();
            var clips = new List<Clip>();
            var clipStartIndices = new Dictionary<Animator, int>();
            var skinnedMeshRendererHashes = new Dictionary<SkinnedMeshRenderer, Hash128>();
            var skins = new List<Skin>();
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                foreach (var material in skinnedMeshRenderer.sharedMaterials)
                    GetOrCreateMaterial(material);
                
                animator = skinnedMeshRenderer.GetComponentInParent<Animator>(true);
                if (animator == null)
                {
                    EditorUtility.DisplayDialog("Warning", "Selected object does not have Animator.", "OK");
                    continue;
                }

                animationClips = animator.runtimeAnimatorController.animationClips;
                
                hash = GenerateSkinHash(animationClips, skinnedMeshRenderer, animator.gameObject, _targetFrameRate, ref pixelsTemp);
                if(__renderIndices.ContainsKey(hash))
                    continue;

                __renderIndices[hash] = skins.Count;
                
                skinnedMeshRendererHashes[skinnedMeshRenderer] = hash;
                
                if (!clipStartIndices.TryGetValue(animator, out clipStartIndex))
                {
                    clipStartIndex = clips.Count;

                    clipStartIndices[animator] = clipStartIndex;
                    
                    frameIndex = 1;

                    foreach (var animationClip in animationClips)
                    {
                        clip.name = animationClip.name;
                        clip.wrapMode = animationClip.wrapMode;
                        clip.frameCount = (int)(animationClip.length * _targetFrameRate);
                        clip.startFrame = frameIndex;
                        
                        clips.Add(clip);

                        frameIndex += clip.frameCount;
                    }
                }

                skin = CalculatedSkin(
                    animationClips, 
                    skinnedMeshRenderer.bones.Length, 
                    _targetFrameRate, 
                    _maxTextureWidth, 
                    _maxTextureHeight, 
                    ref textureWidth, 
                    ref textureHeight, 
                    ref textureDepth, 
                    ref totalPixels, 
                    textureIndices);

                skins.Add(skin);
            }
            
            _clips = clips.ToArray();
            
            var textures = new Texture2DArray(
                textureWidth, 
                textureHeight, 
                textureDepth, 
                TextureFormat.RGBAHalf,
                false, 
                true);

            textures.name = name;

            _renderers = new Renderer[skins.Count];
            
            int textureSize = textureWidth * textureHeight, rendererIndex = 0;
            Span<Color> subPixels;
            Color[] pixels;
            var pixelColors = new Color[textureDepth][];
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                if(!skinnedMeshRendererHashes.TryGetValue(skinnedMeshRenderer, out hash))
                    continue;
                
                animator = skinnedMeshRenderer.GetComponentInParent<Animator>(true);
                if (animator == null)
                    continue;

                skin = skins[__renderIndices[hash]];

                pixels = pixelColors[skin.depthIndex];
                if (pixels == null)
                {
                    pixels = new Color[textureSize];
                    
                    pixelColors[skin.depthIndex] = pixels;
                }

                subPixels = pixels.AsSpan(skin.pixelIndex, skin.pixelCount);

                animationClips = animator.runtimeAnimatorController.animationClips;
                GenerateAnimationTexture(
                    animationClips,
                    skinnedMeshRenderer,
                    animator.gameObject,
                    _targetFrameRate,
                    ref subPixels);

                ref var renderer = ref _renderers[rendererIndex++];

                renderer.hash = hash;//Hash128.Compute(pixels, skin.pixelIndex, skin.pixelCount);

                renderer.skin = skin;

                renderer.clipIndex = clipStartIndices[animator];
                renderer.clipCount = animationClips.Length;
            }
            
            for(int i = 0; i < textureDepth; ++i)
                textures.SetPixels(pixelColors[i], i);
            
            textures.Apply();
            textures.filterMode = FilterMode.Point;

            EditorUtility.SetDirty(this);

            var oldTextures = AssetDatabase.LoadAssetAtPath<Texture2DArray>(AssetDatabase.GetAssetPath(this));
            if (oldTextures != null)
            {
                EditorUtility.CopySerialized(textures, oldTextures);
                
                EditorUtility.SetDirty(oldTextures);
                
                DestroyImmediate(textures, true);
            }
            else
                AssetDatabase.AddObjectToAsset(textures, this);
            
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(this)))
            {
                if (asset is Material material)
                {
                    material.SetTexture(_textureName, textures);
                    
                    material.SetVector(_textureTexelSizeName, new Vector4(1.0f / textureWidth, 1.0f / textureHeight, textureWidth, textureHeight));

                    material.enableInstancing = true;

                    EditorUtility.SetDirty(material);
                }
            }
        }

        public void Rebuild()
        {
            int numGameObjects = _gameObjects.Length;

            SkinnedMeshRenderer[] components;
            var skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
            for(int i = 0; i < numGameObjects; ++i)
            {
                components = _gameObjects[i].GetComponentsInChildren<SkinnedMeshRenderer>();

                skinnedMeshRenderers.AddRange(components);
            }

            Rebuild(skinnedMeshRenderers.ToArray());
        }

        public bool GetSkin(
            SkinnedMeshRenderer skinnedMeshRenderer, 
            out Skin skin, 
            out int rendererIndex)
        {
            if (__renderIndices == null)
            {
                __renderIndices = new Dictionary<Hash128, int>();
                int numRenderers = _renderers.Length;
                for (int i = 0; i < numRenderers; ++i)
                {
                    ref var renderer = ref _renderers[i];
                    
                    __renderIndices.Add(renderer.hash, i);
                }
            }

            bool result = __renderIndices.TryGetValue(GenerateSkinHash(skinnedMeshRenderer), out rendererIndex);
            if (result)
            {
                ref var renderer = ref _renderers[rendererIndex];
                skin = renderer.skin;
                
                return true;
            }

            skin = default;
            
            return false;
        }

        public Material GetOrCreateMaterial(Material material)
        {
            var texture = material.mainTexture;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(this)))
            {
                if (asset is Material materialAsset && materialAsset.mainTexture == texture)
                    return materialAsset;
            }
            
            var newMaterial = new Material(_material);
            newMaterial.mainTexture = texture;
            AssetDatabase.AddObjectToAsset(newMaterial, this);

            return newMaterial;
        }

        public BlobAssetReference<InstanceAnimationDefinition> CreateAnimationDefinition(
            in AllocatorManager.AllocatorHandle allocator)
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<InstanceAnimationDefinition>();
                root.frameCountPerSecond = _targetFrameRate;
                
                int numClips = _clips.Length;
                var clips = builder.Allocate(ref root.clips, numClips);
                for (int i = 0; i < numClips; ++i)
                {
                    ref var source = ref _clips[i];
                    ref var destination = ref clips[i];
                    
                    destination.name = source.name;
                    destination.wrapMode =  WrapMode.Loop == source.wrapMode ? InstanceAnimationDefinition.Clip.WrapMode.Loop : InstanceAnimationDefinition.Clip.WrapMode.Normal;
                    destination.startFrame = source.startFrame;
                    destination.frameCount = source.frameCount;
                }
                
                int numRenderers = _renderers.Length;
                var renderers = builder.Allocate(ref root.renderers, numRenderers);
                for (int i = 0; i < numRenderers; ++i)
                {
                    ref var source = ref _renderers[i];
                    ref var destination = ref renderers[i];
                    
                    destination.depthIndex = source.skin.depthIndex;
                    destination.pixelOffset =  source.skin.pixelIndex;
                    destination.pixelCountPerFrame = source.skin.pixelCountPerFrame;
                    destination.clipStartIndex = source.clipIndex;
                    destination.clipCount = source.clipCount;
                }
                
                return builder.CreateBlobAssetReference<InstanceAnimationDefinition>(allocator);
            }
        }
    }

    [CustomEditor(typeof(SkinnedMeshRendererDatabase))]
    public class SkinnedMeshRendererDatabaseDrawer : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Clear"))
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(target)))
                {
                    if (asset == target)
                        continue;
                    
                    DestroyImmediate(asset, true);
                }
            }
            
            if(GUILayout.Button("Rebuild"))
                ((SkinnedMeshRendererDatabase)target).Rebuild();
        }
    }
}
#endif