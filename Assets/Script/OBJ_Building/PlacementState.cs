using System;
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

    // 추가: 환경 GridData (자연환경 충돌 체크용)
    GridData environmentData;

    // 추가: 건물 배치 콜백
    Action<Building> onBuildingPlaced;

    public PlacementState(int iD, Grid grid, PreviewSystem previewSystem, ObjectsDatabaseSO database, GridData floorData, GridData furnitureData, ObjectPlacer objectPlacer, GridData environmentData = null, Action<Building> onBuildingPlaced = null)
    {
        ID = iD;
        this.grid = grid;
        this.previewSystem = previewSystem;
        this.database = database;
        this.floorData = floorData;
        this.furnitureData = furnitureData;
        this.objectPlacer = objectPlacer;
        this.environmentData = environmentData;
        this.onBuildingPlaced = onBuildingPlaced;

        selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
        if (selectedObjectIndex > -1)
        {
            // Blueprint 프리팹이 있으면 사용, 없으면 기존 Prefab 사용
            GameObject previewPrefab = database.objectsData[selectedObjectIndex].BlueprintPrefab
                ?? database.objectsData[selectedObjectIndex].Prefab;

            previewSystem.StartShowingPlacementPreview(
                previewPrefab,
                database.objectsData[selectedObjectIndex].Size);
        }
        else
            throw new System.Exception($"No object with ID {iD}");
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

        // 건설 작업이 필요한 경우 (ConstructionWorkRequired > 0)
        if (objectData.ConstructionWorkRequired > 0)
        {
            // Building 컴포넌트로 생성 (Blueprint 상태)
            GameObject buildingObj = new GameObject($"Building_{objectData.Name}");
            buildingObj.transform.position = worldPos;

            Building building = buildingObj.AddComponent<Building>();
            building.Initialize(objectData, gridPosition, instantBuild: false);

            // GridData에 등록
            GridData selectedData = objectData.ID == 0 ? floorData : furnitureData;
            selectedData.AddObjectAt(gridPosition, objectData.Size, objectData.ID, 0);

            // 콜백 호출 (TaskManager에 건설 작업 등록용)
            onBuildingPlaced?.Invoke(building);
        }
        else
        {
            // 기존 방식: 즉시 건설 (ConstructionWorkRequired가 0인 경우)
            int index = objectPlacer.PlaceObject(objectData.Prefab, worldPos);

            GridData selectedData = objectData.ID == 0 ? floorData : furnitureData;
            selectedData.AddObjectAt(gridPosition, objectData.Size, objectData.ID, index);
        }

        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition), false);
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        ObjectData objectData = database.objectsData[selectedObjectIndex];
        GridData selectedData = objectData.ID == 0 ? floorData : furnitureData;

        // 기존 체크
        if (!selectedData.CanPlaceObejctAt(gridPosition, objectData.Size))
            return false;

        // 추가: 환경 데이터 체크 (나무, 돌 등과 충돌)
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