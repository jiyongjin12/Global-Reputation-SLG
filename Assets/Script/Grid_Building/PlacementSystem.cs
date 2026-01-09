using System;
using System.Collections.Generic;
using UnityEngine;

public class PlacementSystem : MonoBehaviour
{
    [SerializeField]
    private InputManager inputManager;
    [SerializeField]
    private Grid grid;

    [SerializeField]
    private ObjectsDatabaseSO database;

    [SerializeField]
    private GameObject gridVisualization;

    private GridData floorData, furnitureData;

    [SerializeField]
    private MapGenerator mapGenerator;

    [SerializeField]
    private PreviewSystem preview;

    private Vector3Int lastDetectedPosition = Vector3Int.zero;

    [SerializeField]
    private ObjectPlacer objectPlacer;

    IBuildingState buildingState;

    // 배치된 건물 추적 (GridPosition -> GameObject)
    private Dictionary<Vector3Int, GameObject> placedBuildings = new Dictionary<Vector3Int, GameObject>();

    // ★ 현재 배치 중인 오브젝트 크기 (중앙 보정용)
    private Vector2Int currentPlacementSize = Vector2Int.one;

    // ★ 현재 모드 (외부에서 체크용)
    public bool IsPlacementActive => buildingState != null;
    public bool IsMovingMode { get; private set; } = false;
    public bool IsRemovingMode { get; private set; } = false;

    // 이벤트
    public event Action<Building> OnBuildingPlaced;
    public event Action<Building> OnBuildingMoved;
    public event Action<GameObject> OnBuildingRemoved;
    public event Action<string> OnPlacementFailed;
    public event Action OnPlacementStarted;
    public event Action OnPlacementEnded;

    private void Start()
    {
        gridVisualization.SetActive(false);
        floorData = new();
        furnitureData = new();

        Debug.Log("[PlacementSystem] Start 완료");
    }

    public void StartPlacement(int ID)
    {
        StopPlacement();

        ObjectData data = database.GetObjectByID(ID);
        if (data == null)
        {
            Debug.LogError($"Object with ID {ID} not found!");
            return;
        }

        // 자원 체크
        if (data.ConstructionCosts != null && data.ConstructionCosts.Length > 0)
        {
            if (ResourceManager.Instance != null && !ResourceManager.Instance.CanAfford(data.ConstructionCosts))
            {
                OnPlacementFailed?.Invoke("자원이 부족합니다!");
                Debug.Log("자원이 부족합니다!");
                return;
            }
        }

        gridVisualization.SetActive(true);

        // ★ 현재 배치 중인 오브젝트 크기 저장
        currentPlacementSize = data.Size;

        // GridDataManager 사용
        GridData envData = null;
        if (GridDataManager.Instance != null)
        {
            envData = GridDataManager.Instance.GetRawGridData();
        }
        else if (mapGenerator != null)
        {
            envData = mapGenerator.GetEnvironmentGridData();
        }

        buildingState = new PlacementState(
            ID,
            grid,
            preview,
            database,
            floorData,
            furnitureData,
            objectPlacer,
            envData,
            placedBuildings,
            OnBuildingPlacedInternal
        );

        IsMovingMode = false;
        IsRemovingMode = false;

        inputManager.OnClicked += PlaceStructure;
        inputManager.OnExit += StopPlacement;

        OnPlacementStarted?.Invoke();
        Debug.Log($"[PlacementSystem] 배치 모드 시작: {data.Name}");
    }

    /// <summary>
    /// ★ 건물 이동 모드 시작 (C 키)
    /// BuildingUIManager에서 호출됨
    /// </summary>
    public void StartMoving()
    {
        StopPlacement();
        gridVisualization.SetActive(true);

        // ★ 이동 모드는 선택 전까지 1x1
        currentPlacementSize = Vector2Int.one;

        buildingState = new MovingState(
            grid,
            preview,
            floorData,
            furnitureData,
            database,
            placedBuildings,
            OnBuildingMovedInternal
        );

        IsMovingMode = true;
        IsRemovingMode = false;

        inputManager.OnClicked += PlaceStructure;
        inputManager.OnExit += StopPlacement;

        OnPlacementStarted?.Invoke();
        Debug.Log("[PlacementSystem] 이동 모드 시작 - 이동할 건물을 클릭하세요");
    }

    public void StartRemoving()
    {
        StopPlacement();
        gridVisualization.SetActive(true);

        // ★ 삭제 모드는 1x1
        currentPlacementSize = Vector2Int.one;

        buildingState = new RemovingState(
            grid,
            preview,
            floorData,
            furnitureData,
            objectPlacer,
            placedBuildings,
            OnBuildingRemovedInternal
        );

        IsMovingMode = false;
        IsRemovingMode = true;

        inputManager.OnClicked += PlaceStructure;
        inputManager.OnExit += StopPlacement;

        OnPlacementStarted?.Invoke();
        Debug.Log("[PlacementSystem] 삭제 모드 시작 - 삭제할 건물을 클릭하세요");
    }

    private void PlaceStructure()
    {
        if (inputManager.IsPointerOverUI())
        {
            return;
        }
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();

        // ★ 중앙 보정 + 클램핑 적용
        Vector3Int gridPosition = GetCenteredAndClampedGridPosition(mousePosition, currentPlacementSize);

        buildingState.OnAction(gridPosition);
    }

    public void StopPlacement()
    {
        if (buildingState == null)
            return;

        gridVisualization.SetActive(false);
        buildingState.EndState();
        inputManager.OnClicked -= PlaceStructure;
        inputManager.OnExit -= StopPlacement;
        lastDetectedPosition = Vector3Int.zero;
        buildingState = null;

        // ★ 모드 플래그 초기화
        IsMovingMode = false;
        IsRemovingMode = false;

        // ★ 크기 초기화
        currentPlacementSize = Vector2Int.one;

        OnPlacementEnded?.Invoke();
        Debug.Log("[PlacementSystem] 모드 종료");
    }

    private void Update()
    {
        if (buildingState == null)
            return;
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();

        // ★ 중앙 보정 + 클램핑 적용
        Vector3Int gridPosition = GetCenteredAndClampedGridPosition(mousePosition, currentPlacementSize);

        if (lastDetectedPosition != gridPosition)
        {
            buildingState.UpdateState(gridPosition);
            lastDetectedPosition = gridPosition;
        }
    }

    /// <summary>
    /// ★ 마우스 위치를 중앙 기준으로 보정하고, 맵 범위 내로 클램핑
    /// </summary>
    private Vector3Int GetCenteredAndClampedGridPosition(Vector3 mouseWorldPos, Vector2Int size)
    {
        int originX, originZ;

        // X축 계산
        if (size.x % 2 == 0) // 짝수: 셀 교차점이 중심
        {
            int centerX = Mathf.RoundToInt(mouseWorldPos.x);
            originX = centerX - size.x / 2;
        }
        else // 홀수: 셀 중앙이 중심
        {
            int centerCellX = Mathf.FloorToInt(mouseWorldPos.x);
            originX = centerCellX - size.x / 2;
        }

        // Z축 계산
        if (size.y % 2 == 0) // 짝수
        {
            int centerZ = Mathf.RoundToInt(mouseWorldPos.z);
            originZ = centerZ - size.y / 2;
        }
        else // 홀수
        {
            int centerCellZ = Mathf.FloorToInt(mouseWorldPos.z);
            originZ = centerCellZ - size.y / 2;
        }

        Vector3Int gridPos = new Vector3Int(originX, 0, originZ);

        // 맵 범위 내로 클램핑
        return ClampToMapBounds(gridPos, size);
    }

    /// <summary>
    /// ★ 그리드 위치를 맵 범위 내로 클램핑
    /// </summary>
    private Vector3Int ClampToMapBounds(Vector3Int gridPos, Vector2Int size)
    {
        if (GridDataManager.Instance == null)
            return gridPos;

        int cellCountX = GridDataManager.Instance.CellCountX;
        int cellCountZ = GridDataManager.Instance.CellCountZ;

        var (cellX, cellZ) = GridDataManager.Instance.GridPositionToCellIndex(gridPos);

        int clampedCellX = Mathf.Clamp(cellX, 0, cellCountX - size.x);
        int clampedCellZ = Mathf.Clamp(cellZ, 0, cellCountZ - size.y);

        Vector3Int clampedGridPos = GridDataManager.Instance.CellIndexToGridPosition(clampedCellX, clampedCellZ);

        return clampedGridPos;
    }

    private void OnBuildingPlacedInternal(Building building)
    {
        OnBuildingPlaced?.Invoke(building);
    }

    private void OnBuildingMovedInternal(Building building)
    {
        OnBuildingMoved?.Invoke(building);
        Debug.Log($"[PlacementSystem] 건물 이동됨: {building?.name}");
    }

    private void OnBuildingRemovedInternal(GameObject removedObj)
    {
        OnBuildingRemoved?.Invoke(removedObj);
        Debug.Log($"[PlacementSystem] 건물 제거됨: {removedObj?.name}");
    }

    // GridData 접근용
    public GridData GetFloorData() => floorData;
    public GridData GetFurnitureData() => furnitureData;

    /// <summary>
    /// 특정 위치의 건물 가져오기
    /// </summary>
    public GameObject GetBuildingAt(Vector3Int gridPosition)
    {
        return placedBuildings.TryGetValue(gridPosition, out var obj) ? obj : null;
    }

    /// <summary>
    /// 배치된 모든 건물 가져오기
    /// </summary>
    public Dictionary<Vector3Int, GameObject> GetAllPlacedBuildings()
    {
        return placedBuildings;
    }
}