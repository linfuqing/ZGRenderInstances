using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ZG
{
    public struct InstanceAnimationDefinition
    {
        public struct Clip
        {
            public enum WrapMode
            {
                Normal,
                Loop
            }

            public FixedString128Bytes name;

            public WrapMode wrapMode;

            public int startFrame;

            public int frameCount;

            public int GetOffsetFrame(int frameCount, int currentFrame, out bool isPlaying)
            {
                switch (wrapMode)
                {
                    case WrapMode.Normal:
                        if (currentFrame >= frameCount)
                        {
                            isPlaying = false;
                            currentFrame = frameCount - 1;
                        }
                        else
                            isPlaying = true;

                        break;
                    case WrapMode.Loop:
                        isPlaying = true;
                    
                        currentFrame %= frameCount;

                        break;
                    default:
                        isPlaying = false;
                        break;
                }

                return currentFrame;
            }
        
            public int Evaluate(float time, int frameCountPerSecond, out bool isPlaying)
            {
                int offsetFrame = (int)(frameCountPerSecond * time);
                return startFrame + GetOffsetFrame(frameCount, offsetFrame, out isPlaying);
            
                //return currentFrame * root.pixelCountPerFrame + root.pixelOffset;
            }
        }

        public struct Renderer
        {
            public int depthIndex;
            public int pixelOffset;
            public int pixelCountPerFrame;

            public int clipStartIndex;
            public int clipCount;

            public bool IsInClip(int index)
            {
                return index >= clipStartIndex && index < clipStartIndex + clipCount;
            }
            
            public RenderSkinnedData Evaluate(int frame)
            {
                RenderSkinnedData skinnedData;
                skinnedData.depth = depthIndex;
                skinnedData.pixelOffset = (uint)(frame * pixelCountPerFrame + pixelOffset);

                return skinnedData;
            }
        }

        public int frameCountPerSecond;

        public BlobArray<Clip> clips;
        public BlobArray<Renderer> renderers;

        public int IndexOfClip(
            in FixedString128Bytes clipName, 
            int clipStartIndex, 
            int clipCount)
        {
            //ref var renderer = ref renderers[rendererIndex];
            
            int numClips = math.min(clipStartIndex + clipCount, clips.Length);
            for (int i = clipStartIndex; i < numClips; ++i)
            {
                ref var clip = ref clips[i];
                if (clip.name == clipName)
                    return i;
            }

            return -1;
        }
    }
    
    public struct InstanceAnimationDefinitionData : IComponentData
    {
        public int clipStartIndex;
        public int clipCount;
        public BlobAssetReference<InstanceAnimationDefinition> definition;

        public int IndexOfClip(in FixedString128Bytes clipName)
        {
            return definition.Value.IndexOfClip(clipName, clipStartIndex, clipCount);
        }
    }
    
    public struct InstanceAnimationStatus : IComponentData, IEnableableComponent
    {
        public int clipIndex;
        public float time;

        public bool Evaluate(
            float deltaTime,
            in NativeArray<InstanceSkinnedMeshRenderer> renderers, 
            ref InstanceAnimationDefinition definition,
            ref ComponentLookup<RenderSkinnedData> skinnedDatas)
        {
            if (clipIndex >= definition.clips.Length)
                return false;

            time += deltaTime;
            
            ref var clip = ref definition.clips[clipIndex];
            int frame = clip.Evaluate(time, definition.frameCountPerSecond, out bool isPlaying), 
                numRenderers = definition.renderers.Length;
            foreach (var renderer in renderers)
            {
                if(renderer.index < 0 || renderer.index >= numRenderers)
                    continue;
                
                if (!skinnedDatas.HasComponent(renderer.entity))
                    continue;

                ref var rendererDefinition = ref definition.renderers[renderer.index];
                if(!rendererDefinition.IsInClip(clipIndex))
                    continue;
                
                skinnedDatas[renderer.entity] = rendererDefinition.Evaluate(frame);
            }

            return isPlaying;
        }
    }

    public struct InstanceSkinnedMeshRenderer : IBufferElementData
    {
        public int index;
        
        public Entity entity;
    }
}