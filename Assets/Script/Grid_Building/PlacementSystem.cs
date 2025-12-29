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
    public event Action<GameObject> OnBuildingRemoved;
    public event Action<string> OnPlacementFailed;

    private void Start()
    {
        gridVisualization.SetActive(false);
        floorData = new();
        furnitureData = new();
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

        // ★ 수정: GridDataManager 사용
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

    private void StopPlacement()
    {
        if (buildingState == null)
            return;
        gridVisualization.SetActive(false);
        buildingState.EndState();
        inputManager.OnClicked -= PlaceStructure;
        inputManager.OnExit -= StopPlacement;
        lastDetectedPosition = Vector3Int.zero;
        buildingState = null;
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

        // ★ 수정: Building이 스스로 TaskManager에 등록하므로 여기서는 호출 안 함
        // TaskManager.Instance?.AddConstructionTask(building);  // 삭제됨
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
}