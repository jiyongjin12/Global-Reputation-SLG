using System;
using System.Collections.Generic;
using UnityEngine;

public class RemovingState : IBuildingState
{
    Grid grid;
    PreviewSystem previewSystem;
    GridData floorData;
    GridData furnitureData;
    ObjectPlacer objectPlacer;
    Dictionary<Vector3Int, GameObject> placedBuildings;
    Action<GameObject> onBuildingRemoved;

    public RemovingState(Grid grid,
                         PreviewSystem previewSystem,
                         GridData floorData,
                         GridData furnitureData,
                         ObjectPlacer objectPlacer,
                         Dictionary<Vector3Int, GameObject> placedBuildings = null,
                         Action<GameObject> onBuildingRemoved = null)
    {
        this.grid = grid;
        this.previewSystem = previewSystem;
        this.floorData = floorData;
        this.furnitureData = furnitureData;
        this.objectPlacer = objectPlacer;
        this.placedBuildings = placedBuildings ?? new Dictionary<Vector3Int, GameObject>();
        this.onBuildingRemoved = onBuildingRemoved;
        previewSystem.StartShowingRemovePreview();
    }

    public void EndState()
    {
        previewSystem.StopShowingPreview();
    }

    public void OnAction(Vector3Int gridPosition)
    {
        GridData selectedData = null;

        // 어떤 GridData에 있는지 확인
        if (!furnitureData.CanPlaceObejctAt(gridPosition, Vector2Int.one))
        {
            selectedData = furnitureData;
        }
        else if (!floorData.CanPlaceObejctAt(gridPosition, Vector2Int.one))
        {
            selectedData = floorData;
        }

        if (selectedData == null)
        {
            Debug.Log("[RemovingState] 제거할 건물이 없습니다.");
            return;
        }

        // 배치된 건물 찾기
        GameObject buildingToRemove = null;
        if (placedBuildings.TryGetValue(gridPosition, out buildingToRemove))
        {
            // Building 컴포넌트가 있으면 크기 정보 가져오기
            Building building = buildingToRemove?.GetComponent<Building>();
            Vector2Int size = Vector2Int.one;

            if (building != null && building.Data != null)
            {
                size = building.Data.Size;

                // 모든 점유 셀에서 placedBuildings 제거
                for (int x = 0; x < size.x; x++)
                {
                    for (int y = 0; y < size.y; y++)
                    {
                        Vector3Int cellPos = building.GridPosition + new Vector3Int(x, 0, y);
                        placedBuildings.Remove(cellPos);
                    }
                }
            }
            else
            {
                // Building 컴포넌트 없으면 현재 위치만 제거
                placedBuildings.Remove(gridPosition);
            }

            // GameObject 제거
            if (buildingToRemove != null)
            {
                onBuildingRemoved?.Invoke(buildingToRemove);
                UnityEngine.Object.Destroy(buildingToRemove);
                Debug.Log($"[RemovingState] 건물 제거됨: {buildingToRemove.name}");
            }
        }
        else
        {
            // placedBuildings에 없는 경우 (기존 ObjectPlacer 방식)
            int gameObjectIndex = selectedData.GetRepresentationIndex(gridPosition);
            if (gameObjectIndex != -1)
            {
                objectPlacer.RemoveObjectAt(gameObjectIndex);
            }
        }

        // GridData에서 제거
        selectedData.RemoveObjectAt(gridPosition);

        Vector3 cellPosition = grid.CellToWorld(gridPosition);
        previewSystem.UpdatePosition(cellPosition, CheckIfSelectionIsValid(gridPosition));
    }

    private bool CheckIfSelectionIsValid(Vector3Int gridPosition)
    {
        return !(furnitureData.CanPlaceObejctAt(gridPosition, Vector2Int.one) &&
            floorData.CanPlaceObejctAt(gridPosition, Vector2Int.one));
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        bool validity = CheckIfSelectionIsValid(gridPosition);
        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition), validity);
    }
}