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
            GameObject previewPrefab = objData.BlueprintPrefab ?? objData.Prefab;

            if (previewPrefab != null)
            {
                previewSystem.StartShowingPlacementPreview(previewPrefab, objData.Size);
            }
            else
            {
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
        if (!placementValidity)
        {
            return;
        }

        ObjectData objectData = database.objectsData[selectedObjectIndex];

        // 자원 체크
        if (objectData.ConstructionCosts != null && objectData.ConstructionCosts.Length > 0)
        {
            if (!ResourceManager.Instance.CanAfford(objectData.ConstructionCosts))
            {
                Debug.Log("자원이 부족합니다!");
                return;
            }
            ResourceManager.Instance.PayCosts(objectData.ConstructionCosts);
        }

        // ★ 기존 좌표 계산 유지!
        Vector3 worldPos = grid.CellToWorld(gridPosition);

        GameObject placedObject = null;
        Building building = null;

        if (objectData.ConstructionWorkRequired > 0)
        {
            GameObject buildingObj = new GameObject($"Building_{objectData.Name}");
            buildingObj.transform.position = worldPos;

            building = buildingObj.AddComponent<Building>();
            building.Initialize(objectData, gridPosition, instantBuild: false);

            placedObject = buildingObj;
        }
        else
        {
            if (objectData.Prefab != null)
            {
                placedObject = UnityEngine.Object.Instantiate(objectData.Prefab);
                placedObject.transform.position = worldPos;
                placedObject.name = $"Building_{objectData.Name}";

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

        // ★ GridDataManager에도 등록
        if (GridDataManager.Instance != null)
        {
            PlacedObjectType objType = objectData.Type == BuildingType.Floor
                ? PlacedObjectType.Floor
                : PlacedObjectType.Building;
            GridDataManager.Instance.PlaceObject(gridPosition, objectData.Size, objectData.ID, objType, placedObject);
        }

        // 배치된 건물 추적 (모든 셀에 등록)
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

        if (building != null)
        {
            onBuildingPlaced?.Invoke(building);
        }

        // ★ 기존 프리뷰 업데이트 유지
        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition), false);
    }

    /// <summary>
    /// ★ 배치 가능 여부 - 범위 체크 강화!
    /// </summary>
    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        ObjectData objectData = database.objectsData[selectedObjectIndex];
        Vector2Int size = objectData.Size;

        // ★ GridDataManager로 범위 체크 (2x2 등 모든 셀이 맵 안에 있는지)
        if (GridDataManager.Instance != null)
        {
            if (!GridDataManager.Instance.CanPlaceAt(gridPosition, size))
            {
                return false;
            }
        }

        // 기존 GridData 체크
        GridData selectedData = objectData.ID == 0 ? floorData : furnitureData;

        if (!selectedData.CanPlaceObejctAt(gridPosition, size))
            return false;

        // 환경 오브젝트 체크
        if (environmentData != null && !environmentData.CanPlaceObejctAt(gridPosition, size))
            return false;

        return true;
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);
        // ★ 기존 좌표 계산 유지!
        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition), placementValidity);
    }
}
