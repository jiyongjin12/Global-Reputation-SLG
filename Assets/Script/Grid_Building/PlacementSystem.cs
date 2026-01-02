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

    // 이벤트
    public event Action<Building> OnBuildingPlaced;
    public event Action<Building> OnBuildingMoved;
    public event Action<GameObject> OnBuildingRemoved;
    public event Action<string> OnPlacementFailed;

    private void Start()
    {
        gridVisualization.SetActive(false);
        floorData = new();
        furnitureData = new();

        // ★ GameInputManager 이벤트 연결
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.OnActionTriggered += HandleGameAction;
        }
    }

    private void OnDestroy()
    {
        // 이벤트 해제
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.OnActionTriggered -= HandleGameAction;
        }
    }

    /// <summary>
    /// ★ GameInputManager에서 액션 처리
    /// </summary>
    private void HandleGameAction(GameAction action)
    {
        switch (action)
        {
            case GameAction.MoveBuilding:
                if (buildingState == null) // 다른 모드가 아닐 때만
                    StartMoving();
                break;

            case GameAction.DeleteBuilding:
                if (buildingState == null)
                    StartRemoving();
                break;
        }
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
            if (!ResourceManager.Instance.CanAfford(data.ConstructionCosts))
            {
                OnPlacementFailed?.Invoke("자원이 부족합니다!");
                Debug.Log("자원이 부족합니다!");
                return;
            }
        }

        gridVisualization.SetActive(true);

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

        inputManager.OnClicked += PlaceStructure;
        inputManager.OnExit += StopPlacement;

        // ★ GameInputManager에 현재 모드 등록
        GameInputManager.Instance?.SetCurrentMode(StopPlacement, 50);
    }

    public void StartRemoving()
    {
        StopPlacement();
        gridVisualization.SetActive(true);
        buildingState = new RemovingState(
            grid,
            preview,
            floorData,
            furnitureData,
            objectPlacer,
            placedBuildings,
            OnBuildingRemovedInternal
        );
        inputManager.OnClicked += PlaceStructure;
        inputManager.OnExit += StopPlacement;

        // ★ GameInputManager에 현재 모드 등록
        GameInputManager.Instance?.SetCurrentMode(StopPlacement, 50);

        Debug.Log("[PlacementSystem] 삭제 모드 시작 (X키 또는 버튼)");
    }

    /// <summary>
    /// ★ 건물 이동 모드 시작
    /// </summary>
    public void StartMoving()
    {
        StopPlacement();
        gridVisualization.SetActive(true);

        buildingState = new MovingState(
            grid,
            preview,
            floorData,
            furnitureData,
            database,
            placedBuildings,
            OnBuildingMovedInternal
        );

        inputManager.OnClicked += PlaceStructure;
        inputManager.OnExit += StopPlacement;

        // ★ GameInputManager에 현재 모드 등록
        GameInputManager.Instance?.SetCurrentMode(StopPlacement, 50);

        Debug.Log("[PlacementSystem] 이동 모드 시작 (C키 또는 버튼)");
    }

    private void PlaceStructure()
    {
        if (inputManager.IsPointerOverUI())
        {
            return;
        }
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

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

        // ★ GameInputManager에서 현재 모드 해제
        GameInputManager.Instance?.ClearCurrentMode();
    }

    private void Update()
    {
        if (buildingState == null)
            return;
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);
        if (lastDetectedPosition != gridPosition)
        {
            buildingState.UpdateState(gridPosition);
            lastDetectedPosition = gridPosition;
        }
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
    /// 현재 모드 활성화 여부
    /// </summary>
    public bool IsInBuildMode => buildingState != null;
}