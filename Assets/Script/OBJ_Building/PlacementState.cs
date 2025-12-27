using System;
using System.Collections.Generic;
using UnityEngine;

public class PlacementState : IBuildingState
{
    private int selectedObjectIndex = -1;
    int ID;
    Grid grid;
    PreviewSystem previewSystem;
    ObjectsDatabaseSO database;
    GridData floorData;
    GridData furnitureData;
    ObjectPlacer objectPlacer;
    GridData environmentData;
    Dictionary<Vector3Int, GameObject> placedBuildings;
    Action<Building> onBuildingPlaced;

    public PlacementState(int iD,
                          Grid grid,
                          PreviewSystem previewSystem,
                          ObjectsDatabaseSO database,
                          GridData floorData,
                          GridData furnitureData,
                          ObjectPlacer objectPlacer,
                          GridData environmentData = null,
                          Dictionary<Vector3Int, GameObject> placedBuildings = null,
                          Action<Building> onBuildingPlaced = null)
    {
        ID = iD;
        this.grid = grid;
        this.previewSystem = previewSystem;
        this.database = database;
        this.floorData = floorData;
        this.furnitureData = furnitureData;
        this.objectPlacer = objectPlacer;
        this.environmentData = environmentData;
        this.placedBuildings = placedBuildings ?? new Dictionary<Vector3Int, GameObject>();
        this.onBuildingPlaced = onBuildingPlaced;

        selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
        if (selectedObjectIndex > -1)
        {
            ObjectData objData = database.objectsData[selectedObjectIndex];

            // Blueprint 프리팹이 있으면 사용, 없으면 기존 Prefab 사용
            GameObject previewPrefab = objData.BlueprintPrefab ?? objData.Prefab;

            // null 체크 - 프리팹이 없으면 커서만 표시
            if (previewPrefab != null)
            {
                previewSystem.StartShowingPlacementPreview(previewPrefab, objData.Size);
            }
            else
            {
                // 프리팹 없이 커서만 표시
                previewSystem.StartShowingPlacementPreviewCursorOnly(objData.Size);
            }
        }
        else
        {
            throw new System.Exception($"No object with ID {iD}");
        }
    }

    public void EndState()
    {
        previewSystem.StopShowingPreview();
    }

    public void OnAction(Vector3Int gridPosition)
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);
        if (placementValidity == false)
        {
            return;
        }

        ObjectData objectData = database.objectsData[selectedObjectIndex];

        // 자원 비용 체크 & 소모
        if (objectData.ConstructionCosts != null && objectData.ConstructionCosts.Length > 0)
        {
            if (!ResourceManager.Instance.CanAfford(objectData.ConstructionCosts))
            {
                Debug.Log("자원이 부족합니다!");
                return;
            }
            ResourceManager.Instance.PayCosts(objectData.ConstructionCosts);
        }

        Vector3 worldPos = grid.CellToWorld(gridPosition);
        GameObject placedObject = null;
        Building building = null;

        // 건설 작업이 필요한 경우 (ConstructionWorkRequired > 0)
        if (objectData.ConstructionWorkRequired > 0)
        {
            // Building 컴포넌트로 생성 (Blueprint 상태)
            GameObject buildingObj = new GameObject($"Building_{objectData.Name}");
            buildingObj.transform.position = worldPos;

            building = buildingObj.AddComponent<Building>();
            building.Initialize(objectData, gridPosition, instantBuild: false);

            placedObject = buildingObj;
        }
        else
        {
            // 즉시 건설 (ConstructionWorkRequired가 0인 경우)
            if (objectData.Prefab != null)
            {
                placedObject = UnityEngine.Object.Instantiate(objectData.Prefab);
                placedObject.transform.position = worldPos;
                placedObject.name = $"Building_{objectData.Name}";

                // Building 컴포넌트 추가 (완료 상태)
                building = placedObject.AddComponent<Building>();
                building.Initialize(objectData, gridPosition, instantBuild: true);
            }
            else
            {
                Debug.LogWarning($"Prefab이 없습니다: {objectData.Name}");
            }
        }

        // GridData에 등록
        GridData selectedData = objectData.ID == 0 ? floorData : furnitureData;
        selectedData.AddObjectAt(gridPosition, objectData.Size, objectData.ID, 0);

        // 배치된 건물 추적 (크기가 1보다 큰 건물은 모든 셀에 등록)
        if (placedObject != null)
        {
            for (int x = 0; x < objectData.Size.x; x++)
            {
                for (int y = 0; y < objectData.Size.y; y++)
                {
                    Vector3Int cellPos = gridPosition + new Vector3Int(x, 0, y);
                    placedBuildings[cellPos] = placedObject;
                }
            }
        }

        // 콜백 호출
        if (building != null)
        {
            onBuildingPlaced?.Invoke(building);
        }

        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition), false);
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        ObjectData objectData = database.objectsData[selectedObjectIndex];
        GridData selectedData = objectData.ID == 0 ? floorData : furnitureData;

        if (!selectedData.CanPlaceObejctAt(gridPosition, objectData.Size))
            return false;

        if (environmentData != null && !environmentData.CanPlaceObejctAt(gridPosition, objectData.Size))
            return false;

        return true;
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);
        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition), placementValidity);
    }
}