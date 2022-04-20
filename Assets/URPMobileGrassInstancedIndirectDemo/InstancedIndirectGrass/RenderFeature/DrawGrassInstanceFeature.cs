using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DrawGrassInstanceFeature : ScriptableRendererFeature
{
    DrawGrassInstancePass drawInstancePass;

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(drawInstancePass);
    }

    public override void Create()
    {
        drawInstancePass = new DrawGrassInstancePass();
        drawInstancePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    private class DrawGrassInstancePass : ScriptableRenderPass
    {
        private InstancedIndirectGrassRenderer grassRenderer;

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (grassRenderer == null)
            {
                grassRenderer = InstancedIndirectGrassRenderer.instance;
            }
            else
            {
                //=====================================================================================================
                // then loop though only visible cells, each visible cell dispatch GPU culling job once
                // at the end compute shader will fill all visible instance into visibleInstancesOnlyPosWSIDBuffer
                //=====================================================================================================
                CommandBuffer commandBuffer = CommandBufferPool.Get("DrawGrassInstance");

                Matrix4x4 v = grassRenderer.cam.worldToCameraMatrix;
                Matrix4x4 p = grassRenderer.cam.projectionMatrix;
                Matrix4x4 vp = p * v;

                //set once only
                commandBuffer.SetComputeMatrixParam(grassRenderer.cullingComputeShader, "_VPMatrix", vp);
                commandBuffer.SetComputeFloatParam(grassRenderer.cullingComputeShader,"_MaxDrawDistance", grassRenderer.drawDistance);

                //for (int i = 0; i < grassRenderer.visibleCellsID.Length; i++)
                //{
                //    int targetCellFlattenID = grassRenderer.visibleCellsID[i];
                //    int memoryOffset = grassRenderer.cellGrassCount * targetCellFlattenID;
                //    commandBuffer.SetComputeIntParam(grassRenderer.cullingComputeShader, "_StartOffset", memoryOffset); //culling read data started at offseted pos, will start from cell's total offset in memory
                //    int jobLength = grassRenderer.cellGrassCount;

                //    //============================================================================================
                //    //batch n dispatchs into 1 dispatch, if memory is continuous in allInstancesPosWSBuffer
                //    while ((i < grassRenderer.visibleCellsID.Length - 1) && //test this first to avoid out of bound access to visibleCellIDList
                //                (grassRenderer.visibleCellsID[i + 1] == grassRenderer.visibleCellsID[i] + 1))
                //    {
                //        //if memory is continuous, append them together into the same dispatch call
                //        jobLength += grassRenderer.cellGrassCount;
                //        i++;
                //    }
                //    //============================================================================================

                //    commandBuffer.DispatchCompute(grassRenderer.cullingComputeShader, 0, Mathf.CeilToInt(jobLength / 64f), 1, 1);//disaptch.X division number must match numthreads.x in compute shader (e.g. 64)
                //}
                int jobLength = grassRenderer.cellGrassCount * grassRenderer.visibleCellsID.Length;
                if (jobLength > 0)
                    commandBuffer.DispatchCompute(grassRenderer.cullingComputeShader, 0, Mathf.CeilToInt(jobLength / 64f), 1, 1);

                //====================================================================================
                // Final 1 big DrawMeshInstancedIndirect draw call 
                //====================================================================================
                // GPU per instance culling finished, copy visible count to argsBuffer, to setup DrawMeshInstancedIndirect's draw amount 
                commandBuffer.CopyCounterValue(grassRenderer.visibleInstancesOnlyPosWSIDBuffer, grassRenderer.argsBuffer, 4);
                // Render 1 big drawcall using DrawMeshInstancedIndirect
                commandBuffer.DrawMeshInstancedIndirect(grassRenderer.GetGrassMeshCache(), 0, grassRenderer.instanceMaterial, 0, grassRenderer.argsBuffer);
                context.ExecuteCommandBuffer(commandBuffer);
                CommandBufferPool.Release(commandBuffer);
            }
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (grassRenderer != null && grassRenderer.visibleInstancesOnlyPosWSIDBuffer != null)
            {
                grassRenderer.visibleInstancesOnlyPosWSIDBuffer.SetCounterValue(0);
            }
        }
    }
}
