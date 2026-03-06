using System.Collections.Generic;
using UnityEngine;

public class SpatialGrid
{
    // 链表节点结构
    public struct Entry
    {
        public int id;
        public Vector3 position;
        public float radius;
        public int next; // 链表中下一个元素的索引
    }

    private readonly int[] cells;      // 格子数组，存储链表头索引
    private Entry[] entries;  // 物体数据仓库
    private int count;

    public Entry GetEntry(int id)
    {
        return entries[id];
    }
    
    private readonly float cellSize;
    private readonly int gridWidth;
    private readonly int gridHeight;
    private readonly int gridDepth;
    private readonly Vector3 minPos;

    public SpatialGrid(Vector3 minPos, Vector3 maxPos, float cellSize, int capacity = 4096)
    {
        this.cellSize = cellSize;
        this.minPos = minPos;
        
        // 计算网格尺寸
        Vector3 size = maxPos - minPos;
        this.gridWidth = Mathf.CeilToInt(size.x / cellSize);
        this.gridHeight = Mathf.CeilToInt(size.y / cellSize);
        this.gridDepth = Mathf.CeilToInt(size.z / cellSize);
        this.gridWidth = Mathf.Max(this.gridWidth, 1);
        this.gridHeight =  Mathf.Max(this.gridHeight, 1);
        this.gridDepth = Mathf.Max(this.gridDepth, 1);
        int totalCells = gridWidth * gridHeight * gridDepth;
        this.cells = new int[totalCells];
        this.entries = new Entry[capacity];
        
        Clear();
    }

    public void Clear()
    {
        count = 0;
        // 重置格子头指针为 -1
        System.Array.Fill(cells, -1);
    }

    public void Add(Vector3 pos, int id, float radius)
    {
        if (count >= entries.Length) return; // 或扩容

        // 1. 计算格子索引
        int gx = (int)((pos.x - minPos.x) / cellSize);
        int gy = (int)((pos.y - minPos.y) / cellSize);
        int gz = (int)((pos.z - minPos.z) / cellSize);
        // 边界保护
        if (gx < 0 || gx >= gridWidth || gy < 0 || gy >= gridHeight || gz < 0 || gz >= gridDepth)
            return;
        
        int cellIndex = (gz * gridHeight + gy) * gridWidth + gx;

        // 2. 插入链表头 (头插法)
        entries[count] = new Entry
        {
            id = id,
            position = pos,
            radius = radius,
            next = cells[cellIndex] // 指向旧的头
        };
        
        cells[cellIndex] = count; // 更新头为当前元素
        count++;
    }

    public void NeighborScan(Vector3 center, float searchRadius, List<(int, float)> results)
    {
        results.Clear();

        // 计算搜索范围涉及的格子
        int minX = (int)((center.x - searchRadius - minPos.x) / cellSize);
        int maxX = (int)((center.x + searchRadius - minPos.x) / cellSize);
        int minY = (int)((center.y - searchRadius - minPos.y) / cellSize);
        int maxY = (int)((center.y + searchRadius - minPos.y) / cellSize);
        int minZ = (int)((center.z - searchRadius - minPos.z) / cellSize);
        int maxZ = (int)((center.z + searchRadius - minPos.z) / cellSize);

        // Clamp 索引
        minX = Mathf.Max(0, minX); maxX = Mathf.Min(gridWidth - 1, maxX);
        minY = Mathf.Max(0, minY); maxY = Mathf.Min(gridHeight - 1, maxY);
        minZ = Mathf.Max(0, minZ); maxZ = Mathf.Min(gridDepth - 1, maxZ);
        
        // 遍历格子
        for (int z = minZ; z <= maxZ; z++)
        {
            int zOffset = z * gridHeight * gridWidth;
            for (int y = minY; y <= maxY; y++)
            {
                int yOffset = y * gridWidth;
                for (int x = minX; x <= maxX; x++)
                {
                    int cellIndex = zOffset + yOffset + x;
                    int currentIdx = cells[cellIndex];
                    
                    while (currentIdx != -1)
                    {
                        ref Entry item = ref entries[currentIdx];
                        
                        // 距离判定
                        float dx = item.position.x - center.x;
                        float dy = item.position.y - center.y;
                        float dz = item.position.z - center.z;
                        float sqrDist = dx * dx + dy * dy + dz * dz;
                        
                        float combinedRadius = searchRadius + item.radius;
                        
                        if (sqrDist <= combinedRadius * combinedRadius)
                        {
                            results.Add((item.id, Mathf.Sqrt(sqrDist)));
                        }

                        currentIdx = item.next;
                    }
                }
            }
        }
    }
}