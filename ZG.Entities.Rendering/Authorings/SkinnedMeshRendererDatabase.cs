using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
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
            public int boneDataPixelIndex;
        }
        
        [Serializable]
        internal struct Renderer
        {
            public Skin skin;
            public Hash128 hash;
            
            public int clipIndex;
            public int clipCount;
        }

        private struct MeshWrapper : IDisposable
        {
            private Mesh __mesh;
            private ModelImporter __modelImporter;

            public MeshWrapper(Mesh mesh)
            {
                __mesh = mesh;

                var name = mesh.name;
                
                string path = AssetDatabase.GetAssetPath(mesh);
                __modelImporter = AssetDatabase.LoadAssetAtPath<ModelImporter>(path);
                
                if (__modelImporter != null)
                {
                    if (__modelImporter.isReadable)
                        __modelImporter = null;
                    else
                    {
                        __modelImporter.isReadable = true;

                        __modelImporter.SaveAndReimport();

                        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        {
                            if (asset is Mesh temp && temp.name == name)
                            {
                                __mesh = temp;

                                break;
                            }
                        }
                    }
                }
            }

            public void Dispose()
            {
                if (__modelImporter == null)
                    return;
                
                __modelImporter.isReadable = false;
                
                __modelImporter.SaveAndReimport();

                __modelImporter = null;
            }
            
            public static implicit operator Mesh(MeshWrapper wrapper)
            {
                return wrapper.__mesh;
            }
        }
        
        // Each bone matrix (float3x4) uses 6 texels with RGBA32 half-float encoding:
        // Each float4 row is encoded as 2 RGBA32 texels (2 half-floats per texel).
        public const int BONE_MATRIX_ROW_COUNT = 6;

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

        // Convert IEEE 754 float32 to float16 (half-precision).
        // Stores 2 half-floats per RGBA32 texel: R,G = first half, B,A = second half.
        public static ushort FloatToHalf(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            uint f = BitConverter.ToUInt32(bytes, 0);
            uint sign = (f >> 16) & 0x8000u;
            uint exponent = (f >> 23) & 0xFFu;
            uint mantissa = (f >> 13) & 0x3FFu;

            if (exponent == 0)
                return (ushort)sign; // Zero

            if (exponent == 0xFF)
                return (ushort)(sign | 0x7C00u); // Infinity/NaN

            int halfExp = (int)exponent - 127 + 15;
            if (halfExp <= 0)
                return (ushort)sign; // Underflow to zero
            if (halfExp >= 31)
                return (ushort)(sign | 0x7C00u); // Overflow to infinity

            return (ushort)(sign | ((uint)halfExp << 10) | mantissa);
        }

        // Encode a float4 as 2 consecutive Color32 pixels (2 half-floats per pixel).
        // Pixel 0: (R,G) = half(f0), (B,A) = half(f1)
        // Pixel 1: (R,G) = half(f2), (B,A) = half(f3)
        public static void EncodeFloat4ToColor32(float f0, float f1, float f2, float f3, out Color32 pixel0, out Color32 pixel1)
        {
            ushort h0 = FloatToHalf(f0);
            ushort h1 = FloatToHalf(f1);
            ushort h2 = FloatToHalf(f2);
            ushort h3 = FloatToHalf(f3);

            pixel0 = new Color32((byte)(h0 & 0xFF), (byte)(h0 >> 8), (byte)(h1 & 0xFF), (byte)(h1 >> 8));
            pixel1 = new Color32((byte)(h2 & 0xFF), (byte)(h2 >> 8), (byte)(h3 & 0xFF), (byte)(h3 >> 8));
        }

        public static int CalculatedTexturePixels(float clipLength, int boneLength, int targetFrameRate)
        {
            long boneMatrixCount = BONE_MATRIX_ROW_COUNT * (long)boneLength;
            long frameCount = (long)(clipLength * targetFrameRate);
            long total = boneMatrixCount * frameCount;
            if (total > int.MaxValue)
            {
                throw new OverflowException(
                    $"Animation texture pixel count overflow: clipLength={clipLength}, boneLength={boneLength}, targetFrameRate={targetFrameRate}.");
            }

            return (int)total;
        }

        public const int BONE_DATA_PIXELS_PER_VERTEX = 4;

        public static int GetSkinnedVertexCount(Mesh mesh)
        {
            if (mesh == null)
            {
                return 0;
            }

            var boneWeights = mesh.boneWeights;
            if (boneWeights != null && boneWeights.Length > 0)
            {
                return boneWeights.Length;
            }

            return mesh.vertexCount;
        }

        public static int GetBoneMatrixCount(int boneCount, int bindPoseCount)
        {
            if (bindPoseCount < boneCount)
            {
                Debug.LogWarning(
                    $"SkinnedMeshRenderer bone count ({boneCount}) exceeds mesh bindpose count ({bindPoseCount}). Using {bindPoseCount}.");
            }

            return bindPoseCount < boneCount ? bindPoseCount : boneCount;
        }

        public static int CalculateAnimationTexturePixelCount(
            int vertexCount,
            int boneCount,
            ReadOnlySpan<AnimationClip> animationClips,
            int targetFrameRate)
        {
            long pixelCount = BONE_DATA_PIXELS_PER_VERTEX * (long)vertexCount + BONE_MATRIX_ROW_COUNT * (long)boneCount;
            for (int i = 0; i < animationClips.Length; ++i)
            {
                pixelCount += CalculatedTexturePixels(animationClips[i].length, boneCount, targetFrameRate);
            }

            if (pixelCount > int.MaxValue)
            {
                throw new OverflowException(
                    $"Animation texture pixel count overflow: vertexCount={vertexCount}, boneCount={boneCount}, clipCount={animationClips.Length}.");
            }

            return (int)pixelCount;
        }

        public static int CalculateAnimationTexturePixelCount(
            int vertexCount,
            int boneCount,
            AnimationClip[] animationClips,
            int targetFrameRate)
        {
            return CalculateAnimationTexturePixelCount(
                vertexCount,
                boneCount,
                animationClips.AsSpan(),
                targetFrameRate);
        }

        private static void WriteBoneMatrixPixels(ref Span<Color32> pixels, ref int pixelIndex, in Matrix4x4 boneMatrix)
        {
            if (pixelIndex + BONE_MATRIX_ROW_COUNT > pixels.Length)
            {
                throw new IndexOutOfRangeException(
                    $"Animation texture write out of range at pixel {pixelIndex}, need {BONE_MATRIX_ROW_COUNT} pixels, buffer length {pixels.Length}.");
            }

            EncodeFloat4ToColor32(boneMatrix.m00, boneMatrix.m01, boneMatrix.m02, boneMatrix.m03,
                out pixels[pixelIndex], out pixels[pixelIndex + 1]);
            pixelIndex += 2;
            EncodeFloat4ToColor32(boneMatrix.m10, boneMatrix.m11, boneMatrix.m12, boneMatrix.m13,
                out pixels[pixelIndex], out pixels[pixelIndex + 1]);
            pixelIndex += 2;
            EncodeFloat4ToColor32(boneMatrix.m20, boneMatrix.m21, boneMatrix.m22, boneMatrix.m23,
                out pixels[pixelIndex], out pixels[pixelIndex + 1]);
            pixelIndex += 2;
        }

        public static Skin CalculatedSkin(
            in ReadOnlySpan<AnimationClip> animationClips, 
            int boneLength,
            int vertexCount,
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
            
            result.pixelCount = CalculateAnimationTexturePixelCount(
                vertexCount,
                boneLength,
                animationClips,
                targetFrameRate);

            int boneDataPixelCount = BONE_DATA_PIXELS_PER_VERTEX * vertexCount;

            if (textureDepth < 2)
            {
                result.depthIndex = textureDepth - 1;
                result.boneDataPixelIndex = totalPixels;
                result.pixelIndex = totalPixels + boneDataPixelCount;
                
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

                        result.boneDataPixelIndex = 0;
                        result.pixelIndex = boneDataPixelCount;

                        result.depthIndex = textureDepth++;
                        
                        break;
                    }
                }
            }
            else
            {
                int textureSize = textureWidth * textureHeight;
                UnityEngine.Assertions.Assert.IsTrue(textureSize >= result.pixelCount);

                result.boneDataPixelIndex = 0;
                result.pixelIndex = boneDataPixelCount;
                
                int i;
                for (i = 0; i < textureDepth; ++i)
                {
                    result.boneDataPixelIndex = textureIndices[i];
                    if (textureSize - result.boneDataPixelIndex >= result.pixelCount)
                        break;
                }

                if (i < textureDepth)
                {
                    result.pixelIndex = result.boneDataPixelIndex + boneDataPixelCount;
                    textureIndices[i] += result.pixelCount;

                    result.depthIndex = i;
                }
                else
                {
                    textureIndices.Add(result.pixelCount);

                    result.boneDataPixelIndex = 0;
                    result.pixelIndex = boneDataPixelCount;
                    
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
            ref Span<Color32> pixels)
        {
            AnimationClip[] clipArray = clips as AnimationClip[] ?? clips.ToArray();

            BoneWeight[] boneWeights;
            Matrix4x4[] bindposes;
            using (var meshWrapper = new MeshWrapper(smr.sharedMesh))
            {
                Mesh sharedMesh = meshWrapper;
                // Write per-vertex bone data (indices + weights) at the beginning
                // Use bakedMesh instead of smr.sharedMesh to avoid accessing read-only mesh properties
                boneWeights = sharedMesh.boneWeights ?? Array.Empty<BoneWeight>();
                bindposes = sharedMesh.bindposes ?? Array.Empty<Matrix4x4>();
            }

            int vertexCount = boneWeights.Length > 0 ? boneWeights.Length : GetSkinnedVertexCount(smr.sharedMesh);
            var bones = smr.bones ?? Array.Empty<Transform>();
            int boneCount = GetBoneMatrixCount(bones.Length, bindposes.Length);
            int requiredPixelCount = CalculateAnimationTexturePixelCount(
                vertexCount,
                boneCount,
                clipArray,
                targetFrameRate);
            if (pixels.Length < requiredPixelCount)
            {
                throw new IndexOutOfRangeException(
                    $"Animation texture buffer too small: need {requiredPixelCount}, got {pixels.Length}. " +
                    $"vertexCount={vertexCount}, boneCount={boneCount}, clipCount={clipArray.Length}.");
            }

            int pixelIndex = 0;

            for (int v = 0; v < boneWeights.Length; v++)
            {
                if (pixelIndex + BONE_DATA_PIXELS_PER_VERTEX > pixels.Length)
                {
                    throw new IndexOutOfRangeException(
                        $"Animation texture write out of range while writing bone weights at vertex {v}, pixel {pixelIndex}.");
                }

                var bw = boneWeights[v];
                // Pixel pair 1: bone indices (idx0, idx1, idx2, idx3) as half-float
                EncodeFloat4ToColor32(bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3,
                    out pixels[pixelIndex], out pixels[pixelIndex + 1]);
                pixelIndex += 2;
                // Pixel pair 2: bone weights (w0, w1, w2, w3) as half-float
                EncodeFloat4ToColor32(bw.weight0, bw.weight1, bw.weight2, bw.weight3,
                    out pixels[pixelIndex], out pixels[pixelIndex + 1]);
                pixelIndex += 2;
            }
            
            // Setup bind pose (frame 0)
            for (int boneIndex = 0; boneIndex < boneCount; ++boneIndex)
            {
                var bone = bones[boneIndex];
                if (bone == null)
                {
                    throw new InvalidOperationException(
                        $"SkinnedMeshRenderer bone at index {boneIndex} is null while baking animation texture.");
                }

                WriteBoneMatrixPixels(ref pixels, ref pixelIndex, bone.localToWorldMatrix * bindposes[boneIndex]);
            }

            for (int clipIndex = 0; clipIndex < clipArray.Length; ++clipIndex)
            {
                var clip = clipArray[clipIndex];
                var totalFrames = (int)(clip.length * targetFrameRate);
                for (int frame = 0; frame < totalFrames; ++frame)
                {
                    clip.SampleAnimation(targetObject, (float)frame / targetFrameRate);

                    for (int boneIndex = 0; boneIndex < boneCount; ++boneIndex)
                    {
                        var bone = bones[boneIndex];
                        if (bone == null)
                        {
                            throw new InvalidOperationException(
                                $"SkinnedMeshRenderer bone at index {boneIndex} is null while sampling clip '{clip.name}'.");
                        }

                        WriteBoneMatrixPixels(ref pixels, ref pixelIndex, bone.localToWorldMatrix * bindposes[boneIndex]);
                    }
                }
            }

            if (pixelIndex != requiredPixelCount)
            {
                throw new InvalidOperationException(
                    $"Animation texture pixel count mismatch: wrote {pixelIndex}, expected {requiredPixelCount}.");
            }
        }

        public static Hash128 GenerateSkinHash(
            IEnumerable<AnimationClip> clips,
            SkinnedMeshRenderer smr,
            GameObject targetObject,
            int targetFrameRate, 
            ref Color32[] pixels)
        {
            AnimationClip[] clipArray = clips as AnimationClip[] ?? clips.ToArray();

            int vertexCount;
            int boneCount;
            using (var meshWrapper = new MeshWrapper(smr.sharedMesh))
            {
                Mesh sharedMesh = meshWrapper;
                vertexCount = GetSkinnedVertexCount(sharedMesh);
                boneCount = GetBoneMatrixCount(smr.bones.Length, sharedMesh.bindposes.Length);
            }

            int pixelCount = CalculateAnimationTexturePixelCount(
                vertexCount,
                boneCount,
                clipArray,
                targetFrameRate);

            if (pixels == null || pixels.Length < pixelCount)
            {
                Array.Resize(ref pixels, pixelCount);
            }
            
            var span = pixels.AsSpan(0, pixelCount);
            GenerateAnimationTexture(clipArray, smr, targetObject, targetFrameRate, ref span);
            
            return HashUtility.Compute((ReadOnlySpan<Color32>)span);
        }

        public Hash128 GenerateSkinHash(SkinnedMeshRenderer skinnedMeshRenderer)
        {
            var animator = skinnedMeshRenderer.GetComponentInParent<Animator>(true);
            if (animator == null)
            {
                EditorUtility.DisplayDialog("Warning", "Selected object does not have Animator.", "OK");
                return default;
            }

            Color32[] pixels = null;
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
            Color32[] pixelsTemp = null;
            var textureIndices = new List<int>();
            var clips = new List<Clip>();
            var clipStartIndices = new Dictionary<Animator, int>();
            var skinnedMeshRendererHashes = new Dictionary<SkinnedMeshRenderer, Hash128>();
            var skins = new List<Skin>();
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                foreach (var material in skinnedMeshRenderer.sharedMaterials)
                    GetOrCreateMaterial(material);
                
                GetOrCreateMesh(skinnedMeshRenderer);
                
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

                int skinVertexCount;
                int skinBoneCount;
                using (var meshWrapper = new MeshWrapper(skinnedMeshRenderer.sharedMesh))
                {
                    Mesh sharedMesh = meshWrapper;
                    skinVertexCount = GetSkinnedVertexCount(sharedMesh);
                    skinBoneCount = GetBoneMatrixCount(
                        skinnedMeshRenderer.bones.Length,
                        sharedMesh.bindposes.Length);
                }

                skin = CalculatedSkin(
                    animationClips, 
                    skinBoneCount, 
                    skinVertexCount,
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
            
            // Use RGBA32 + half-float encoding instead of RGBAFloat.
            // RGBAFloat Texture2DArray is unreliable on WebGL2/GLES3 (WeChat mini-game):
            //   - textureLod() returns zero for some texels on RGBAFloat
            //   - texelFetch() returns zero for all texels on RGBAFloat
            // RGBA32 is universally supported on all platforms including WebGL2.
            // Each float4 bone matrix row is encoded as 2 RGBA32 texels (2 half-floats per texel).
            var textures = new Texture2DArray(
                textureWidth, 
                textureHeight, 
                textureDepth, 
                TextureFormat.RGBA32,
                false, 
                true);

            textures.name = name;

            _renderers = new Renderer[skins.Count];
            
            int textureSize = textureWidth * textureHeight, rendererIndex = 0;
            Span<Color32> subPixels;
            Color32[] pixels;
            var pixelColors = new Color32[textureDepth][];
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
                    pixels = new Color32[textureSize];
                    
                    pixelColors[skin.depthIndex] = pixels;
                }

                subPixels = pixels.AsSpan(skin.boneDataPixelIndex, skin.pixelCount);

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
                textures.SetPixels32(pixelColors[i], i);
            
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

            var hash = GenerateSkinHash(skinnedMeshRenderer);
            bool result = __renderIndices.TryGetValue(hash, out rendererIndex);
            if (result)
            {
                ref var renderer = ref _renderers[rendererIndex];
                skin = renderer.skin;
                
                return true;
            }
            
            Rebuild();
            result = __renderIndices.TryGetValue(hash, out rendererIndex);
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

        private Dictionary<Mesh, Mesh> __meshCache;

        public Mesh GetOrCreateMesh(SkinnedMeshRenderer skinnedMeshRenderer)
        {
            Mesh sharedMesh = skinnedMeshRenderer.sharedMesh;
            if (__meshCache != null && __meshCache.TryGetValue(sharedMesh, out var cached) && cached != null)
                return cached;

            using (var meshWrapper = new MeshWrapper(skinnedMeshRenderer.sharedMesh))
            {
                sharedMesh = meshWrapper;

                // Search for an existing baked mesh sub-asset
                var assetPath = AssetDatabase.GetAssetPath(this);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (asset is Mesh mesh && asset != this && asset.name == sharedMesh.name)
                    {
                        if (__meshCache == null)
                            __meshCache = new Dictionary<Mesh, Mesh>();

                        __meshCache[sharedMesh] = mesh;

                        return mesh;
                    }
                }

                // Create a new baked mesh using AcquireReadOnlyMeshData (works on read-only meshes)
                var newMesh = new Mesh();
                newMesh.name = sharedMesh.name;
                newMesh.bounds = sharedMesh.bounds;

                using (var readOnlyData = Mesh.AcquireReadOnlyMeshData(sharedMesh))
                {
                    var source = readOnlyData[0];
                    int vertexCount = source.vertexCount;
                    var descriptors = new List<VertexAttributeDescriptor>(sharedMesh.GetVertexAttributes());
                    int numDescriptors = descriptors.Count;
                    for (int i = 0; i < numDescriptors; ++i)
                    {
                        switch (descriptors[i].attribute)
                        {
                            case VertexAttribute.BlendWeight:
                            case VertexAttribute.BlendIndices:
                                descriptors.RemoveAt(i--);

                                --numDescriptors;
                                break;
                        }
                    }

                    var writableData = Mesh.AllocateWritableMeshData(1);
                    var dest = writableData[0];

                    dest.SetVertexBufferParams(vertexCount, descriptors.ToArray());

                    int maxStream = 0;
                    foreach (var d in descriptors)
                    {
                        if (d.stream > maxStream)
                            maxStream = d.stream;
                    }

                    for (int stream = 0; stream <= maxStream; stream++)
                    {
                        var srcData = source.GetVertexData<byte>(stream);
                        var dstData = dest.GetVertexData<byte>(stream);
                        if (srcData.Length > 0)
                            NativeArray<byte>.Copy(srcData, dstData, srcData.Length);
                    }

                    int indexCount = 0;
                    for (int i = 0; i < source.subMeshCount; i++)
                    {
                        var subMesh = source.GetSubMesh(i);
                        int last = subMesh.indexStart + subMesh.indexCount;
                        if (last > indexCount)
                            indexCount = last;
                    }

                    dest.SetIndexBufferParams(indexCount, source.indexFormat);

                    var srcIdx = source.GetIndexData<byte>();
                    var dstIdx = dest.GetIndexData<byte>();
                    if (srcIdx.Length > 0)
                        NativeArray<byte>.Copy(srcIdx, dstIdx, srcIdx.Length);

                    dest.subMeshCount = source.subMeshCount;
                    for (int i = 0; i < source.subMeshCount; i++)
                        dest.SetSubMesh(i, source.GetSubMesh(i));

                    Mesh.ApplyAndDisposeWritableMeshData(writableData, newMesh);
                }

                AssetDatabase.AddObjectToAsset(newMesh, this);

                if (__meshCache == null)
                    __meshCache = new Dictionary<Mesh, Mesh>();

                __meshCache[sharedMesh] = newMesh;

                return newMesh;
            }
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
                    destination.boneDataPixelIndex = source.skin.boneDataPixelIndex;
                }
                
                return builder.CreateBlobAssetReference<InstanceAnimationDefinition>(allocator);
            }
        }
    }

    [CustomEditor(typeof(SkinnedMeshRendererDatabase))]
    public class SkinnedMeshRendererDatabaseDrawer : Editor
    {
        [MenuItem("Assets/ZG/Rebuild All SkinnedMeshRendererDatabases")]
        public static void RebuildAllDatabases()
        {
            var guids = AssetDatabase.FindAssets("t:SkinnedMeshRendererDatabase");
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                
                var database =
                    AssetDatabase.LoadAssetAtPath<SkinnedMeshRendererDatabase>(path);
                
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == database)
                        continue;
                    
                    DestroyImmediate(asset, true);
                }
                
                database.Rebuild();
                
                EditorUtility.SetDirty(database);
            }
        }
        
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