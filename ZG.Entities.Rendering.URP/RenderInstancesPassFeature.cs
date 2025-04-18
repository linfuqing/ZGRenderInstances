using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZG
{
    public class RenderInstancesPassFeature : ScriptableRendererFeature
    {
        class RenderPass : ScriptableRenderPass
        {
            private readonly ProfilingSampler __profilingSampler = new ProfilingSampler("RenderInstancesPassFeature");

            // This method is called before executing the render pass.
            // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
            // When empty this render pass will render to the active camera render target.
            // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
            // The render pipeline will ensure target setup and clearing happens in a performant manner.
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
            }

            // Here you can implement the rendering logic.
            // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
            // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
            // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                /*var commandBuffers = RenderCommandBufferPool.commandBuffers;
                if (commandBuffers != null)
                {
                    foreach (var commandBuffer in commandBuffers)
                        context.ExecuteCommandBuffer(commandBuffer);
                }*/
                var cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, __profilingSampler))
                    RenderInstanceSystem.Apply(renderingData.cameraData.camera, cmd);
                    
                context.ExecuteCommandBuffer(cmd);
                
                cmd.Clear();
                
                CommandBufferPool.Release(cmd);
            }

            // Cleanup any allocated resources that were created during the execution of this render pass.
            public override void OnCameraCleanup(CommandBuffer cmd)
            {
            }
        }

        private RenderPass __renderPass;

        /// <inheritdoc/>
        public override void Create()
        {
            __renderPass = new RenderPass();

            // Configures where the render pass should be injected.
            __renderPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(__renderPass);
        }
    }
}

