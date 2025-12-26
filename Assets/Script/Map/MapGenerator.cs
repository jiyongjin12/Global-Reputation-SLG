using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자연환경 스폰 설정
/// </summary>
[System.Serializable]
public class ResourceSpawnSettings
{
    public ResourceNodeData NodeData;
    [Range(0f, 1f)] public float SpawnChance = 0.1f;
    public int MinCount = 5;
    public int MaxCount = 20;

    [Header("Clustering")]
    public bool UseClustering = false;
    public int ClusterCount = 3;
    public int NodesPerCluster = 5;
    public float ClusterRadius = 5f;
}

/// <summary>
/// 맵 생성기 - 자연환경 자동 배치
/// </summary>
public class MapGenerator : MonoBehaviour
{
    [Header("Grid Reference")]
    [SerializeField] private Grid grid;
    [SerializeField] private GridData environmentGridData; // 자연환경용 GridData

    [Header("Map Settings")]
    [SerializeField] private Vector2Int mapSize = new Vector2Int(50, 50);
    [SerializeField] private Vector2Int mapOffset = new Vector2Int(-25, -25); // 맵 시작 오프셋

    [Header("Clear Zone")]
    [SerializeField] private Vector2Int clearZoneCenter = Vector2Int.zero;
    [SerializeField] private int clearZoneRadius = 5; // 시작 지점 주변 빈 공간

    [Header("Spawn Settings")]
    [SerializeField] private List<ResourceSpawnSettings> spawnSettings = new List<ResourceSpawnSettings>();

    [Header("Database")]
    [SerializeField] private ResourceNodeDatabaseSO nodeDatabase;

    // 생성된 노드 추적
    private List<ResourceNode> spawnedNodes = new List<ResourceNode>();

    private void Awake()
    {
        if (environmentGridData == null)
        {
            environmentGridData = new GridData();
        }
    }

    /// <summary>
    /// 맵 생성 시작
    /// </summary>
    public void GenerateMap()
    {
        ClearMap();

        foreach (var settings in spawnSettings)
        {
            if (settings.UseClustering)
            {
                SpawnClustered(settings);
            }
            else
            {
                SpawnRandom(settings);
            }
        }

        Debug.Log($"[MapGenerator] Generated {spawnedNodes.Count} resource nodes");
    }

    /// <summary>
    /// 랜덤 분포로 스폰
    /// </summary>
    private void SpawnRandom(ResourceSpawnSettings settings)
    {
        int targetCount = Random.Range(settings.MinCount, settings.MaxCount + 1);
        int spawnedCount = 0;
        int maxAttempts = targetCount * 10;
        int attempts = 0;

        while (spawnedCount < targetCount && attempts < maxAttempts)
        {
            attempts++;

            // 랜덤 위치
            int x = Random.Range(mapOffset.x, mapOffset.x + mapSize.x);
            int z = Random.Range(mapOffset.y, mapOffset.y + mapSize.y);
            Vector3Int gridPos = new Vector3Int(x, 0, z);

            if (TrySpawnNode(settings.NodeData, gridPos))
            {
                spawnedCount++;
            }
        }
    }

    /// <summary>
    /// 클러스터 방식으로 스폰 (숲, 광맥 등)
    /// </summary>
    private void SpawnClustered(ResourceSpawnSettings settings)
    {
        for (int cluster = 0; cluster < settings.ClusterCount; cluster++)
        {
            // 클러스터 중심점
            int cx = Random.Range(mapOffset.x + 5, mapOffset.x + mapSize.x - 5);
            int cz = Random.Range(mapOffset.y + 5, mapOffset.y + mapSize.y - 5);

            // Clear Zone 체크
            if (IsInClearZone(new Vector2Int(cx, cz)))
            {
                continue;
            }

            // 클러스터 내 노드 스폰
            for (int i = 0; i < settings.NodesPerCluster; i++)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = Random.Range(0f, settings.ClusterRadius);

                int x = cx + Mathf.RoundToInt(Mathf.Cos(angle) * distance);
                int z = cz + Mathf.RoundToInt(Mathf.Sin(angle) * distance);

                Vector3Int gridPos = new Vector3Int(x, 0, z);
                TrySpawnNode(settings.NodeData, gridPos);
            }
        }
    }

    /// <summary>
    /// 노드 스폰 시도
    /// </summary>
    private bool TrySpawnNode(ResourceNodeData nodeData, Vector3Int gridPos)
    {
        // Clear Zone 체크
        if (IsInClearZone(new Vector2Int(gridPos.x, gridPos.z)))
            return false;

        // 맵 범위 체크
        if (!IsInMapBounds(gridPos))
            return false;

        // 배치 가능 여부 체크
        if (!environmentGridData.CanPlaceObejctAt(gridPos, nodeData.Size))
            return false;

        // 스폰
        Vector3 worldPos = grid.CellToWorld(gridPos);
        GameObject nodeObj = Instantiate(nodeData.Prefab, worldPos, Quaternion.identity);
        nodeObj.transform.parent = transform; // 정리용

        // 컴포넌트 설정
        ResourceNode node = nodeObj.GetComponent<ResourceNode>();
        if (node == null)
        {
            node = nodeObj.AddComponent<ResourceNode>();
        }
        node.Initialize(nodeData, gridPos);

        // GridData에 등록
        environmentGridData.AddObjectAt(gridPos, nodeData.Size, nodeData.ID, spawnedNodes.Count);

        // 추적 리스트에 추가
        spawnedNodes.Add(node);

        // 파괴 시 GridData에서 제거
        node.OnDepleted += HandleNodeDepleted;

        return true;
    }

    /// <summary>
    /// Clear Zone 내부인지 확인
    /// </summary>
    private bool IsInClearZone(Vector2Int pos)
    {
        float distance = Vector2Int.Distance(pos, clearZoneCenter);
        return distance < clearZoneRadius;
    }

    /// <summary>
    /// 맵 범위 내인지 확인
    /// </summary>
    private bool IsInMapBounds(Vector3Int pos)
    {
        return pos.x >= mapOffset.x && pos.x < mapOffset.x + mapSize.x &&
               pos.z >= mapOffset.y && pos.z < mapOffset.y + mapSize.y;
    }

    /// <summary>
    /// 맵 초기화
    /// </summary>
    public void ClearMap()
    {
        foreach (var node in spawnedNodes)
        {
            if (node != null)
            {
                Destroy(node.gameObject);
            }
        }
        spawnedNodes.Clear();
        environmentGridData = new GridData();
    }

    /// <summary>
    /// 노드 파괴 처리
    /// </summary>
    private void HandleNodeDepleted(ResourceNode node)
    {
        // 리스폰하지 않는 노드는 GridData에서 제거
        if (!node.Data.CanRespawn)
        {
            environmentGridData.RemoveObjectAt(node.GridPosition);
            spawnedNodes.Remove(node);
        }
        node.OnDepleted -= HandleNodeDepleted;
    }

    /// <summary>
    /// 에디터용: 맵 재생성
    /// </summary>
    [ContextMenu("Regenerate Map")]
    public void RegenerateMap()
    {
        GenerateMap();
    }

    /// <summary>
    /// GridData 반환 (PlacementSystem 연동용)
    /// </summary>
    public GridData GetEnvironmentGridData()
    {
        return environmentGridData;
    }
}