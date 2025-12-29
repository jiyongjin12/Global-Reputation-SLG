using UnityEngine;

public class MapGenerator_DebugTest : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Grid grid;
    [SerializeField] private GridLineSet gridLineSet;
    [SerializeField] private GameObject testPrefab;  // 나무 Prefab 넣기

    [Header("Test Settings")]
    [SerializeField] private int testCellX = 5;
    [SerializeField] private int testCellZ = 5;

    [Header("Debug Markers")]
    [SerializeField] private bool createDebugSpheres = true;

    // 캐시
    private Vector3 gridOrigin;
    private Vector2 gridTotalSize;
    private Vector2 cellSize;
    private float offsetX;
    private float offsetZ;

    private Transform debugContainer;

    private void CacheSettings()
    {
        gridOrigin = grid != null ? grid.transform.position : Vector3.zero;

        if (gridLineSet != null)
        {
            gridTotalSize = gridLineSet.gridTotalSize;
            cellSize = gridLineSet.cellSize;
        }
        else
        {
            gridTotalSize = new Vector2(20f, 20f);
            cellSize = new Vector2(1f, 1f);
        }

        offsetX = -gridTotalSize.x / 2f;
        offsetZ = -gridTotalSize.y / 2f;
    }

    [ContextMenu("1. 설정값 출력")]
    public void LogSettings()
    {
        CacheSettings();

        Debug.Log("========== 설정값 ==========");
        Debug.Log($"Grid.transform.position: {(grid != null ? grid.transform.position.ToString() : "NULL")}");
        Debug.Log($"GridLineSet.gridTotalSize: {gridTotalSize}");
        Debug.Log($"GridLineSet.cellSize: {cellSize}");
        Debug.Log($"계산된 offsetX: {offsetX}");
        Debug.Log($"계산된 offsetZ: {offsetZ}");
    }

    [ContextMenu("2. 테스트 셀에 마커 생성")]
    public void CreateTestMarkers()
    {
        ClearDebug();
        CacheSettings();

        debugContainer = new GameObject("DebugMarkers").transform;
        debugContainer.SetParent(transform);

        int x = testCellX;
        int z = testCellZ;

        Debug.Log($"========== 셀 ({x}, {z}) 좌표 계산 ==========");

        // 1. 셀 코너 (좌하단)
        Vector3 corner = new Vector3(
            gridOrigin.x + offsetX + (x * cellSize.x),
            gridOrigin.y,
            gridOrigin.z + offsetZ + (z * cellSize.y)
        );
        Debug.Log($"[계산] 코너 (좌하단): {corner}");

        // 2. 셀 중앙 (우리가 원하는 위치)
        Vector3 center = corner + new Vector3(cellSize.x * 0.5f, 0, cellSize.y * 0.5f);
        Debug.Log($"[계산] 중앙: {center}");

        // 3. 셀의 4개 코너
        Vector3 cornerBL = corner;  // 좌하단
        Vector3 cornerBR = corner + new Vector3(cellSize.x, 0, 0);  // 우하단
        Vector3 cornerTL = corner + new Vector3(0, 0, cellSize.y);  // 좌상단
        Vector3 cornerTR = corner + new Vector3(cellSize.x, 0, cellSize.y);  // 우상단

        Debug.Log($"[계산] 좌하단: {cornerBL}");
        Debug.Log($"[계산] 우하단: {cornerBR}");
        Debug.Log($"[계산] 좌상단: {cornerTL}");
        Debug.Log($"[계산] 우상단: {cornerTR}");

        if (createDebugSpheres)
        {
            // 코너에 빨간 구
            CreateDebugSphere("Corner_BL", cornerBL, Color.red, 0.15f);
            CreateDebugSphere("Corner_BR", cornerBR, Color.red, 0.15f);
            CreateDebugSphere("Corner_TL", cornerTL, Color.red, 0.15f);
            CreateDebugSphere("Corner_TR", cornerTR, Color.red, 0.15f);

            // 중앙에 초록 구
            CreateDebugSphere("Center", center, Color.green, 0.2f);

            // 셀 경계선 (노란색)
            Debug.DrawLine(cornerBL, cornerBR, Color.yellow, 60f);
            Debug.DrawLine(cornerBR, cornerTR, Color.yellow, 60f);
            Debug.DrawLine(cornerTR, cornerTL, Color.yellow, 60f);
            Debug.DrawLine(cornerTL, cornerBL, Color.yellow, 60f);
        }
    }

    [ContextMenu("3. 테스트 셀 중앙에 Prefab 생성")]
    public void SpawnPrefabAtCenter()
    {
        if (testPrefab == null)
        {
            Debug.LogError("testPrefab을 설정해주세요!");
            return;
        }

        CacheSettings();

        int x = testCellX;
        int z = testCellZ;

        // 셀 중앙 계산
        Vector3 corner = new Vector3(
            gridOrigin.x + offsetX + (x * cellSize.x),
            gridOrigin.y,
            gridOrigin.z + offsetZ + (z * cellSize.y)
        );
        Vector3 center = corner + new Vector3(cellSize.x * 0.5f, 0, cellSize.y * 0.5f);

        Debug.Log($"========== Prefab 생성 ==========");
        Debug.Log($"목표 위치 (셀 중앙): {center}");

        // Prefab 생성
        GameObject obj = Instantiate(testPrefab, center, Quaternion.identity);
        obj.name = $"TestSpawn_Cell_{x}_{z}";

        if (debugContainer == null)
        {
            debugContainer = new GameObject("DebugMarkers").transform;
            debugContainer.SetParent(transform);
        }
        obj.transform.SetParent(debugContainer);

        // 실제 생성된 위치 확인
        Debug.Log($"실제 생성 위치: {obj.transform.position}");

        // Prefab의 Pivot 정보
        MeshRenderer renderer = obj.GetComponentInChildren<MeshRenderer>();
        if (renderer != null)
        {
            Debug.Log($"Mesh Bounds Center: {renderer.bounds.center}");
            Debug.Log($"Mesh Bounds Size: {renderer.bounds.size}");
            Debug.Log($"Mesh Bounds Min: {renderer.bounds.min}");
            Debug.Log($"Mesh Bounds Max: {renderer.bounds.max}");
        }

        // 생성된 오브젝트 위치에 파란 구
        CreateDebugSphere("SpawnedPosition", obj.transform.position, Color.blue, 0.25f);
    }

    [ContextMenu("4. Grid.CellToWorld 비교")]
    public void CompareWithGridCellToWorld()
    {
        if (grid == null)
        {
            Debug.LogError("Grid가 없습니다!");
            return;
        }

        CacheSettings();

        int x = testCellX;
        int z = testCellZ;

        Debug.Log($"========== Grid.CellToWorld 비교 ==========");

        // 우리 계산
        Vector3 ourCorner = new Vector3(
            gridOrigin.x + offsetX + (x * cellSize.x),
            gridOrigin.y,
            gridOrigin.z + offsetZ + (z * cellSize.y)
        );
        Vector3 ourCenter = ourCorner + new Vector3(cellSize.x * 0.5f, 0, cellSize.y * 0.5f);

        // Grid.CellToWorld (다양한 좌표로 테스트)
        Vector3Int gridPos1 = new Vector3Int(x, 0, z);
        Vector3Int gridPos2 = new Vector3Int(x + Mathf.RoundToInt(offsetX), 0, z + Mathf.RoundToInt(offsetZ));

        Vector3 gridCorner1 = grid.CellToWorld(gridPos1);
        Vector3 gridCenter1 = grid.GetCellCenterWorld(gridPos1);

        Vector3 gridCorner2 = grid.CellToWorld(gridPos2);
        Vector3 gridCenter2 = grid.GetCellCenterWorld(gridPos2);

        Debug.Log($"[우리 계산]");
        Debug.Log($"  코너: {ourCorner}");
        Debug.Log($"  중앙: {ourCenter}");

        Debug.Log($"[Grid API - gridPos({x}, 0, {z})]");
        Debug.Log($"  CellToWorld: {gridCorner1}");
        Debug.Log($"  GetCellCenterWorld: {gridCenter1}");

        Debug.Log($"[Grid API - gridPos({gridPos2})]");
        Debug.Log($"  CellToWorld: {gridCorner2}");
        Debug.Log($"  GetCellCenterWorld: {gridCenter2}");

        // 차이 계산
        Debug.Log($"[차이]");
        Debug.Log($"  우리 중앙 vs Grid GetCellCenterWorld(1): {Vector3.Distance(ourCenter, gridCenter1):F4}");
        Debug.Log($"  우리 중앙 vs Grid GetCellCenterWorld(2): {Vector3.Distance(ourCenter, gridCenter2):F4}");
    }

    [ContextMenu("5. 현재 씬의 나무 위치 분석")]
    public void AnalyzeExistingTrees()
    {
        CacheSettings();

        Debug.Log("========== 기존 나무 위치 분석 ==========");

        // ResourceNode 또는 "Tree" 이름을 가진 오브젝트 찾기
        ResourceNode[] nodes = FindObjectsOfType<ResourceNode>();

        if (nodes.Length == 0)
        {
            Debug.Log("ResourceNode를 찾을 수 없습니다.");
            return;
        }

        foreach (var node in nodes)
        {
            Vector3 pos = node.transform.position;

            // 이 위치가 어느 셀에 해당하는지 역계산
            float cellXFloat = (pos.x - gridOrigin.x - offsetX) / cellSize.x;
            float cellZFloat = (pos.z - gridOrigin.z - offsetZ) / cellSize.y;

            int cellX = Mathf.FloorToInt(cellXFloat);
            int cellZ = Mathf.FloorToInt(cellZFloat);

            // 셀 내에서의 오프셋 (0~1 범위)
            float inCellOffsetX = cellXFloat - cellX;
            float inCellOffsetZ = cellZFloat - cellZ;

            // 셀 중앙이면 0.5, 0.5 여야 함
            Debug.Log($"{node.name}: pos{pos} → Cell({cellX},{cellZ}), 셀 내 오프셋: ({inCellOffsetX:F3}, {inCellOffsetZ:F3})");

            // 중앙(0.5)에서 얼마나 벗어났는지
            float deviationX = Mathf.Abs(inCellOffsetX - 0.5f);
            float deviationZ = Mathf.Abs(inCellOffsetZ - 0.5f);

            if (deviationX > 0.1f || deviationZ > 0.1f)
            {
                Debug.LogWarning($" 중앙에서 벗어남! 편차: ({deviationX:F3}, {deviationZ:F3})");
            }
        }
    }

    [ContextMenu("Clear Debug Objects")]
    public void ClearDebug()
    {
        if (debugContainer != null)
        {
            DestroyImmediate(debugContainer.gameObject);
        }

        Transform existing = transform.Find("DebugMarkers");
        if (existing != null)
        {
            DestroyImmediate(existing.gameObject);
        }
    }

    private void CreateDebugSphere(string name, Vector3 position, Color color, float radius)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * radius * 2f;
        sphere.transform.SetParent(debugContainer);

        // Collider 제거
        Collider col = sphere.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        // 색상 설정
        Renderer rend = sphere.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            rend.material = mat;
        }
    }

    private void OnDrawGizmos()
    {
        if (grid == null || gridLineSet == null) return;

        CacheSettings();

        int x = testCellX;
        int z = testCellZ;

        // 테스트 셀 시각화
        Vector3 corner = new Vector3(
            gridOrigin.x + offsetX + (x * cellSize.x),
            gridOrigin.y + 0.01f,
            gridOrigin.z + offsetZ + (z * cellSize.y)
        );

        Vector3 center = corner + new Vector3(cellSize.x * 0.5f, 0, cellSize.y * 0.5f);

        // 셀 영역 (노란색)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(center, new Vector3(cellSize.x, 0.02f, cellSize.y));

        // 중앙점 (초록)
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(center, 0.1f);

        // 코너들 (빨강)
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(corner, 0.08f);
        Gizmos.DrawSphere(corner + new Vector3(cellSize.x, 0, 0), 0.08f);
        Gizmos.DrawSphere(corner + new Vector3(0, 0, cellSize.y), 0.08f);
        Gizmos.DrawSphere(corner + new Vector3(cellSize.x, 0, cellSize.y), 0.08f);
    }
}