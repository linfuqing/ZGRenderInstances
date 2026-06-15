using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph.Internal;

namespace UnityEditor.ShaderGraph
{
    [Title("Input", "Mesh Deformation", "Skinned Instance Node")]
    class SkinnedInstanceNode : AbstractMaterialNode, 
        IGeneratesBodyCode, 
        IGeneratesFunction, 
        IMayRequireVertexID,
        IMayRequirePosition, 
        IMayRequireNormal, 
        IMayRequireTangent
    {
        public const int kTexSlotId = 0;
        public const int kTexelSizeSlotId = 1;
        public const int kPositionSlotId = 2;
        public const int kNormalSlotId = 3;
        public const int kTangentSlotId = 4;
        public const int kPositionOutputSlotId = 5;
        public const int kNormalOutputSlotId = 6;
        public const int kTangentOutputSlotId = 7;

        public const string kSlotTexName = "Animation Map";
        public const string kSlotTexelSizeName = "Animation Map Texel Size";
        
        public const string kSlotPositionName = "Vertex Position";
        public const string kSlotNormalName = "Vertex Normal";
        public const string kSlotTangentName = "Vertex Tangent";
        public const string kOutputSlotPositionName = "Skinned Position";
        public const string kOutputSlotNormalName = "Skinned Normal";
        public const string kOutputSlotTangentName = "Skinned Tangent";

        public SkinnedInstanceNode()
        {
            name = "Skinned Instance Node";
            UpdateNodeAfterDeserialization();
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new Texture2DArrayInputMaterialSlot(kTexSlotId, kSlotTexName, kSlotTexName, ShaderStageCapability.Vertex));
            
            AddSlot(new Vector4MaterialSlot(kTexelSizeSlotId, kSlotTexelSizeName, kSlotTexelSizeName, SlotType.Input, Vector4.zero, ShaderStageCapability.Vertex));

            AddSlot(new PositionMaterialSlot(kPositionSlotId, kSlotPositionName, kSlotPositionName,
                CoordinateSpace.Object, ShaderStageCapability.Vertex));
            AddSlot(new NormalMaterialSlot(kNormalSlotId, kSlotNormalName, kSlotNormalName, CoordinateSpace.Object,
                ShaderStageCapability.Vertex));
            AddSlot(new TangentMaterialSlot(kTangentSlotId, kSlotTangentName, kSlotTangentName, CoordinateSpace.Object,
                ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kPositionOutputSlotId, kOutputSlotPositionName, kOutputSlotPositionName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kNormalOutputSlotId, kOutputSlotNormalName, kOutputSlotNormalName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kTangentOutputSlotId, kOutputSlotTangentName, kOutputSlotTangentName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            RemoveSlotsNameNotMatching(new[]
            {
                kTexSlotId, 
                kTexelSizeSlotId, 
                
                kPositionSlotId, 
                kNormalSlotId, 
                kTangentSlotId, 
                kPositionOutputSlotId, 
                kNormalOutputSlotId,
                kTangentOutputSlotId
            });
        }

        public bool RequiresVertexID(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            return true;
        }

        public NeededCoordinateSpace RequiresPosition(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        public NeededCoordinateSpace RequiresNormal(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        public NeededCoordinateSpace RequiresTangent(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        public override void CollectShaderProperties(PropertyCollector properties, GenerationMode generationMode)
        {
            base.CollectShaderProperties(properties, generationMode);
        }

        public void GenerateNodeCode(ShaderStringBuilder sb, GenerationMode generationMode)
        {
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kPositionOutputSlotId));
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kNormalOutputSlotId));
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kTangentOutputSlotId));
            if (generationMode == GenerationMode.ForReals)
            {
                sb.AppendLine($"{GetFunctionName()}(" +
                              $"{GetSlotValue(kTexSlotId, generationMode)}, " +
                              $"{GetSlotValue(kTexelSizeSlotId, generationMode)}, " +
                              $"IN.VertexID, " +
                              $"{GetSlotValue(kPositionSlotId, generationMode)}, " +
                              $"{GetSlotValue(kNormalSlotId, generationMode)}, " +
                              $"{GetSlotValue(kTangentSlotId, generationMode)}, " +
                              $"{GetVariableNameForSlot(kPositionOutputSlotId)}, " +
                              $"{GetVariableNameForSlot(kNormalOutputSlotId)}, " +
                              $"{GetVariableNameForSlot(kTangentOutputSlotId)});");
            }
        }

        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            registry.ProvideFunction("SkinnedInstance", sb =>
            {
                sb.AppendLine("UNITY_INSTANCING_BUFFER_START(SkinnedInstance)");
                using (sb.IndentScope())
                {
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float, _PixelOffset)");
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float, _Depth)");
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float, _BoneDataPixelOffset)");
                }
                sb.AppendLine("UNITY_INSTANCING_BUFFER_END(SkinnedInstance)");
            });
            
            registry.ProvideFunction("SkinnedInstanceGetUV", sb =>
            {
                sb.AppendLine($"float2 SkinnedInstanceGetUV(uint index, float4 texelSize)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("uint z = (uint)texelSize.z;");
                    sb.AppendLine("uint row = index / z;");
                    sb.AppendLine("uint col = index % z;");
                    sb.AppendLine("return float2(col * texelSize.x, row * texelSize.y);");
                }

                sb.AppendLine("}");
            });
            
            registry.ProvideFunction("SkinnedInstanceTex", sb =>
            {
                sb.AppendLine($"float4 SkinnedInstanceTex(UnityTexture2DArray map, float4 texelSize, uint index, float depth)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("float2 uv = SkinnedInstanceGetUV(index, texelSize);");
                    sb.AppendLine("return SAMPLE_TEXTURE2D_ARRAY_LOD(map.tex, map.samplerstate, uv, depth, 0);");
                }

                sb.AppendLine("}");
            });
            
            registry.ProvideFunction("SkinnedInstanceDecodeHalf2", sb =>
            {
                sb.AppendLine("float2 SkinnedInstanceDecodeHalf2(float4 rgba)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("uint h0 = (uint)(rgba.r * 255.0 + 0.5) | ((uint)(rgba.g * 255.0 + 0.5) << 8);");
                    sb.AppendLine("uint h1 = (uint)(rgba.b * 255.0 + 0.5) | ((uint)(rgba.a * 255.0 + 0.5) << 8);");
                    sb.AppendLine("return float2(f16tof32(h0), f16tof32(h1));");
                }
                sb.AppendLine("}");
            });

            registry.ProvideFunction("SkinnedInstanceReadFloat4", sb =>
            {
                sb.AppendLine("float4 SkinnedInstanceReadFloat4(UnityTexture2DArray map, float4 texelSize, uint texelIndex, float depth)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("float2 a = SkinnedInstanceDecodeHalf2(SkinnedInstanceTex(map, texelSize, texelIndex, depth));");
                    sb.AppendLine("float2 b = SkinnedInstanceDecodeHalf2(SkinnedInstanceTex(map, texelSize, texelIndex + 1, depth));");
                    sb.AppendLine("return float4(a, b);");
                }
                sb.AppendLine("}");
            });

            registry.ProvideFunction("SkinnedInstanceGetMatrix", sb =>
            {
                sb.AppendLine($"float3x4 SkinnedInstanceGetMatrix(uint startIndex, uint boneIndex, float depth, float4 texelSize, UnityTexture2DArray map)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("uint matrixIndex = startIndex + boneIndex * 6;");
                    sb.AppendLine("float4 row0 = SkinnedInstanceReadFloat4(map, texelSize, matrixIndex + 0, depth);");
                    sb.AppendLine("float4 row1 = SkinnedInstanceReadFloat4(map, texelSize, matrixIndex + 2, depth);");
                    sb.AppendLine("float4 row2 = SkinnedInstanceReadFloat4(map, texelSize, matrixIndex + 4, depth);");
                    
                    sb.AppendLine("return float3x4(row0, row1, row2);");
                }

                sb.AppendLine("}");
            });

            registry.ProvideFunction(GetFunctionName(), sb =>
            {
                sb.AppendLine($"void {GetFunctionName()}(" +
                              "UnityTexture2DArray map, " +
                              "$precision4 texelSize, " +
                              "uint vertexID, " +
                              "$precision3 positionIn, " +
                              "$precision3 normalIn, " +
                              "$precision3 tangentIn, " +
                              "out $precision3 positionOut, " +
                              "out $precision3 normalOut, " +
                              "out $precision3 tangentOut)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("positionOut = 0;");
                    sb.AppendLine("normalOut = 0;");
                    sb.AppendLine("tangentOut = 0;");

                    sb.AppendLine(
                        "uint pixelOffset = asuint(UNITY_ACCESS_INSTANCED_PROP(SkinnedInstance, _PixelOffset));");
                    sb.AppendLine(
                        "float depth = UNITY_ACCESS_INSTANCED_PROP(SkinnedInstance, _Depth);");
                    sb.AppendLine(
                        "uint boneDataPixelOffset = asuint(UNITY_ACCESS_INSTANCED_PROP(SkinnedInstance, _BoneDataPixelOffset));");

                    // Read bone indices and weights from _AnimationMap via vertexID
                    sb.AppendLine("uint bonePixelIndex = boneDataPixelOffset + vertexID * 4;");
                    sb.AppendLine("float4 idxF = SkinnedInstanceReadFloat4(map, texelSize, bonePixelIndex, depth);");
                    sb.AppendLine("uint4 indices = (uint4)(idxF + 0.5);");
                    sb.AppendLine("float4 weights = SkinnedInstanceReadFloat4(map, texelSize, bonePixelIndex + 2, depth);");

                    sb.AppendLine(
                        "float totalWeight = 0.0f, weight;");

                    sb.AppendLine("for (int i = 0; i < 3; ++i)");
                    sb.AppendLine("{");
                    using (sb.IndentScope())
                    {
                        sb.AppendLine("float3x4 skinMatrix = SkinnedInstanceGetMatrix(pixelOffset, indices[i], depth, texelSize, map);");
                        sb.AppendLine("float3 vtransformed = mul(skinMatrix, float4(positionIn, 1));");
                        sb.AppendLine("half3 ntransformed = mul(skinMatrix, half4(normalIn, 0));");
                        sb.AppendLine("half3 ttransformed = mul(skinMatrix, half4(tangentIn, 0));");
                        sb.AppendLine("");
                        sb.AppendLine("weight = weights[i];");
                        sb.AppendLine("positionOut += vtransformed * weight;");
                        sb.AppendLine("normalOut   += ntransformed * weight;");
                        sb.AppendLine("tangentOut  += ttransformed * weight;");
                        sb.AppendLine("totalWeight += weight;");
                    }
                    sb.AppendLine("}");
                    
                    sb.AppendLine("float3x4 skinMatrix = SkinnedInstanceGetMatrix(pixelOffset, indices.w, depth, texelSize, map);");
                    sb.AppendLine("float3 vtransformed = mul(skinMatrix, float4(positionIn, 1));");
                    sb.AppendLine("half3 ntransformed = mul(skinMatrix, half4(normalIn, 0));");
                    sb.AppendLine("half3 ttransformed = mul(skinMatrix, half4(tangentIn, 0));");
                    sb.AppendLine("");
                    sb.AppendLine("weight = 1.0 - totalWeight;");
                    sb.AppendLine("positionOut += vtransformed * weight;");
                    sb.AppendLine("normalOut   += ntransformed * weight;");
                    sb.AppendLine("tangentOut  += ttransformed * weight;");
                }

                sb.AppendLine("}");
            });
        }

        string GetFunctionName()
        {
            return "SkinnedInstance_$precision";
        }
    }
}
