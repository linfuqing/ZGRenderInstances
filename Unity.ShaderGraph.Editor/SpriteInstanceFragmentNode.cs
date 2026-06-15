using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph.Internal;

namespace UnityEditor.ShaderGraph
{
    [Title("Input", "2D", "Sprite Instance Fragment Node")]
    class SpriteInstanceFragmentNode : AbstractMaterialNode, 
        IGeneratesBodyCode, 
        IGeneratesFunction, 
        IMayRequireMeshUV
    {
        public const int kUVSlotID = 0;
        public const int kUVOutputSlotID = 1;
        public const int kColorOutputSlotID = 2;
        public const int kTextureIndexOutputSlotID = 3;

        public const string kSlotUVName = "UV";
        public const string kOutputSlotUVName = "Output UV";
        public const string kOutputSlotColorName = "Output Color";
        public const string kOutputSlotTextureIndexName = "Output Texture Index";

        public SpriteInstanceFragmentNode()
        {
            name = "Sprite Instance Fragment Node";
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
            
            AddSlot(new Vector2MaterialSlot(kUVOutputSlotID, kOutputSlotUVName, kOutputSlotUVName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Fragment));
            
            AddSlot(new ColorRGBAMaterialSlot(kColorOutputSlotID, kOutputSlotColorName, kOutputSlotColorName,
                SlotType.Output, Vector4.zero, ShaderStageCapability.Fragment));

            AddSlot(new Vector1MaterialSlot(kTextureIndexOutputSlotID, kOutputSlotTextureIndexName, kOutputSlotTextureIndexName,
                SlotType.Output, 0.0f, ShaderStageCapability.Fragment));
            
            RemoveSlotsNameNotMatching(new[]
            {
                kUVSlotID, 
                
                kUVOutputSlotID, 
                kColorOutputSlotID, 
                kTextureIndexOutputSlotID
            });
        }

        public bool RequiresMeshUV(UVChannel channel, ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            return UVChannel.UV0 == channel && (ShaderStageCapability.Fragment & stageCapability) == ShaderStageCapability.Fragment;
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
            sb.AppendLine("$precision2 {0} = 0;", GetVariableNameForSlot(kUVOutputSlotID));
            sb.AppendLine("$precision4 {0} = 0;", GetVariableNameForSlot(kColorOutputSlotID));
            sb.AppendLine("$precision {0} = 0;", GetVariableNameForSlot(kTextureIndexOutputSlotID));
            if (generationMode == GenerationMode.ForReals)
            {
                sb.AppendLine($"{GetFunctionName()}(" +
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
            
            registry.ProvideFunction(GetFunctionName(), sb =>
            {
                sb.AppendLine($"void {GetFunctionName()}(" +
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

        string GetFunctionName()
        {
            return "SpriteInstanceFragment_$precision";
        }
    }
}