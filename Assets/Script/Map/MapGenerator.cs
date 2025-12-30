using System.Collections.Generic;
using UnityEngine;

public enum SpawnDistribution
{
    Random,
    PoissonDisc,
    Clustered,
    ClusteredPoisson
}

[System.Serializable]
public class ResourceSpawnSettings
{
    [Header("Basic")]
    public string Name = "Resource";
    public int NodeID;
    public bool Enabled = true;

    [Header("Count")]
    public int MinCount = 5;
    public int MaxCount = 20;

    [Header("Distribution")]
    public SpawnDistribution Distribution = SpawnDistribution.PoissonDisc;

    [Header("Poisson Disc Settings")]
    public float MinDistance = 3f;

    [Header("Cluster Settings")]
    public int ClusterCount = 3;
    public int NodesPerCluster = 5;
    public float ClusterRadius = 8f;
    public float ClusterMinDistance = 1.5f;

    [Header("Placement")]
    public int EdgePadding = 2;

    [Header("Variation")]
    [Range(0f, 0.5f)] public float ScaleVariation = 0.2f;
    public bool RandomRotation = true;
}

/// <summary>
/// �� ������ - GridCellVisualizer�� ������ ��ǥ ���
/// </summary>
public class MapGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Grid grid;
    [SerializeField] private GridLineSet gridLineSet;
    [SerializeField] private ResourceNodeDatabaseSO nodeDatabase;

    [Header("Clear Zone (�� �ε��� ����)")]
    [SerializeField] private int clearZoneCellX = 10;
    [SerializeField] private int clearZoneCellZ = 10;
    [SerializeField] private int clearZoneRadius = 3;

    [Header("Spawn Settings")]
    [SerializeField] private List<ResourceSpawnSettings> spawnSettings = new List<ResourceSpawnSettings>();

    [Header("Generation")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int randomSeed = 0;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool logGeneration = true;

    // �� ĳ�� (GridCellVisualizer�� ������ ������)
    private Vector3 gridOrigin;      // �� Grid�� ���� ��ġ
    private Vector2 gridTotalSize;
    private Vector2 cellSize;
    private float offsetX;
    private float offsetZ;
    private int gridCellCountX;
    private int gridCellCountZ;

    // ������
    private GridData environmentGridData;
    private List<ResourceNode> spawnedNodes = new List<ResourceNode>();
    private Transform nodesContainer;

    public List<ResourceNode> SpawnedNodes => spawnedNodes;

    private void Awake()
    {
        environmentGridData = new GridData();
        nodesContainer = new GameObject("ResourceNodes").transform;
        nodesContainer.SetParent(transform);
    }

    private void Start()
    {
        CacheGridSettings();

        if (generateOnStart)
        {
            GenerateMap();
        }
    }

    /// <summary>
    /// �� GridCellVisualizer�� �����ϰ� ���� ĳ��
    /// </summary>
    private void CacheGridSettings()
    {
        // �ڡڡ� Grid�� ���� ��ġ ���� �ڡڡ�
        if (grid != null)
        {
            gridOrigin = grid.transform.position;
        }
        else
        {
            gridOrigin = Vector3.zero;
            Debug.LogWarning("[MapGenerator] Grid�� �������� �ʾҽ��ϴ�!");
        }

        // GridLineSet���� ũ�� ����
        if (gridLineSet != null)
        {
            gridTotalSize = gridLineSet.gridTotalSize;
            cellSize = gridLineSet.cellSize;
        }
        else
        {
            gridTotalSize = new Vector2(20f, 20f);
            cellSize = new Vector2(1f, 1f);
            Debug.LogWarning("[MapGenerator] GridLineSet�� �������� �ʾҽ��ϴ�!");
        }

        // �� ����
        gridCellCountX = Mathf.RoundToInt(gridTotalSize.x / cellSize.x);
        gridCellCountZ = Mathf.RoundToInt(gridTotalSize.y / cellSize.y);

        // ������ (�׸��� �߾� ����)
        offsetX = -gridTotalSize.x / 2f;
        offsetZ = -gridTotalSize.y / 2f;

        Debug.Log($"[MapGenerator] === ���� ĳ�� �Ϸ� ===");
        Debug.Log($"  gridOrigin: {gridOrigin}");
        Debug.Log($"  gridTotalSize: {gridTotalSize}");
        Debug.Log($"  cellSize: {cellSize}");
        Debug.Log($"  cellCount: {gridCellCountX} x {gridCellCountZ}");
        Debug.Log($"  offset: ({offsetX}, {offsetZ})");
    }

    // ==================== �ڡڡ� ��ǥ ��� (GridCellVisualizer�� 100% ����) �ڡڡ� ====================

    /// <summary>
    /// �� �� �ڳ� ��ǥ (GridCellVisualizer�� ����)
    /// </summary>
    private Vector3 GetCellCornerPosition(int cellX, int cellZ)
    {
        return new Vector3(
            gridOrigin.x + offsetX + (cellX * cellSize.x),
            gridOrigin.y,
            gridOrigin.z + offsetZ + (cellZ * cellSize.y)
        );
    }

    /// <summary>
    /// �� �� �߾� ��ǥ (GridCellVisualizer�� ����)
    /// </summary>
    private Vector3 GetCellCenterPosition(int cellX, int cellZ)
    {
        Vector3 corner = GetCellCornerPosition(cellX, cellZ);
        return corner + new Vector3(cellSize.x * 0.5f, 0, cellSize.y * 0.5f);
    }

    // ==================== ���� ���� ====================

    [ContextMenu("Generate Map")]
    public void GenerateMap()
    {
        if (nodeDatabase == null)
        {
            Debug.LogError("[MapGenerator] ResourceNodeDatabaseSO�� �����ϴ�!");
            return;
        }

        if (spawnSettings.Count == 0)
        {
            Debug.LogError("[MapGenerator] Spawn Settings�� ����ֽ��ϴ�!");
            return;
        }

        // ���� �ٽ� ĳ�� (�����Ϳ��� ������� �� ����)
        CacheGridSettings();

        Random.InitState(useRandomSeed ? System.Environment.TickCount : randomSeed);
        ClearMap();

        int totalSpawned = 0;

        foreach (var settings in spawnSettings)
        {
            if (!settings.Enabled) continue;

            ResourceNodeData nodeData = nodeDatabase.GetNodeByID(settings.NodeID);
            if (nodeData == null)
            {
                Debug.LogWarning($"[MapGenerator] NodeID {settings.NodeID} ����: {settings.Name}");
                continue;
            }

            if (nodeData.Prefab == null)
            {
                Debug.LogWarning($"[MapGenerator] {nodeData.Name} Prefab ����!");
                continue;
            }

            int spawnedCount = SpawnNodes(settings, nodeData);
            totalSpawned += spawnedCount;

            if (logGeneration)
                Debug.Log($"[MapGenerator] {settings.Name}: {spawnedCount}�� ����");
        }

        if (logGeneration)
            Debug.Log($"[MapGenerator] �� {totalSpawned}�� ���� �Ϸ�");
    }

    private int SpawnNodes(ResourceSpawnSettings settings, ResourceNodeData nodeData)
    {
        List<Vector2Int> cellPositions = GenerateCellPositions(settings);
        int targetCount = Random.Range(settings.MinCount, settings.MaxCount + 1);
        int spawnedCount = 0;

        foreach (var cellPos in cellPositions)
        {
            if (spawnedCount >= targetCount) break;

            if (TrySpawnNodeAtCell(nodeData, cellPos.x, cellPos.y, settings))
            {
                spawnedCount++;
            }
        }

        return spawnedCount;
    }

    /// <summary>
    /// �� ��� ���� (�� �߾ӿ� ��Ȯ��!)
    /// </summary>
    private bool TrySpawnNodeAtCell(ResourceNodeData nodeData, int cellX, int cellZ, ResourceSpawnSettings settings)
    {
        Vector2Int size = nodeData.Size;

        // Clear Zone üũ (��� ��)
        for (int x = 0; x < size.x; x++)
        {
            for (int z = 0; z < size.y; z++)
            {
                if (IsCellInClearZone(cellX + x, cellZ + z))
                    return false;
            }
        }

        // ���� üũ (��� ��)
        for (int x = 0; x < size.x; x++)
        {
            for (int z = 0; z < size.y; z++)
            {
                if (!IsCellInBounds(cellX + x, cellZ + z))
                    return false;
            }
        }

        // ��ġ ���� üũ
        Vector3Int gridPos = CellIndexToGridPosition(cellX, cellZ);

        if (GridDataManager.Instance != null)
        {
            if (!GridDataManager.Instance.CanPlaceAtCell(cellX, cellZ, size))
                return false;
        }
        else
        {
            if (!environmentGridData.CanPlaceObejctAt(gridPos, size))
                return false;
        }

        // �ڡڡ� ũ�⿡ �´� �߾� ��ǥ ��� �ڡڡ�
        // ���ϴ� �ڳ� ��ǥ
        Vector3 corner = GetCellCornerPosition(cellX, cellZ);

        // ũ�⿡ ���� �߾� ������
        // 1x1: (0.5, 0.5) - 1ĭ �߾�
        // 2x2: (1.0, 1.0) - 4ĭ �߾�
        // 3x3: (1.5, 1.5) - 9ĭ �߾�
        Vector3 worldPos = corner + new Vector3(
            size.x * cellSize.x * 0.5f,
            0,
            size.y * cellSize.y * 0.5f
        );

        if (logGeneration)
        {
            Debug.Log($"[MapGenerator] {nodeData.Name} Size({size.x}x{size.y}): Cell({cellX},{cellZ}) �� World({worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2})");
        }

        // ����
        GameObject nodeObj = Instantiate(nodeData.Prefab, worldPos, Quaternion.identity);
        nodeObj.transform.SetParent(nodesContainer);
        nodeObj.name = $"{nodeData.Name}_{spawnedNodes.Count}";

        // ������
        if (settings.ScaleVariation > 0)
        {
            float scale = 1f + Random.Range(-settings.ScaleVariation, settings.ScaleVariation);
            nodeObj.transform.localScale *= scale;
        }

        // ȸ��
        if (settings.RandomRotation)
        {
            nodeObj.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        }

        // ResourceNode ������Ʈ
        ResourceNode node = nodeObj.GetComponent<ResourceNode>();
        if (node == null)
            node = nodeObj.AddComponent<ResourceNode>();

        node.Initialize(nodeData, gridPos);

        // GridData ��� (origin ��ġ�� �״�� ���ϴ� ��!)
        if (GridDataManager.Instance != null)
        {
            GridDataManager.Instance.PlaceObjectAtCell(cellX, cellZ, size, nodeData.ID, PlacedObjectType.ResourceNode, nodeObj);
        }
        else
        {
            environmentGridData.AddObjectAt(gridPos, size, nodeData.ID, spawnedNodes.Count);
        }

        spawnedNodes.Add(node);
        node.OnDepleted += HandleNodeDepleted;

        // ★ TaskManager에 채집 작업 등록
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.AddHarvestTask(node);
        }

        return true;
    }

    // ==================== �� ��ġ ���� ====================

    private List<Vector2Int> GenerateCellPositions(ResourceSpawnSettings settings)
    {
        switch (settings.Distribution)
        {
            case SpawnDistribution.Random:
                return GenerateRandomCellPositions(settings);
            case SpawnDistribution.PoissonDisc:
                return GeneratePoissonCellPositions(settings);
            case SpawnDistribution.Clustered:
            case SpawnDistribution.ClusteredPoisson:
                return GenerateClusteredCellPositions(settings);
            default:
                return new List<Vector2Int>();
        }
    }

    private List<Vector2Int> GenerateRandomCellPositions(ResourceSpawnSettings settings)
    {
        List<Vector2Int> positions = new List<Vector2Int>();
        HashSet<Vector2Int> used = new HashSet<Vector2Int>();
        int padding = settings.EdgePadding;

        for (int i = 0; i < settings.MaxCount * 3; i++)
        {
            int x = Random.Range(padding, gridCellCountX - padding);
            int z = Random.Range(padding, gridCellCountZ - padding);
            Vector2Int pos = new Vector2Int(x, z);

            if (!used.Contains(pos) && !IsCellInClearZone(x, z))
            {
                positions.Add(pos);
                used.Add(pos);
            }
        }

        return positions;
    }

    private List<Vector2Int> GeneratePoissonCellPositions(ResourceSpawnSettings settings)
    {
        List<Vector2Int> positions = new List<Vector2Int>();
        HashSet<Vector2Int> used = new HashSet<Vector2Int>();
        int padding = settings.EdgePadding;
        int minDist = Mathf.Max(1, Mathf.CeilToInt(settings.MinDistance));

        // ù ����Ʈ
        for (int attempt = 0; attempt < 100 && positions.Count == 0; attempt++)
        {
            int x = Random.Range(padding, gridCellCountX - padding);
            int z = Random.Range(padding, gridCellCountZ - padding);
            if (!IsCellInClearZone(x, z))
            {
                positions.Add(new Vector2Int(x, z));
                used.Add(new Vector2Int(x, z));
            }
        }

        if (positions.Count == 0) return positions;

        List<int> active = new List<int> { 0 };

        while (active.Count > 0 && positions.Count < settings.MaxCount * 2)
        {
            int idx = Random.Range(0, active.Count);
            Vector2Int basePos = positions[active[idx]];
            bool found = false;

            for (int i = 0; i < 30; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                int dist = Random.Range(minDist, minDist * 2 + 1);
                int newX = basePos.x + Mathf.RoundToInt(Mathf.Cos(angle) * dist);
                int newZ = basePos.y + Mathf.RoundToInt(Mathf.Sin(angle) * dist);
                Vector2Int newPos = new Vector2Int(newX, newZ);

                if (newX < padding || newX >= gridCellCountX - padding ||
                    newZ < padding || newZ >= gridCellCountZ - padding)
                    continue;

                if (used.Contains(newPos) || IsCellInClearZone(newX, newZ))
                    continue;

                bool tooClose = false;
                foreach (var p in positions)
                {
                    if (Vector2Int.Distance(p, newPos) < minDist)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    positions.Add(newPos);
                    used.Add(newPos);
                    active.Add(positions.Count - 1);
                    found = true;
                    break;
                }
            }

            if (!found) active.RemoveAt(idx);
        }

        return positions;
    }

    private List<Vector2Int> GenerateClusteredCellPositions(ResourceSpawnSettings settings)
    {
        List<Vector2Int> positions = new List<Vector2Int>();
        HashSet<Vector2Int> used = new HashSet<Vector2Int>();
        int padding = settings.EdgePadding;
        int clusterRadius = Mathf.Max(1, Mathf.CeilToInt(settings.ClusterRadius));

        for (int c = 0; c < settings.ClusterCount; c++)
        {
            int centerX = Random.Range(padding + clusterRadius, gridCellCountX - padding - clusterRadius);
            int centerZ = Random.Range(padding + clusterRadius, gridCellCountZ - padding - clusterRadius);

            for (int attempt = 0; IsCellInClearZone(centerX, centerZ) && attempt < 50; attempt++)
            {
                centerX = Random.Range(padding + clusterRadius, gridCellCountX - padding - clusterRadius);
                centerZ = Random.Range(padding + clusterRadius, gridCellCountZ - padding - clusterRadius);
            }

            for (int n = 0; n < settings.NodesPerCluster; n++)
            {
                Vector2 offset = Random.insideUnitCircle * clusterRadius;
                int x = centerX + Mathf.RoundToInt(offset.x);
                int z = centerZ + Mathf.RoundToInt(offset.y);
                Vector2Int pos = new Vector2Int(x, z);

                if (x >= padding && x < gridCellCountX - padding &&
                    z >= padding && z < gridCellCountZ - padding &&
                    !used.Contains(pos) && !IsCellInClearZone(x, z))
                {
                    positions.Add(pos);
                    used.Add(pos);
                }
            }
        }

        return positions;
    }

    // ==================== ��ƿ��Ƽ ====================

    private Vector3Int CellIndexToGridPosition(int cellX, int cellZ)
    {
        int gridX = cellX + Mathf.RoundToInt(offsetX);
        int gridZ = cellZ + Mathf.RoundToInt(offsetZ);
        return new Vector3Int(gridX, 0, gridZ);
    }

    private bool IsCellInClearZone(int cellX, int cellZ)
    {
        int dx = cellX - clearZoneCellX;
        int dz = cellZ - clearZoneCellZ;
        return (dx * dx + dz * dz) < (clearZoneRadius * clearZoneRadius);
    }

    private bool IsCellInBounds(int cellX, int cellZ)
    {
        return cellX >= 0 && cellX < gridCellCountX &&
               cellZ >= 0 && cellZ < gridCellCountZ;
    }

    // ==================== ���� ====================

    [ContextMenu("Clear Map")]
    public void ClearMap()
    {
        foreach (var node in spawnedNodes)
        {
            if (node != null)
            {
                node.OnDepleted -= HandleNodeDepleted;
                Destroy(node.gameObject);
            }
        }
        spawnedNodes.Clear();
        environmentGridData = new GridData();
    }

    private void HandleNodeDepleted(ResourceNode node)
    {
        if (!node.Data.CanRespawn)
        {
            if (GridDataManager.Instance != null)
                GridDataManager.Instance.RemoveObject(node.GridPosition);
            else
                environmentGridData.RemoveObjectAt(node.GridPosition);
            spawnedNodes.Remove(node);
        }
        node.OnDepleted -= HandleNodeDepleted;
    }

    public GridData GetEnvironmentGridData()
    {
        return GridDataManager.Instance != null
            ? GridDataManager.Instance.GetRawGridData()
            : environmentGridData;
    }

    // ==================== ����� ====================

    [ContextMenu("Test: Compare with GridCellVisualizer")]
    public void TestCompareCoordinates()
    {
        CacheGridSettings();

        Debug.Log("=== MapGenerator vs GridCellVisualizer �� ===");
        Debug.Log($"gridOrigin: {gridOrigin}");
        Debug.Log($"offsetX: {offsetX}, offsetZ: {offsetZ}");
        Debug.Log($"cellSize: {cellSize}");

        int[][] testCells = {
            new[] {0, 0},
            new[] {1, 1},
            new[] {10, 10},
            new[] {gridCellCountX - 1, gridCellCountZ - 1}
        };

        foreach (var cell in testCells)
        {
            Vector3 corner = GetCellCornerPosition(cell[0], cell[1]);
            Vector3 center = GetCellCenterPosition(cell[0], cell[1]);
            Debug.Log($"Cell({cell[0]},{cell[1]}) �� Corner:{corner}, Center:{center}");
        }
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || gridLineSet == null) return;

        // ��ü ���
        Gizmos.color = Color.yellow;
        Vector3 center = grid != null ? grid.transform.position : Vector3.zero;
        Gizmos.DrawWireCube(center, new Vector3(gridTotalSize.x, 0.1f, gridTotalSize.y));

        // Clear Zone
        if (Application.isPlaying || gridOrigin != Vector3.zero)
        {
            Gizmos.color = Color.green;
            Vector3 clearCenter = GetCellCenterPosition(clearZoneCellX, clearZoneCellZ);
            DrawCircleGizmo(clearCenter, clearZoneRadius);
        }
    }

    private void DrawCircleGizmo(Vector3 center, float radius)
    {
        for (int i = 0; i < 32; i++)
        {
            float a1 = (i / 32f) * Mathf.PI * 2f;
            float a2 = ((i + 1) / 32f) * Mathf.PI * 2f;
            Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
            Vector3 p2 = center + new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2)) * radius;
            Gizmos.DrawLine(p1, p2);
        }
    }
}