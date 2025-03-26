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
        public const int kUVSlotID = 0;
        public const int kPositionSlotID = 1;
        public const int kPositionOutputSlotID = 2;
        public const int kUVOutputSlotID = 3;
        //public const int kTextureIndexOutputSlotID = 4;

        public const string kSlotUVName = "UV";
        public const string kSlotPositionName = "Vertex Position";
        public const string kOutputSlotPositionName = "Output Position";
        public const string kOutputSlotUVName = "Output UV";
        //public const string kOutputSlotTextureIndexName = "Output Texture Index";

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
                ShaderStageCapability.Vertex));
            
            AddSlot(new PositionMaterialSlot(kPositionSlotID, kSlotPositionName, kSlotPositionName,
                CoordinateSpace.Object, ShaderStageCapability.Vertex));
            
            AddSlot(new Vector3MaterialSlot(kPositionOutputSlotID, kOutputSlotPositionName, kOutputSlotPositionName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            
            AddSlot(new Vector3MaterialSlot(kUVOutputSlotID, kOutputSlotUVName, kOutputSlotUVName,
                SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            
            RemoveSlotsNameNotMatching(new[]
            {
                kUVSlotID, 
                
                kPositionSlotID, 
                kPositionOutputSlotID, 
                kUVOutputSlotID
            });
        }

        public bool RequiresMeshUV(UVChannel channel, ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            return UVChannel.UV0 == channel;
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
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kUVOutputSlotID));
            if (generationMode == GenerationMode.ForReals)
            {
                sb.AppendLine($"{GetFunctionName()}(" +
                              $"{GetSlotValue(kPositionSlotID, generationMode)}, " +
                              $"{GetSlotValue(kUVSlotID, generationMode)}, " +
                              $"{GetVariableNameForSlot(kPositionOutputSlotID)}, " + 
                              $"{GetVariableNameForSlot(kUVOutputSlotID)});");
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
                    sb.AppendLine("UNITY_DEFINE_INSTANCED_PROP(float, _TextureIndex)");
                }
                sb.AppendLine("UNITY_INSTANCING_BUFFER_END(SpriteInstance)");
            });
            
            registry.ProvideFunction(GetFunctionName(), sb =>
            {
                sb.AppendLine($"void {GetFunctionName()}(" +
                              "$precision3 positionIn, " +
                              "$precision2 uvIn, " +
                              "out $precision3 positionOut, " +
                              "out $precision3 uvOut)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine(
                        "float4 positionST = UNITY_ACCESS_INSTANCED_PROP(SpriteInstance, _PositionST);");
                    sb.AppendLine(
                        "positionOut = $precision3 (positionIn.xy * positionST.xy + positionST.zw, positionIn.z);");
                    sb.AppendLine(
                        "float4 uvST = UNITY_ACCESS_INSTANCED_PROP(SpriteInstance, _UVST);");
                    sb.AppendLine(
                        "uvOut = $precision3 (uvIn * uvST.xy + uvST.zw, UNITY_ACCESS_INSTANCED_PROP(SpriteInstance, _TextureIndex));");
                }

                sb.AppendLine("}");
            });
        }

        string GetFunctionName()
        {
            return "SpriteInstance_$precision";
        }
    }
}