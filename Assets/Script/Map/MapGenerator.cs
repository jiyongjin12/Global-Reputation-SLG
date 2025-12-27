using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자원 스폰 분포 타입
/// </summary>
public enum SpawnDistribution
{
    Random,             // 완전 랜덤
    PoissonDisc,        // 포아송 디스크 (균일한 분포)
    Clustered,          // 클러스터 (군집)
    ClusteredPoisson    // 클러스터 + 포아송 (클러스터 내에서 균일)
}

/// <summary>
/// 자연환경 스폰 설정
/// </summary>
[System.Serializable]
public class ResourceSpawnSettings
{
    [Header("Basic")]
    public string Name = "Resource";
    [Tooltip("ResourceNodeDatabaseSO에서 사용할 노드 ID")]
    public int NodeID;
    public bool Enabled = true;

    [Header("Count")]
    public int MinCount = 5;
    public int MaxCount = 20;

    [Header("Distribution")]
    public SpawnDistribution Distribution = SpawnDistribution.PoissonDisc;

    [Header("Poisson Disc Settings")]
    [Tooltip("최소 간격 (이 거리 안에 다른 노드가 없음)")]
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
/// 맵 생성기 - 포아송 디스크 샘플링 기반
/// </summary>
public class MapGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Grid grid;
    [SerializeField] private ResourceNodeDatabaseSO nodeDatabase;

    [Header("Map Settings")]
    [SerializeField] private Vector2Int mapSize = new Vector2Int(50, 50);
    [SerializeField] private Vector2Int mapOffset = new Vector2Int(-25, -25);

    [Header("Clear Zone (시작 지점)")]
    [SerializeField] private Vector2Int clearZoneCenter = Vector2Int.zero;
    [SerializeField] private int clearZoneRadius = 5;

    [Header("Spawn Settings")]
    [SerializeField] private List<ResourceSpawnSettings> spawnSettings = new List<ResourceSpawnSettings>();

    [Header("Generation")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int randomSeed = 0;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool logGeneration = true;

    // 데이터
    private GridData environmentGridData;
    private List<ResourceNode> spawnedNodes = new List<ResourceNode>();
    private Transform nodesContainer;

    // Properties
    public GridData EnvironmentGridData => environmentGridData;
    public List<ResourceNode> SpawnedNodes => spawnedNodes;

    private void Awake()
    {
        environmentGridData = new GridData();
        nodesContainer = new GameObject("ResourceNodes").transform;
        nodesContainer.SetParent(transform);
    }

    private void Start()
    {
        if (generateOnStart)
        {
            GenerateMap();
        }
    }

    // ==================== 메인 생성 ====================

    [ContextMenu("Generate Map")]
    public void GenerateMap()
    {
        if (nodeDatabase == null)
        {
            Debug.LogError("[MapGenerator] ResourceNodeDatabaseSO가 설정되지 않았습니다!");
            return;
        }

        // 시드 설정
        Random.InitState(useRandomSeed ? System.Environment.TickCount : randomSeed);

        ClearMap();

        int totalSpawned = 0;

        foreach (var settings in spawnSettings)
        {
            if (!settings.Enabled) continue;

            // 데이터베이스에서 노드 데이터 가져오기
            ResourceNodeData nodeData = nodeDatabase.GetNodeByID(settings.NodeID);
            if (nodeData == null)
            {
                Debug.LogWarning($"[MapGenerator] ID {settings.NodeID}에 해당하는 노드를 찾을 수 없습니다: {settings.Name}");
                continue;
            }

            if (nodeData.Prefab == null)
            {
                Debug.LogWarning($"[MapGenerator] {nodeData.Name}의 Prefab이 없습니다!");
                continue;
            }

            int spawnedCount = SpawnNodes(settings, nodeData);

            if (logGeneration)
            {
                Debug.Log($"[MapGenerator] {settings.Name}: {spawnedCount}개 생성 ({settings.Distribution})");
            }

            totalSpawned += spawnedCount;
        }

        if (logGeneration)
        {
            Debug.Log($"[MapGenerator] 총 {totalSpawned}개 자원 노드 생성 완료");
        }
    }

    /// <summary>
    /// 분포 타입에 따라 노드 생성
    /// </summary>
    private int SpawnNodes(ResourceSpawnSettings settings, ResourceNodeData nodeData)
    {
        List<Vector2> points = GeneratePoints(settings);
        int targetCount = Random.Range(settings.MinCount, settings.MaxCount + 1);
        int spawnedCount = 0;

        foreach (var point in points)
        {
            if (spawnedCount >= targetCount) break;

            Vector3Int gridPos = LocalToGridPosition(point);

            if (TrySpawnNode(nodeData, gridPos, settings))
            {
                spawnedCount++;
            }
        }

        return spawnedCount;
    }

    // ==================== 포인트 생성 ====================

    /// <summary>
    /// 분포 타입에 따라 포인트 생성
    /// </summary>
    private List<Vector2> GeneratePoints(ResourceSpawnSettings settings)
    {
        int targetCount = settings.MaxCount * 2; // 여유있게 생성

        switch (settings.Distribution)
        {
            case SpawnDistribution.Random:
                return GenerateRandomPoints(targetCount, settings.EdgePadding);

            case SpawnDistribution.PoissonDisc:
                return GeneratePoissonPoints(targetCount, settings.MinDistance, settings.EdgePadding);

            case SpawnDistribution.Clustered:
                return GenerateClusteredPoints(settings);

            case SpawnDistribution.ClusteredPoisson:
                return GenerateClusteredPoissonPoints(settings);

            default:
                return new List<Vector2>();
        }
    }

    /// <summary>
    /// 완전 랜덤 포인트
    /// </summary>
    private List<Vector2> GenerateRandomPoints(int count, int padding)
    {
        List<Vector2> points = new List<Vector2>();

        for (int i = 0; i < count; i++)
        {
            points.Add(new Vector2(
                Random.Range(padding, mapSize.x - padding),
                Random.Range(padding, mapSize.y - padding)
            ));
        }

        return points;
    }

    /// <summary>
    /// 포아송 디스크 샘플링 (Bridson's Algorithm)
    /// </summary>
    private List<Vector2> GeneratePoissonPoints(int maxCount, float minDist, int padding)
    {
        float cellSize = minDist / Mathf.Sqrt(2);
        int gridWidth = Mathf.CeilToInt(mapSize.x / cellSize);
        int gridHeight = Mathf.CeilToInt(mapSize.y / cellSize);

        int[,] sampleGrid = new int[gridWidth, gridHeight];
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                sampleGrid[x, y] = -1;

        List<Vector2> points = new List<Vector2>();
        List<Vector2> activeList = new List<Vector2>();

        // 첫 포인트
        Vector2 firstPoint = new Vector2(
            Random.Range(padding, mapSize.x - padding),
            Random.Range(padding, mapSize.y - padding)
        );

        AddPoissonPoint(firstPoint, points, activeList, sampleGrid, cellSize, gridWidth, gridHeight);

        int k = 30; // 시도 횟수

        while (activeList.Count > 0 && points.Count < maxCount)
        {
            int randomIndex = Random.Range(0, activeList.Count);
            Vector2 point = activeList[randomIndex];
            bool found = false;

            for (int i = 0; i < k; i++)
            {
                Vector2 newPoint = GetRandomPointAround(point, minDist);

                if (!IsInBounds(newPoint, padding)) continue;
                if (HasNeighbor(newPoint, points, sampleGrid, cellSize, minDist, gridWidth, gridHeight)) continue;

                AddPoissonPoint(newPoint, points, activeList, sampleGrid, cellSize, gridWidth, gridHeight);
                found = true;
                break;
            }

            if (!found)
            {
                activeList.RemoveAt(randomIndex);
            }
        }

        return points;
    }

    /// <summary>
    /// 원 안에서 포아송 포인트 생성
    /// </summary>
    private List<Vector2> GeneratePoissonPointsInCircle(Vector2 center, float radius, float minDist, int maxCount)
    {
        List<Vector2> points = new List<Vector2>();
        List<Vector2> activeList = new List<Vector2>();

        points.Add(center);
        activeList.Add(center);

        int k = 30;

        while (activeList.Count > 0 && points.Count < maxCount)
        {
            int randomIndex = Random.Range(0, activeList.Count);
            Vector2 point = activeList[randomIndex];
            bool found = false;

            for (int i = 0; i < k; i++)
            {
                Vector2 newPoint = GetRandomPointAround(point, minDist);

                // 원 안에 있는지 체크
                if (Vector2.Distance(newPoint, center) > radius) continue;

                // 기존 포인트와 거리 체크
                bool tooClose = false;
                foreach (var p in points)
                {
                    if (Vector2.Distance(p, newPoint) < minDist)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    points.Add(newPoint);
                    activeList.Add(newPoint);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                activeList.RemoveAt(randomIndex);
            }
        }

        return points;
    }

    /// <summary>
    /// 클러스터 포인트
    /// </summary>
    private List<Vector2> GenerateClusteredPoints(ResourceSpawnSettings settings)
    {
        List<Vector2> points = new List<Vector2>();

        for (int c = 0; c < settings.ClusterCount; c++)
        {
            Vector2 clusterCenter = GetRandomClusterCenter(settings);

            for (int i = 0; i < settings.NodesPerCluster; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2);
                float distance = Random.Range(0f, settings.ClusterRadius);

                Vector2 point = clusterCenter + new Vector2(
                    Mathf.Cos(angle) * distance,
                    Mathf.Sin(angle) * distance
                );

                points.Add(point);
            }
        }

        return points;
    }

    /// <summary>
    /// 클러스터 + 포아송 포인트
    /// </summary>
    private List<Vector2> GenerateClusteredPoissonPoints(ResourceSpawnSettings settings)
    {
        List<Vector2> points = new List<Vector2>();

        for (int c = 0; c < settings.ClusterCount; c++)
        {
            Vector2 clusterCenter = GetRandomClusterCenter(settings);

            List<Vector2> clusterPoints = GeneratePoissonPointsInCircle(
                clusterCenter,
                settings.ClusterRadius,
                settings.ClusterMinDistance,
                settings.NodesPerCluster
            );

            points.AddRange(clusterPoints);
        }

        return points;
    }

    // ==================== 포아송 헬퍼 ====================

    private void AddPoissonPoint(Vector2 point, List<Vector2> points, List<Vector2> activeList,
                                  int[,] grid, float cellSize, int gridWidth, int gridHeight)
    {
        points.Add(point);
        activeList.Add(point);

        int gx = Mathf.FloorToInt(point.x / cellSize);
        int gy = Mathf.FloorToInt(point.y / cellSize);

        if (gx >= 0 && gx < gridWidth && gy >= 0 && gy < gridHeight)
        {
            grid[gx, gy] = points.Count - 1;
        }
    }

    private Vector2 GetRandomPointAround(Vector2 center, float minDist)
    {
        float angle = Random.Range(0f, Mathf.PI * 2);
        float distance = Random.Range(minDist, minDist * 2);
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }

    private bool IsInBounds(Vector2 point, int padding)
    {
        return point.x >= padding && point.x < mapSize.x - padding &&
               point.y >= padding && point.y < mapSize.y - padding;
    }

    private bool HasNeighbor(Vector2 point, List<Vector2> points, int[,] grid,
                             float cellSize, float minDist, int gridWidth, int gridHeight)
    {
        int gx = Mathf.FloorToInt(point.x / cellSize);
        int gy = Mathf.FloorToInt(point.y / cellSize);

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                int checkX = gx + dx;
                int checkY = gy + dy;

                if (checkX >= 0 && checkX < gridWidth && checkY >= 0 && checkY < gridHeight)
                {
                    int pointIndex = grid[checkX, checkY];
                    if (pointIndex >= 0 && Vector2.Distance(points[pointIndex], point) < minDist)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private Vector2 GetRandomClusterCenter(ResourceSpawnSettings settings)
    {
        int padding = settings.EdgePadding + (int)settings.ClusterRadius;
        Vector2 center;
        int attempts = 0;

        do
        {
            center = new Vector2(
                Random.Range(padding, mapSize.x - padding),
                Random.Range(padding, mapSize.y - padding)
            );
            attempts++;
        }
        while (IsInClearZone(center) && attempts < 50);

        return center;
    }

    // ==================== 노드 스폰 ====================

    private bool TrySpawnNode(ResourceNodeData nodeData, Vector3Int gridPos, ResourceSpawnSettings settings)
    {
        // Clear Zone 체크
        Vector2 localPos = new Vector2(gridPos.x - mapOffset.x, gridPos.z - mapOffset.y);
        if (IsInClearZone(localPos))
            return false;

        // 맵 범위 체크
        if (!IsInMapBounds(gridPos))
            return false;

        // GridData 배치 가능 체크
        if (!environmentGridData.CanPlaceObejctAt(gridPos, nodeData.Size))
            return false;

        // ★ 수정: PlacementSystem과 동일한 방식으로 월드 좌표 계산
        Vector3 worldPos = grid.CellToWorld(gridPos);

        // Grid cellSize 보정 (셀 중심에 배치)
        worldPos += new Vector3(grid.cellSize.x * 0.5f, 0, grid.cellSize.z * 0.5f);

        // 스폰
        GameObject nodeObj = Instantiate(nodeData.Prefab, worldPos, Quaternion.identity);
        nodeObj.transform.SetParent(nodesContainer);
        nodeObj.name = $"{nodeData.Name}_{spawnedNodes.Count}";

        // 스케일 변화
        if (settings.ScaleVariation > 0)
        {
            float scale = 1f + Random.Range(-settings.ScaleVariation, settings.ScaleVariation);
            nodeObj.transform.localScale *= scale;
        }

        // 회전 랜덤화
        if (settings.RandomRotation)
        {
            nodeObj.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        }

        // 컴포넌트 설정
        ResourceNode node = nodeObj.GetComponent<ResourceNode>();
        if (node == null)
        {
            node = nodeObj.AddComponent<ResourceNode>();
        }
        node.Initialize(nodeData, gridPos);

        // GridData에 등록
        environmentGridData.AddObjectAt(gridPos, nodeData.Size, nodeData.ID, spawnedNodes.Count);

        // 추적
        spawnedNodes.Add(node);
        node.OnDepleted += HandleNodeDepleted;

        // ★ 디버그 로그 (문제 확인 후 제거)
        if (logGeneration)
        {
            Debug.Log($"[MapGenerator] Spawned {nodeData.Name} at Grid:{gridPos} -> World:{worldPos}");
        }

        return true;
    }

    // ==================== 유틸리티 ====================

    private Vector3Int LocalToGridPosition(Vector2 localPos)
    {
        return new Vector3Int(
            mapOffset.x + Mathf.RoundToInt(localPos.x),
            0,
            mapOffset.y + Mathf.RoundToInt(localPos.y)
        );
    }

    private bool IsInClearZone(Vector2 localPos)
    {
        Vector2 clearLocal = new Vector2(
            clearZoneCenter.x - mapOffset.x,
            clearZoneCenter.y - mapOffset.y
        );
        return Vector2.Distance(localPos, clearLocal) < clearZoneRadius;
    }

    private bool IsInMapBounds(Vector3Int gridPos)
    {
        return gridPos.x >= mapOffset.x && gridPos.x < mapOffset.x + mapSize.x &&
               gridPos.z >= mapOffset.y && gridPos.z < mapOffset.y + mapSize.y;
    }

    // ==================== 관리 ====================

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

        if (logGeneration)
        {
            Debug.Log("[MapGenerator] 맵 초기화됨");
        }
    }

    private void HandleNodeDepleted(ResourceNode node)
    {
        if (!node.Data.CanRespawn)
        {
            environmentGridData.RemoveObjectAt(node.GridPosition);
            spawnedNodes.Remove(node);
        }
        node.OnDepleted -= HandleNodeDepleted;
    }

    /// <summary>
    /// GridData 반환 (PlacementSystem 연동)
    /// </summary>
    public GridData GetEnvironmentGridData()
    {
        return environmentGridData;
    }

    /// <summary>
    /// 특정 위치에 노드 수동 추가
    /// </summary>
    public ResourceNode SpawnNodeAt(int nodeID, Vector3Int gridPos)
    {
        if (nodeDatabase == null) return null;

        ResourceNodeData nodeData = nodeDatabase.GetNodeByID(nodeID);
        if (nodeData == null || nodeData.Prefab == null) return null;

        if (!IsInMapBounds(gridPos)) return null;
        if (!environmentGridData.CanPlaceObejctAt(gridPos, nodeData.Size)) return null;

        Vector3 worldPos = grid.CellToWorld(gridPos);
        GameObject nodeObj = Instantiate(nodeData.Prefab, worldPos, Quaternion.identity);
        nodeObj.transform.SetParent(nodesContainer);

        ResourceNode node = nodeObj.GetComponent<ResourceNode>();
        if (node == null) node = nodeObj.AddComponent<ResourceNode>();
        node.Initialize(nodeData, gridPos);

        environmentGridData.AddObjectAt(gridPos, nodeData.Size, nodeData.ID, spawnedNodes.Count);
        spawnedNodes.Add(node);
        node.OnDepleted += HandleNodeDepleted;

        return node;
    }

    // ==================== 디버그 ====================

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;

        // 맵 범위
        Gizmos.color = Color.white;
        Vector3 mapCenter = new Vector3(
            mapOffset.x + mapSize.x / 2f,
            0,
            mapOffset.y + mapSize.y / 2f
        );
        Gizmos.DrawWireCube(mapCenter, new Vector3(mapSize.x, 0.1f, mapSize.y));

        // Clear Zone
        Gizmos.color = Color.green;
        Vector3 clearCenter = new Vector3(clearZoneCenter.x, 0, clearZoneCenter.y);
        DrawCircleGizmo(clearCenter, clearZoneRadius);
    }

    private void DrawCircleGizmo(Vector3 center, float radius)
    {
        int segments = 32;
        float angleStep = 360f / segments;

        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * angleStep * Mathf.Deg2Rad;
            float angle2 = (i + 1) * angleStep * Mathf.Deg2Rad;

            Vector3 p1 = center + new Vector3(Mathf.Cos(angle1), 0, Mathf.Sin(angle1)) * radius;
            Vector3 p2 = center + new Vector3(Mathf.Cos(angle2), 0, Mathf.Sin(angle2)) * radius;

            Gizmos.DrawLine(p1, p2);
        }
    }
}