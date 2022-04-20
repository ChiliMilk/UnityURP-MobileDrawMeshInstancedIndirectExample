using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Burst;

public struct PlaneAABB
{
    public float3 normal;
    public float distance;
}

[BurstCompile]
public struct TestPlaneAABBJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<PlaneAABB> planeAABBs;
    [ReadOnly] public float3 position;
    [ReadOnly] public float cellSize;
    //[ReadOnly] public int cellCountX;
    [ReadOnly] public int cellCountZ;
   
    [WriteOnly] public NativeList<int>.ParallelWriter result;

    public void Execute(int index)
    {
        float3 center = new float3(index / cellCountZ * cellSize, 0, index % cellCountZ * cellSize) + position;

        for (int i = 0; i< planeAABBs.Length; i++)
        {
            PlaneAABB plane = planeAABBs[i];
            float3 normalSign = math.sign(plane.normal);
            float3 maxPoint = center + new float3(cellSize, 0, cellSize) * normalSign;

            if(math.dot(maxPoint,plane.normal) + plane.distance < 0)
            {
                return;
            }
        }
        result.AddNoResize(index);
    }
}



