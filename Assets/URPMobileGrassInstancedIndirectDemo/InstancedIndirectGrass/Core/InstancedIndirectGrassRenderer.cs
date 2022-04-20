//see this for ref: https://docs.unity3d.com/ScriptReference/Graphics.DrawMeshInstancedIndirect.html

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

[ExecuteAlways]
public class InstancedIndirectGrassRenderer : MonoBehaviour
{
    [Header("Settings")]
    public float drawDistance = 125;//this setting will affect performance a lot!
    public Material instanceMaterial;

    [Header("Internal")]
    public ComputeShader cullingComputeShader;

    public bool useJobCull;
    
    public NativeArray<float3> allGrassPos;
    //=====================================================
    [HideInInspector]   
    public static InstancedIndirectGrassRenderer instance;// global ref to this script

    [HideInInspector] public int cellCountX = -1;
    [HideInInspector] public int cellCountZ = -1;
    [HideInInspector] public int cellGrassCount = 0;

    //smaller the number, CPU needs more time, but GPU is faster
    [HideInInspector] public float cellSize = 10;

    private int instanceCountCache = -1;
    private Mesh cachedGrassMesh;

    private ComputeBuffer allInstancesPosWSBuffer;
    public ComputeBuffer visibleInstancesOnlyPosWSIDBuffer;
    public ComputeBuffer argsBuffer;

    private Plane[] cameraFrustumPlanes = new Plane[6];

    [HideInInspector] public int[] visibleCellsID;
    [HideInInspector] public Camera cam;

    public ComputeBuffer visibleCellsBuffer;

    bool shouldBatchDispatch = true;
    //=====================================================

    private void OnEnable()
    {
        instance = this; // assign global ref using this script
    }

    void LateUpdate()
    {
        // recreate all buffers if needed
        Profiler.BeginSample("UpdateAllInstanceTransformBufferIfNeeded");
        UpdateAllInstanceTransformBufferIfNeeded();
        Profiler.EndSample();
        //=====================================================================================================
        // rough quick big cell frustum culling in CPU first
        //=====================================================================================================
        cam = Camera.main;
        ////Do frustum culling using per cell bound
        ////https://docs.unity3d.com/ScriptReference/GeometryUtility.CalculateFrustumPlanes.html
        ////https://docs.unity3d.com/ScriptReference/GeometryUtility.TestPlanesAABB.html
        float cameraOriginalFarPlane = cam.farClipPlane;
        cam.farClipPlane = drawDistance;//allow drawDistance control    
        GeometryUtility.CalculateFrustumPlanes(cam, cameraFrustumPlanes);//Ordering: [0] = Left, [1] = Right, [2] = Down, [3] = Up, [4] = Near, [5] = Far
        cam.farClipPlane = cameraOriginalFarPlane;//revert far plane edit

        if (!useJobCull)
        {
            ////slow loop
            ////TODO: (A)replace this forloop by a quadtree test?
            ////TODO: (B)convert this forloop to job+burst? (UnityException: TestPlanesAABB can only be called from the main thread.)
            Profiler.BeginSample("CPU cell frustum culling (heavy)");

            List<int> visibleCellIDList = new List<int>();
            Vector3 offset = new Vector3(cellCountX * cellSize * 0.5f, 0f, cellCountZ * cellSize * 0.5f);
            for (int i = 0; i < cellCountX; i++)
            {
                for(int j = 0; j < cellCountZ; j++)
                {
                    Vector3 centerPosWS = new Vector3(i * cellSize, 0, j * cellSize) + transform.position - offset;
                    Vector3 sizeWS = new Vector3(cellSize, 0, cellSize);
                    Bounds cellBound = new Bounds(centerPosWS, sizeWS);

                    if (GeometryUtility.TestPlanesAABB(cameraFrustumPlanes, cellBound))
                    {
                        visibleCellIDList.Add(i * cellCountZ + j);
                    }
                }
            }
            visibleCellsID = visibleCellIDList.ToArray();
            Profiler.EndSample();
        }
        else
        {
            Profiler.BeginSample("CPU cell frustum culling Job");

            Vector3 offset = new Vector3(cellCountX * cellSize * 0.5f, 0f, cellCountZ * cellSize * 0.5f);

            NativeArray<PlaneAABB> planeAABBs = new NativeArray<PlaneAABB>(cameraFrustumPlanes.Length, Allocator.TempJob);
            for (int i = 0; i < cameraFrustumPlanes.Length; i++)
            {
                planeAABBs[i] = new PlaneAABB() { normal = cameraFrustumPlanes[i].normal, distance = cameraFrustumPlanes[i].distance };
            }

            NativeList<int> result = new NativeList<int>(cellCountX * cellCountZ, Allocator.TempJob);

            TestPlaneAABBJob testPlaneAABBJob = new TestPlaneAABBJob
            {
                planeAABBs = planeAABBs,
                cellSize = cellSize,
                //cellCountX = cellCountX,
                cellCountZ = cellCountZ,
                position = transform.position - offset,
                result = result.AsParallelWriter()
            };

            JobHandle jobHandle = testPlaneAABBJob.Schedule(cellCountX * cellCountZ, 64);
            jobHandle.Complete();
            //result.Sort();
            visibleCellsID = result.ToArray();
            result.Dispose();
            planeAABBs.Dispose();
            Profiler.EndSample();
        }

        Profiler.BeginSample("Set visibleCellsBuffer");
        if (visibleCellsID.Length > 0)
        {
            if (visibleCellsBuffer != null)
                visibleCellsBuffer.Release();
            visibleCellsBuffer = new ComputeBuffer(visibleCellsID.Length, sizeof(uint));
            visibleCellsBuffer.SetData(visibleCellsID);
            cullingComputeShader.SetBuffer(0, "_VisibleCellsBuffer", visibleCellsBuffer);
        }
        Profiler.EndSample();
    }

    private void OnGUI()
    {
        GUI.contentColor = Color.black;
        GUI.Label(new Rect(200, 0, 400, 60),
            $"After CPU cell frustum culling,\n" +
            $"-Visible cell count = {visibleCellsID.Length}/{cellCountX * cellCountZ}\n");

        shouldBatchDispatch = GUI.Toggle(new Rect(400, 400, 200, 100), shouldBatchDispatch, "shouldBatchDispatch");
    }

    void OnDisable()
    {
        //release all compute buffers
        if (allInstancesPosWSBuffer != null)
            allInstancesPosWSBuffer.Release();
        allInstancesPosWSBuffer = null;

        if (visibleInstancesOnlyPosWSIDBuffer != null)
            visibleInstancesOnlyPosWSIDBuffer.Release();
        visibleInstancesOnlyPosWSIDBuffer = null;

        if (argsBuffer != null)
            argsBuffer.Release();
        argsBuffer = null;

        if (visibleCellsBuffer != null)
            visibleCellsBuffer.Release();
        visibleCellsBuffer = null;

        if (allGrassPos.IsCreated)
            allGrassPos.Dispose();

        instance = null;
    }

    public Mesh GetGrassMeshCache()
    {
        if (!cachedGrassMesh)
        {
            //if not exist, create a 3 vertices hardcode triangle grass mesh
            cachedGrassMesh = new Mesh();

            //single grass (vertices)
            Vector3[] verts = new Vector3[3];
            verts[0] = new Vector3(-0.25f, 0);
            verts[1] = new Vector3(+0.25f, 0);
            verts[2] = new Vector3(-0.0f, 1);
            //single grass (Triangle index)
            int[] trinagles = new int[3] { 2, 1, 0, }; //order to fit Cull Back in grass shader

            cachedGrassMesh.SetVertices(verts);
            cachedGrassMesh.SetTriangles(trinagles, 0);
        }

        return cachedGrassMesh;
    }

    void UpdateAllInstanceTransformBufferIfNeeded()
    {
        //always update
        instanceMaterial.SetVector("_PivotPosWS", transform.position);
        instanceMaterial.SetVector("_BoundSize", new Vector2(transform.localScale.x, transform.localScale.z));

        //early exit if no need to update buffer
        if (instanceCountCache == allGrassPos.Length &&
            argsBuffer != null &&
            allInstancesPosWSBuffer != null &&
            visibleInstancesOnlyPosWSIDBuffer != null)
        {
            return;
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////
        /////////////////////////////////////////////////////////////////////////////////////////////////////

        Debug.Log("UpdateAllInstanceTransformBuffer (Slow)");

        ///////////////////////////
        // allInstancesPosWSBuffer buffer
        ///////////////////////////
        if (allInstancesPosWSBuffer != null)
            allInstancesPosWSBuffer.Release();
        allInstancesPosWSBuffer = new ComputeBuffer(allGrassPos.Length, sizeof(float)*3); //float3 posWS only, per grass

        if (visibleInstancesOnlyPosWSIDBuffer != null)
            visibleInstancesOnlyPosWSIDBuffer.Release();
        visibleInstancesOnlyPosWSIDBuffer = new ComputeBuffer(allGrassPos.Length, sizeof(uint), ComputeBufferType.Append); //uint only, per visible grass
        
        allInstancesPosWSBuffer.SetData(allGrassPos);

        instanceMaterial.SetBuffer("_AllInstancesTransformBuffer", allInstancesPosWSBuffer);
        instanceMaterial.SetBuffer("_VisibleInstanceOnlyTransformIDBuffer", visibleInstancesOnlyPosWSIDBuffer);

        ///////////////////////////
        // Indirect args buffer
        ///////////////////////////
        if (argsBuffer != null)
            argsBuffer.Release();
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);

        args[0] = (uint)GetGrassMeshCache().GetIndexCount(0);
        args[1] = (uint)allGrassPos.Length;
        args[2] = (uint)GetGrassMeshCache().GetIndexStart(0);
        args[3] = (uint)GetGrassMeshCache().GetBaseVertex(0);
        args[4] = 0;

        argsBuffer.SetData(args);

        ///////////////////////////
        // Update Cache
        ///////////////////////////
        //update cache to prevent future no-op buffer update, which waste performance
        instanceCountCache = allGrassPos.Length;

        //set buffer
        cullingComputeShader.SetBuffer(0, "_AllInstancesPosWSBuffer", allInstancesPosWSBuffer);
        cullingComputeShader.SetBuffer(0, "_VisibleInstancesOnlyPosWSIDBuffer", visibleInstancesOnlyPosWSIDBuffer);

        cullingComputeShader.SetInt("_GrassCountPreCell", cellGrassCount);
    }
}
