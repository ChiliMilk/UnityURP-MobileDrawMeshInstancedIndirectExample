using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[ExecuteAlways]
public class InstancedIndirectGrassPosDefine : MonoBehaviour
{
    [Range(1, 40000000)]
    public int instanceCount = 1000000;
    public float drawDistance = 125;
    private float cellSize = 5;

    // Start is called before the first frame update
    void Start()
    {
        UpdatePosIfNeeded();
    }

    private void Update()
    {
        UpdatePosIfNeeded();
    }
    private void OnGUI()
    {
        GUI.Label(new Rect(300, 50, 200, 30), "Instance Count: " + instanceCount / 1000000 + "Million");
        instanceCount = Mathf.Max(1, (int)(GUI.HorizontalSlider(new Rect(300, 100, 200, 30), instanceCount / 1000000f, 1, 10)) * 1000000);

        GUI.Label(new Rect(300, 150, 200, 30), "Draw Distance: " + drawDistance);
        drawDistance = Mathf.Max(1, (int)(GUI.HorizontalSlider(new Rect(300, 200, 200, 30), drawDistance / 25f, 1, 8)) * 25);
        InstancedIndirectGrassRenderer.instance.drawDistance = drawDistance;
    }
    private void UpdatePosIfNeeded()
    {
        if (InstancedIndirectGrassRenderer.instance.allGrassPos.IsCreated)
            return;

        Debug.Log("UpdatePos (Slow)");

        //same seed to keep grass visual the same
        UnityEngine.Random.InitState(123);

        //auto keep density the same
        float scale = Mathf.Sqrt(instanceCount / 4);
        transform.localScale = new Vector3(scale, transform.localScale.y, scale);

        int cellCountX = (int)(transform.lossyScale.x / cellSize);
        int cellCountZ = (int)(transform.lossyScale.z / cellSize);
        int cellCount = cellCountX * cellCountZ;
        int cellGrassCount = instanceCount / cellCount;

        cellSize = InstancedIndirectGrassRenderer.instance.cellSize;
        InstancedIndirectGrassRenderer.instance.cellCountX = cellCountX;
        InstancedIndirectGrassRenderer.instance.cellCountZ = cellCountZ;
        InstancedIndirectGrassRenderer.instance.cellGrassCount = cellGrassCount;
        InstancedIndirectGrassRenderer.instance.allGrassPos = new Unity.Collections.NativeArray<float3>(instanceCount, Unity.Collections.Allocator.Persistent);

        Vector3 centerOffset = new Vector3(transform.lossyScale.x * 0.5f, 0f, transform.lossyScale.z * 0.5f);
        int index = 0;
        for (int i = 0; i < cellCountX; i++)
        {
            for(int j = 0; j < cellCountZ; j++)
            {
                Vector3 center = new Vector3(i * cellSize, 0, j * cellSize) + transform.position - centerOffset;
                int count = cellGrassCount;
                while(count > 0)
                {
                    Vector3 pos = center;
                    pos.x += UnityEngine.Random.Range(-1f, 1f) * cellSize * 0.5f;
                    pos.z += UnityEngine.Random.Range(-1f, 1f) * cellSize * 0.5f;
                    InstancedIndirectGrassRenderer.instance.allGrassPos[index] = pos;
                    count--;
                    index++;
                }
            }
        }
    }

}
