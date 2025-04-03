using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph.Internal;

namespace UnityEditor.ShaderGraph
{
    [Title("Input", "2D", "Sprite Instance Node")]
    class SpriteInstanceNode : AbstractMaterialNode, 
        IGeneratesBodyCode, 
        IGeneratesFunction, 
        IMayRequireMeshUV, 
        IMayRequirePosition
    {
        public const int kPositionSlotID = 0;
        public const int kPositionOutputSlotID = 1;
        public const int kUVSlotID = 2;
        public const int kUVOutputSlotID = 3;
        public const int kColorOutputSlotID = 4;
        public const int kTextureIndexOutputSlotID = 5;

        public const string kSlotPositionName = "Vertex Position";
        public const string kOutputSlotPositionName = "Output Position";
        public const string kSlotUVName = "UV";
        public const string kOutputSlotUVName = "Output UV";
        public const string kOutputSlotColorName = "Output Color";
        public const string kOutputSlotTextureIndexName = "Output Texture Index";

        public SpriteInstanceNode()
        {
            name = "Sprite Instance Node";
            UpdateNodeAfterDeserialization();
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new UVMaterialSlot(
                kUVSlotID, 
                kSlotUVName, 
                kSlotUVName, 
                UVChannel.UV0, 
                ShaderStageCapability.Fragment));
            
            AddSlot(new PositionMaterialSlot(kPositionSlotID, kSlotPositionName, kSlotPositionName,
                CoordinateSpace.Object, ShaderStageCapability.Vertex));
            
            AddSlot(new Vector3MaterialSlot(kPositionOutputSlotID, kOutputSlotPositionName, kOutputSlotPositionName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            
            AddSlot(new Vector2MaterialSlot(kUVOutputSlotID, kOutputSlotUVName, kOutputSlotUVName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Fragment));
            
            AddSlot(new ColorRGBAMaterialSlot(kColorOutputSlotID, kOutputSlotColorName, kOutputSlotColorName,
                SlotType.Output, Vector4.zero, ShaderStageCapability.Fragment));

            AddSlot(new Vector1MaterialSlot(kTextureIndexOutputSlotID, kOutputSlotTextureIndexName, kOutputSlotTextureIndexName,
                SlotType.Output, 0.0f, ShaderStageCapability.Fragment));
            
            RemoveSlotsNameNotMatching(new[]
            {
                kUVSlotID, 
                
                kPositionSlotID, 
                kPositionOutputSlotID, 
                
                kUVOutputSlotID, 
                kColorOutputSlotID, 
                kTextureIndexOutputSlotID
            });
        }

        public bool RequiresMeshUV(UVChannel channel, ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            return UVChannel.UV0 == channel && (ShaderStageCapability.Fragment & stageCapability) == ShaderStageCapability.Fragment;
        }

        public NeededCoordinateSpace RequiresPosition(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        /*public override void CollectShaderProperties(PropertyCollector properties, GenerationMode generationMode)
        {
            properties.AddShaderProperty(new Vector1ShaderProperty()
            {
                displayName = "PositionST",
                overrideReferenceName = "_PositionST",
                //overrideHLSLDeclaration = true,
                hlslDeclarationOverride = HLSLDeclaration.UnityPerMaterial,
                hidden = true,
                value = 0
            });
            
            properties.AddShaderProperty(new Vector1ShaderProperty()
            {
                displayName = "UVST",
                overrideReferenceName = "_UVST",
                //overrideHLSLDeclaration = true,
                hlslDeclarationOverride = HLSLDeclaration.UnityPerMaterial,
                hidden = true,
                value = 0
            });
            
            properties.AddShaderProperty(new Vector1ShaderProperty()
            {
                displayName = "Texture Index",
                overrideReferenceName = "_TextureIndex",
                //overrideHLSLDeclaration = true,
                hlslDeclarationOverride = HLSLDeclaration.UnityPerMaterial,
                hidden = false,
                value = 0
            });
            
            base.CollectShaderProperties(properties, generationMode);
        }*/

        public void GenerateNodeCode(ShaderStringBuilder sb, GenerationMode generationMode)
        {
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kPositionOutputSlotID));
            sb.AppendLine("$precision2 {0} = 0;", GetVariableNameForSlot(kUVOutputSlotID));
            sb.AppendLine("$precision4 {0} = 0;", GetVariableNameForSlot(kColorOutputSlotID));
            sb.AppendLine("$precision {0} = 0;", GetVariableNameForSlot(kTextureIndexOutputSlotID));
            if (generationMode == GenerationMode.ForReals)
            {
                sb.AppendLine($"{GetVertexFunctionName()}(" +
                              $"{GetSlotValue(kPositionSlotID, generationMode)}, " +
                              $"{GetVariableNameForSlot(kPositionOutputSlotID)});");
                
                sb.AppendLine($"{GetFragmentFunctionName()}(" +
                              $"{GetSlotValue(kUVSlotID, generationMode)}, " +
                              $"{GetVariableNameForSlot(kUVOutputSlotID)}, " + 
                              $"{GetVariableNameForSlot(kColorOutputSlotID)}, " + 
                              $"{GetVariableNameForSlot(kTextureIndexOutputSlotID)});");
            }
        }

        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            registry.ProvideFunction("SpriteInstance", sb =>
            {
                sb.AppendLine("UNITY_INSTANCING_BUFFER_START(SpriteInstance)");
                using (sb.IndentScope())
                {
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float4, _PositionST)");
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float4, _UVST)");
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float4, _Color)");
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float, _TextureIndex)");
                }
                sb.AppendLine("UNITY_INSTANCING_BUFFER_END(SpriteInstance)");
            });
            
            registry.ProvideFunction(GetVertexFunctionName(), sb =>
            {
                sb.AppendLine($"void {GetVertexFunctionName()}(" +
                              "$precision3 positionIn, " +
                              "out $precision3 positionOut)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine(
                        "float4 positionST = UNITY_ACCESS_INSTANCED_PROP(SpriteInstance, _PositionST);");
                    sb.AppendLine(
                        "positionOut = $precision3 (positionIn.xy * positionST.xy + positionST.zw, positionIn.z);");
                }

                sb.AppendLine("}");
            });
            
            registry.ProvideFunction(GetFragmentFunctionName(), sb =>
            {
                sb.AppendLine($"void {GetFragmentFunctionName()}(" +
                              "$precision2 uvIn, " +
                              "out $precision2 uvOut, " +
                              "out $precision4 colorOut, " +
                              "out $precision textureIndexOut)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine(
                        "$precision4 uvST = UNITY_ACCESS_INSTANCED_PROP(SpriteInstance, _UVST);");
                    sb.AppendLine(
                        "uvOut = uvIn * uvST.xy + uvST.zw;");
                    sb.AppendLine(
                        "colorOut = UNITY_ACCESS_INSTANCED_PROP(SpriteInstance, _Color);");
                    sb.AppendLine(
                        "textureIndexOut = UNITY_ACCESS_INSTANCED_PROP(SpriteInstance, _TextureIndex);");
                }

                sb.AppendLine("}");
            });
        }

        string GetVertexFunctionName()
        {
            return "SpriteInstanceVertex_$precision";
        }
        
        string GetFragmentFunctionName()
        {
            return "SpriteInstanceFragment_$precision";
        }
    }
}