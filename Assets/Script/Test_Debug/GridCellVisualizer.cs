using UnityEngine;

public class GridCellVisualizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Grid grid;
    [SerializeField] private GridLineSet gridLineSet;  // ★ GridLineSet 참조 추가

    [Header("Debug Objects")]
    [SerializeField] private bool showGizmoSpheres = true;
    [SerializeField] private float sphereRadius = 0.1f;
    [SerializeField] private Color cornerColor = Color.red;
    [SerializeField] private Color centerColor = Color.green;

    [Header("Info (Read Only)")]
    [SerializeField] private Vector2 actualGridSize;
    [SerializeField] private Vector2 actualCellSize;
    [SerializeField] private int cellCountX;
    [SerializeField] private int cellCountZ;

    private Transform container;

    [ContextMenu("Generate Cell Markers")]
    public void GenerateCellMarkers()
    {
        ClearMarkers();

        if (grid == null)
        {
            Debug.LogError("[GridCellVisualizer] Grid가 설정되지 않았습니다!");
            return;
        }

        // GridLineSet에서 크기 정보 가져오기
        if (gridLineSet != null)
        {
            actualGridSize = gridLineSet.gridTotalSize;
            actualCellSize = gridLineSet.cellSize;
        }
        else
        {
            // GridLineSet 없으면 Grid 컴포넌트에서 가져오기
            actualGridSize = new Vector2(10f, 10f);  // 기본값
            actualCellSize = new Vector2(grid.cellSize.x, grid.cellSize.z);
        }

        // 셀 개수 계산
        cellCountX = Mathf.RoundToInt(actualGridSize.x / actualCellSize.x);
        cellCountZ = Mathf.RoundToInt(actualGridSize.y / actualCellSize.y);

        Debug.Log($"[GridCellVisualizer] === 설정 정보 ===");
        Debug.Log($"  GridLineSet gridTotalSize: {actualGridSize}");
        Debug.Log($"  GridLineSet cellSize: {actualCellSize}");
        Debug.Log($"  Grid.cellSize: {grid.cellSize}");
        Debug.Log($"  Grid.cellSwizzle: {grid.cellSwizzle}");
        Debug.Log($"  Grid.transform.position: {grid.transform.position}");
        Debug.Log($"  계산된 셀 개수: {cellCountX} x {cellCountZ}");

        container = new GameObject("GridCellMarkers").transform;
        container.SetParent(transform);

        // Grid의 시작 위치 (좌하단)
        Vector3 gridOrigin = grid.transform.position;

        // 그리드 중앙이 원점이라면 오프셋 계산
        float offsetX = -actualGridSize.x / 2f;
        float offsetZ = -actualGridSize.y / 2f;

        Debug.Log($"  Grid Origin: {gridOrigin}");
        Debug.Log($"  Offset: ({offsetX}, {offsetZ})");

        for (int x = 0; x < cellCountX; x++)
        {
            for (int z = 0; z < cellCountZ; z++)
            {
                // 방법 1: GridLineSet 기준 수동 계산
                Vector3 cornerPosManual = new Vector3(
                    gridOrigin.x + offsetX + (x * actualCellSize.x),
                    gridOrigin.y,
                    gridOrigin.z + offsetZ + (z * actualCellSize.y)
                );

                Vector3 centerPosManual = cornerPosManual + new Vector3(
                    actualCellSize.x * 0.5f,
                    0,
                    actualCellSize.y * 0.5f
                );

                // 방법 2: Grid 컴포넌트의 CellToWorld
                Vector3Int gridPos = new Vector3Int(
                    x + Mathf.RoundToInt(offsetX),
                    0,
                    z + Mathf.RoundToInt(offsetZ)
                );
                Vector3 cornerPosGrid = grid.CellToWorld(gridPos);
                Vector3 centerPosGrid = grid.GetCellCenterWorld(gridPos);

                // 중앙 마커 생성 (수동 계산 기준)
                GameObject centerMarker = new GameObject($"Center_{x}_{z}");
                centerMarker.transform.position = centerPosManual;
                centerMarker.transform.SetParent(container);

                // 첫 번째 셀 상세 로그
                if (x == 0 && z == 0)
                {
                    Debug.Log($"[GridCellVisualizer] === 첫 번째 셀 (0, 0) ===");
                    Debug.Log($"  [수동] Corner: {cornerPosManual}");
                    Debug.Log($"  [수동] Center: {centerPosManual}");
                    Debug.Log($"  [Grid] GridPos: {gridPos}");
                    Debug.Log($"  [Grid] CellToWorld: {cornerPosGrid}");
                    Debug.Log($"  [Grid] GetCellCenterWorld: {centerPosGrid}");
                }

                // 중앙 셀도 로그
                if (x == cellCountX / 2 && z == cellCountZ / 2)
                {
                    Debug.Log($"[GridCellVisualizer] === 중앙 셀 ({x}, {z}) ===");
                    Debug.Log($"  [수동] Corner: {cornerPosManual}");
                    Debug.Log($"  [수동] Center: {centerPosManual}");
                }
            }
        }

        Debug.Log($"[GridCellVisualizer] {cellCountX * cellCountZ}개 셀 마커 생성 완료");
    }

    [ContextMenu("Clear Markers")]
    public void ClearMarkers()
    {
        if (container != null)
        {
            DestroyImmediate(container.gameObject);
        }

        Transform existing = transform.Find("GridCellMarkers");
        if (existing != null)
        {
            DestroyImmediate(existing.gameObject);
        }
    }

    [ContextMenu("Log Grid Info")]
    public void LogGridInfo()
    {
        if (grid == null)
        {
            Debug.LogError("Grid가 없습니다!");
            return;
        }

        Debug.Log("=== Grid Component 정보 ===");
        Debug.Log($"cellSize: {grid.cellSize}");
        Debug.Log($"cellGap: {grid.cellGap}");
        Debug.Log($"cellLayout: {grid.cellLayout}");
        Debug.Log($"cellSwizzle: {grid.cellSwizzle}");
        Debug.Log($"transform.position: {grid.transform.position}");

        if (gridLineSet != null)
        {
            Debug.Log("=== GridLineSet 정보 ===");
            Debug.Log($"gridTotalSize: {gridLineSet.gridTotalSize}");
            Debug.Log($"cellSize: {gridLineSet.cellSize}");
        }

        // 테스트: 몇 가지 좌표 변환
        Debug.Log("=== 좌표 변환 테스트 ===");

        Vector3Int[] testPositions = {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(-1, 0, -1),
            new Vector3Int(5, 0, 5),
        };

        foreach (var pos in testPositions)
        {
            Vector3 corner = grid.CellToWorld(pos);
            Vector3 center = grid.GetCellCenterWorld(pos);
            Debug.Log($"GridPos {pos} → Corner: {corner}, Center: {center}");
        }
    }

    private void OnDrawGizmos()
    {
        if (!showGizmoSpheres || grid == null) return;

        // GridLineSet 크기 사용
        Vector2 gridSize = gridLineSet != null ? gridLineSet.gridTotalSize : new Vector2(10f, 10f);
        Vector2 cellSize = gridLineSet != null ? gridLineSet.cellSize : new Vector2(1f, 1f);

        int countX = Mathf.RoundToInt(gridSize.x / cellSize.x);
        int countZ = Mathf.RoundToInt(gridSize.y / cellSize.y);

        Vector3 gridOrigin = grid.transform.position;
        float offsetX = -gridSize.x / 2f;
        float offsetZ = -gridSize.y / 2f;

        for (int x = 0; x < countX; x++)
        {
            for (int z = 0; z < countZ; z++)
            {
                // 코너
                Vector3 cornerPos = new Vector3(
                    gridOrigin.x + offsetX + (x * cellSize.x),
                    gridOrigin.y,
                    gridOrigin.z + offsetZ + (z * cellSize.y)
                );
                Gizmos.color = cornerColor;
                Gizmos.DrawSphere(cornerPos, sphereRadius);

                // 중앙
                Vector3 centerPos = cornerPos + new Vector3(cellSize.x * 0.5f, 0, cellSize.y * 0.5f);
                Gizmos.color = centerColor;
                Gizmos.DrawSphere(centerPos, sphereRadius * 0.7f);
            }
        }

        // 그리드 경계선
        Gizmos.color = Color.yellow;
        Vector3 gridCenter = gridOrigin;
        Gizmos.DrawWireCube(gridCenter, new Vector3(gridSize.x, 0.1f, gridSize.y));
    }
}
