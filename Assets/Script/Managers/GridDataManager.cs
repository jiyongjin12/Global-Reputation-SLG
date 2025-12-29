using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 그리드 데이터 통합 관리자 (Single Source of Truth)
/// GridLineSet 설정 기반 좌표 계산
/// </summary>
public class GridDataManager : MonoBehaviour
{
    public static GridDataManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Grid grid;
    [SerializeField] private GridLineSet gridLineSet;

    // 내부 GridData
    private GridData gridData;

    // 배치된 오브젝트 추적
    private Dictionary<Vector3Int, PlacedObjectInfo> placedObjects = new();
    private int objectIndexCounter = 0;

    // 캐시된 설정값
    private Vector2 gridTotalSize;
    private Vector2 cellSize;
    private Vector3 gridOrigin;
    private float offsetX;
    private float offsetZ;

    // 이벤트
    public event Action<Vector3Int, PlacedObjectInfo> OnObjectPlaced;
    public event Action<Vector3Int> OnObjectRemoved;

    // Properties
    public Grid Grid => grid;
    public Vector2 GridTotalSize => gridTotalSize;
    public Vector2 CellSize => cellSize;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            gridData = new GridData();
            CacheGridSettings();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// GridLineSet에서 설정값 캐시
    /// </summary>
    private void CacheGridSettings()
    {
        if (gridLineSet != null)
        {
            gridTotalSize = gridLineSet.gridTotalSize;
            cellSize = gridLineSet.cellSize;
        }
        else if (grid != null)
        {
            gridTotalSize = new Vector2(20f, 20f);  // 기본값
            cellSize = new Vector2(grid.cellSize.x, grid.cellSize.z);
        }
        else
        {
            gridTotalSize = new Vector2(20f, 20f);
            cellSize = new Vector2(1f, 1f);
        }

        gridOrigin = grid != null ? grid.transform.position : Vector3.zero;

        // 그리드 중앙 정렬 오프셋
        offsetX = -gridTotalSize.x / 2f;
        offsetZ = -gridTotalSize.y / 2f;

        Debug.Log($"[GridDataManager] 설정 캐시됨:");
        Debug.Log($"  gridTotalSize: {gridTotalSize}");
        Debug.Log($"  cellSize: {cellSize}");
        Debug.Log($"  gridOrigin: {gridOrigin}");
        Debug.Log($"  offset: ({offsetX}, {offsetZ})");
    }

    // ==================== 좌표 변환 (핵심!) ====================

    /// <summary>
    /// 셀 인덱스 → 월드 좌표 (셀 중앙)
    /// </summary>
    public Vector3 CellIndexToWorldCenter(int cellX, int cellZ)
    {
        float worldX = gridOrigin.x + offsetX + (cellX * cellSize.x) + (cellSize.x * 0.5f);
        float worldZ = gridOrigin.z + offsetZ + (cellZ * cellSize.y) + (cellSize.y * 0.5f);
        return new Vector3(worldX, gridOrigin.y, worldZ);
    }

    /// <summary>
    /// 셀 인덱스 → 월드 좌표 (셀 코너/좌하단)
    /// </summary>
    public Vector3 CellIndexToWorldCorner(int cellX, int cellZ)
    {
        float worldX = gridOrigin.x + offsetX + (cellX * cellSize.x);
        float worldZ = gridOrigin.z + offsetZ + (cellZ * cellSize.y);
        return new Vector3(worldX, gridOrigin.y, worldZ);
    }

    /// <summary>
    /// Grid 좌표 (Vector3Int) → 월드 좌표 (셀 중앙)
    /// </summary>
    public Vector3 GridToWorldPosition(Vector3Int gridPosition)
    {
        // gridPosition은 오프셋이 적용된 좌표일 수 있음
        // 예: (-5, 0, -5) 같은 음수 좌표

        // 셀 인덱스로 변환 (오프셋 고려)
        int cellX = gridPosition.x - Mathf.RoundToInt(offsetX);
        int cellZ = gridPosition.z - Mathf.RoundToInt(offsetZ);

        return CellIndexToWorldCenter(cellX, cellZ);
    }

    /// <summary>
    /// 월드 좌표 → Grid 좌표
    /// </summary>
    public Vector3Int WorldToGridPosition(Vector3 worldPosition)
    {
        // 셀 인덱스 계산
        int cellX = Mathf.FloorToInt((worldPosition.x - gridOrigin.x - offsetX) / cellSize.x);
        int cellZ = Mathf.FloorToInt((worldPosition.z - gridOrigin.z - offsetZ) / cellSize.y);

        // Grid 좌표로 변환 (오프셋 적용)
        int gridX = cellX + Mathf.RoundToInt(offsetX);
        int gridZ = cellZ + Mathf.RoundToInt(offsetZ);

        return new Vector3Int(gridX, 0, gridZ);
    }

    /// <summary>
    /// 셀 인덱스가 유효한 범위인지 체크
    /// </summary>
    public bool IsValidCellIndex(int cellX, int cellZ)
    {
        int maxX = Mathf.RoundToInt(gridTotalSize.x / cellSize.x);
        int maxZ = Mathf.RoundToInt(gridTotalSize.y / cellSize.y);
        return cellX >= 0 && cellX < maxX && cellZ >= 0 && cellZ < maxZ;
    }

    // ==================== 배치 API ====================

    /// <summary>
    /// 배치 가능 여부 (셀 인덱스 기준)
    /// </summary>
    public bool CanPlaceAtCell(int cellX, int cellZ, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int z = 0; z < size.y; z++)
            {
                if (!IsValidCellIndex(cellX + x, cellZ + z))
                    return false;
            }
        }

        Vector3Int gridPos = CellIndexToGridPosition(cellX, cellZ);
        return gridData.CanPlaceObejctAt(gridPos, size);
    }

    /// <summary>
    /// 배치 가능 여부 (Grid 좌표 기준)
    /// </summary>
    public bool CanPlaceAt(Vector3Int gridPosition, Vector2Int size)
    {
        return gridData.CanPlaceObejctAt(gridPosition, size);
    }

    /// <summary>
    /// 오브젝트 배치 (셀 인덱스 기준)
    /// </summary>
    public bool PlaceObjectAtCell(int cellX, int cellZ, Vector2Int size, int id, PlacedObjectType type, GameObject obj = null)
    {
        if (!CanPlaceAtCell(cellX, cellZ, size))
            return false;

        Vector3Int gridPos = CellIndexToGridPosition(cellX, cellZ);
        return PlaceObject(gridPos, size, id, type, obj);
    }

    /// <summary>
    /// 오브젝트 배치 (Grid 좌표 기준)
    /// </summary>
    public bool PlaceObject(Vector3Int gridPosition, Vector2Int size, int id, PlacedObjectType type, GameObject obj = null)
    {
        if (!gridData.CanPlaceObejctAt(gridPosition, size))
            return false;

        int index = objectIndexCounter++;
        gridData.AddObjectAt(gridPosition, size, id, index);

        var info = new PlacedObjectInfo(id, gridPosition, size, type, obj, index);

        for (int x = 0; x < size.x; x++)
        {
            for (int z = 0; z < size.y; z++)
            {
                Vector3Int cellPos = gridPosition + new Vector3Int(x, 0, z);
                placedObjects[cellPos] = info;
            }
        }

        OnObjectPlaced?.Invoke(gridPosition, info);
        return true;
    }

    /// <summary>
    /// 오브젝트 제거
    /// </summary>
    public bool RemoveObject(Vector3Int gridPosition)
    {
        if (!placedObjects.TryGetValue(gridPosition, out var info))
            return false;

        gridData.RemoveObjectAt(gridPosition);

        for (int x = 0; x < info.Size.x; x++)
        {
            for (int z = 0; z < info.Size.y; z++)
            {
                Vector3Int cellPos = info.OriginPosition + new Vector3Int(x, 0, z);
                placedObjects.Remove(cellPos);
            }
        }

        OnObjectRemoved?.Invoke(gridPosition);
        return true;
    }

    /// <summary>
    /// 특정 위치 오브젝트 정보
    /// </summary>
    public PlacedObjectInfo GetObjectAt(Vector3Int gridPosition)
    {
        return placedObjects.TryGetValue(gridPosition, out var info) ? info : null;
    }

    // ==================== 유틸리티 ====================

    /// <summary>
    /// 셀 인덱스 → Grid 좌표
    /// </summary>
    public Vector3Int CellIndexToGridPosition(int cellX, int cellZ)
    {
        int gridX = cellX + Mathf.RoundToInt(offsetX);
        int gridZ = cellZ + Mathf.RoundToInt(offsetZ);
        return new Vector3Int(gridX, 0, gridZ);
    }

    /// <summary>
    /// Grid 좌표 → 셀 인덱스
    /// </summary>
    public (int cellX, int cellZ) GridPositionToCellIndex(Vector3Int gridPosition)
    {
        int cellX = gridPosition.x - Mathf.RoundToInt(offsetX);
        int cellZ = gridPosition.z - Mathf.RoundToInt(offsetZ);
        return (cellX, cellZ);
    }

    /// <summary>
    /// 전체 초기화
    /// </summary>
    public void ClearAll()
    {
        gridData = new GridData();
        placedObjects.Clear();
        objectIndexCounter = 0;
    }

    /// <summary>
    /// 기존 GridData 직접 접근 (호환성용)
    /// </summary>
    public GridData GetRawGridData() => gridData;

    /// <summary>
    /// 그리드 정보 로그
    /// </summary>
    [ContextMenu("Log Grid Info")]
    public void LogGridInfo()
    {
        Debug.Log($"=== GridDataManager Info ===");
        Debug.Log($"gridTotalSize: {gridTotalSize}");
        Debug.Log($"cellSize: {cellSize}");
        Debug.Log($"gridOrigin: {gridOrigin}");
        Debug.Log($"offset: ({offsetX}, {offsetZ})");
        Debug.Log($"placedObjects count: {placedObjects.Count}");

        // 테스트 좌표 변환
        Debug.Log($"=== 좌표 변환 테스트 ===");
        for (int i = 0; i < 3; i++)
        {
            Vector3 center = CellIndexToWorldCenter(i, i);
            Vector3 corner = CellIndexToWorldCorner(i, i);
            Vector3Int gridPos = CellIndexToGridPosition(i, i);
            Debug.Log($"Cell({i},{i}) → Corner: {corner}, Center: {center}, GridPos: {gridPos}");
        }
    }
}

/// <summary>
/// 배치된 오브젝트 정보
/// </summary>
[Serializable]
public class PlacedObjectInfo
{
    public int ID;
    public int Index;
    public Vector3Int OriginPosition;
    public Vector2Int Size;
    public PlacedObjectType Type;
    public GameObject GameObject;

    public PlacedObjectInfo(int id, Vector3Int origin, Vector2Int size, PlacedObjectType type, GameObject obj, int index)
    {
        ID = id;
        OriginPosition = origin;
        Size = size;
        Type = type;
        GameObject = obj;
        Index = index;
    }
}

public enum PlacedObjectType
{
    Building,
    ResourceNode,
    Decoration,
    Floor
}