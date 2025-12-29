using System;
using System.Collections.Generic;
using UnityEngine;

public class GridDataManager : MonoBehaviour
{
    public static GridDataManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Grid grid;
    [SerializeField] private GridLineSet gridLineSet;

    // 내부 데이터
    private GridData gridData;
    private Dictionary<Vector3Int, PlacedObjectInfo> placedObjects = new();
    private int objectIndexCounter = 0;

    // 캐시
    private int gridCellCountX;
    private int gridCellCountZ;
    private float offsetX;
    private float offsetZ;

    // 이벤트
    public event Action<Vector3Int, PlacedObjectInfo> OnObjectPlaced;
    public event Action<Vector3Int> OnObjectRemoved;

    // Properties
    public Grid Grid => grid;
    public int CellCountX => gridCellCountX;
    public int CellCountZ => gridCellCountZ;

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

    private void CacheGridSettings()
    {
        if (gridLineSet != null)
        {
            gridCellCountX = Mathf.RoundToInt(gridLineSet.gridTotalSize.x / gridLineSet.cellSize.x);
            gridCellCountZ = Mathf.RoundToInt(gridLineSet.gridTotalSize.y / gridLineSet.cellSize.y);
            offsetX = -gridLineSet.gridTotalSize.x / 2f;
            offsetZ = -gridLineSet.gridTotalSize.y / 2f;
        }
        else
        {
            gridCellCountX = 20;
            gridCellCountZ = 20;
            offsetX = -10f;
            offsetZ = -10f;
        }

        Debug.Log($"[GridDataManager] Grid: {gridCellCountX}x{gridCellCountZ}, offset: ({offsetX}, {offsetZ})");
    }

    // ==================== 좌표 변환 (기존 유지) ====================

    /// <summary>
    /// Grid 좌표 → 셀 인덱스
    /// </summary>
    public (int cellX, int cellZ) GridPositionToCellIndex(Vector3Int gridPos)
    {
        int cellX = gridPos.x - Mathf.RoundToInt(offsetX);
        int cellZ = gridPos.z - Mathf.RoundToInt(offsetZ);
        return (cellX, cellZ);
    }

    /// <summary>
    /// 셀 인덱스 → Grid 좌표
    /// </summary>
    public Vector3Int CellIndexToGridPosition(int cellX, int cellZ)
    {
        int gridX = cellX + Mathf.RoundToInt(offsetX);
        int gridZ = cellZ + Mathf.RoundToInt(offsetZ);
        return new Vector3Int(gridX, 0, gridZ);
    }

    // ==================== ★ 범위 체크 (핵심 수정!) ★ ====================

    /// <summary>
    /// 셀 인덱스가 맵 범위 내인지 체크
    /// </summary>
    public bool IsCellInBounds(int cellX, int cellZ)
    {
        return cellX >= 0 && cellX < gridCellCountX &&
               cellZ >= 0 && cellZ < gridCellCountZ;
    }

    /// <summary>
    /// ★ 배치 가능 여부 (Grid 좌표 기준) - 모든 셀 범위 체크!
    /// </summary>
    public bool CanPlaceAt(Vector3Int gridPosition, Vector2Int size)
    {
        var (baseCellX, baseCellZ) = GridPositionToCellIndex(gridPosition);

        // ★ 모든 점유할 셀이 맵 범위 내인지 체크
        for (int x = 0; x < size.x; x++)
        {
            for (int z = 0; z < size.y; z++)
            {
                int checkX = baseCellX + x;
                int checkZ = baseCellZ + z;

                if (!IsCellInBounds(checkX, checkZ))
                {
                    Debug.Log($"[GridDataManager] 범위 초과: Cell({checkX},{checkZ}) - 맵: {gridCellCountX}x{gridCellCountZ}");
                    return false;
                }
            }
        }

        // GridData 점유 체크
        return gridData.CanPlaceObejctAt(gridPosition, size);
    }

    /// <summary>
    /// 배치 가능 여부 (셀 인덱스 기준)
    /// </summary>
    public bool CanPlaceAtCell(int cellX, int cellZ, Vector2Int size)
    {
        Vector3Int gridPos = CellIndexToGridPosition(cellX, cellZ);
        return CanPlaceAt(gridPos, size);
    }

    // ==================== 배치/제거 (기존 유지) ====================

    public bool PlaceObject(Vector3Int gridPosition, Vector2Int size, int id, PlacedObjectType type, GameObject obj = null)
    {
        if (!CanPlaceAt(gridPosition, size))
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

    public bool PlaceObjectAtCell(int cellX, int cellZ, Vector2Int size, int id, PlacedObjectType type, GameObject obj = null)
    {
        Vector3Int gridPos = CellIndexToGridPosition(cellX, cellZ);
        return PlaceObject(gridPos, size, id, type, obj);
    }

    public bool RemoveObject(Vector3Int gridPosition)
    {
        if (!placedObjects.TryGetValue(gridPosition, out var info))
            return false;

        gridData.RemoveObjectAt(info.OriginPosition);

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

    public PlacedObjectInfo GetObjectAt(Vector3Int gridPosition)
    {
        return placedObjects.TryGetValue(gridPosition, out var info) ? info : null;
    }

    public void ClearAll()
    {
        gridData = new GridData();
        placedObjects.Clear();
        objectIndexCounter = 0;
    }

    public GridData GetRawGridData() => gridData;
}

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