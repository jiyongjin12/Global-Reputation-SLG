using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물 이동 State
/// 
/// Phase 1: 건물 선택
/// - 클릭으로 건물 선택
/// - 선택된 건물 하이라이트
/// 
/// Phase 2: 위치 이동
/// - 미리보기 표시
/// - 유효성 체크 (초록/빨강)
/// - 클릭으로 이동 확정
/// - ESC로 취소 (원래 위치 복구)
/// </summary>
public class MovingState : IBuildingState
{
    private enum Phase { SelectBuilding, SelectPosition }

    private Phase currentPhase = Phase.SelectBuilding;

    // 참조
    private Grid grid;
    private PreviewSystem previewSystem;
    private GridData floorData;
    private GridData furnitureData;
    private ObjectsDatabaseSO database;
    private Dictionary<Vector3Int, GameObject> placedBuildings;
    private Action<Building> onBuildingMoved;

    // 선택된 건물 정보
    private Building selectedBuilding;
    private Vector3Int originalGridPosition;
    private Vector2Int buildingSize;
    private int buildingID;

    // 원래 위치 복구용
    private bool wasPlaced = false;

    public MovingState(
        Grid grid,
        PreviewSystem previewSystem,
        GridData floorData,
        GridData furnitureData,
        ObjectsDatabaseSO database,
        Dictionary<Vector3Int, GameObject> placedBuildings,
        Action<Building> onBuildingMoved = null)
    {
        this.grid = grid;
        this.previewSystem = previewSystem;
        this.floorData = floorData;
        this.furnitureData = furnitureData;
        this.database = database;
        this.placedBuildings = placedBuildings ?? new Dictionary<Vector3Int, GameObject>();
        this.onBuildingMoved = onBuildingMoved;

        // Phase 1: 건물 선택 모드로 시작
        currentPhase = Phase.SelectBuilding;
        previewSystem.StartShowingRemovePreview(); // 선택 커서 표시

        Debug.Log("[MovingState] 건물 선택 모드 시작 - 이동할 건물을 클릭하세요");
    }

    public void EndState()
    {
        // 취소 시 원래 위치로 복구
        if (currentPhase == Phase.SelectPosition && selectedBuilding != null && !wasPlaced)
        {
            RestoreOriginalPosition();
        }

        previewSystem.StopShowingPreview();
        selectedBuilding = null;
    }

    public void OnAction(Vector3Int gridPosition)
    {
        switch (currentPhase)
        {
            case Phase.SelectBuilding:
                TrySelectBuilding(gridPosition);
                break;

            case Phase.SelectPosition:
                TryPlaceBuilding(gridPosition);
                break;
        }
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        switch (currentPhase)
        {
            case Phase.SelectBuilding:
                // 선택 가능한 건물이 있는지 표시
                bool hasBuilding = placedBuildings.ContainsKey(gridPosition);
                previewSystem.UpdatePosition(grid.CellToWorld(gridPosition), hasBuilding);
                break;

            case Phase.SelectPosition:
                // 배치 가능 여부 표시
                bool canPlace = CheckPlacementValidity(gridPosition);
                previewSystem.UpdatePosition(grid.CellToWorld(gridPosition), canPlace);
                break;
        }
    }

    // ==================== Phase 1: 건물 선택 ====================

    private void TrySelectBuilding(Vector3Int gridPosition)
    {
        if (!placedBuildings.TryGetValue(gridPosition, out var buildingObj))
        {
            Debug.Log("[MovingState] 이 위치에 건물이 없습니다.");
            return;
        }

        selectedBuilding = buildingObj.GetComponent<Building>();
        if (selectedBuilding == null)
        {
            Debug.Log("[MovingState] Building 컴포넌트가 없습니다.");
            return;
        }

        // 건물 정보 저장
        originalGridPosition = selectedBuilding.GridPosition;
        buildingSize = selectedBuilding.Data?.Size ?? Vector2Int.one;
        buildingID = selectedBuilding.Data?.ID ?? 0;

        Debug.Log($"[MovingState] 건물 선택됨: {selectedBuilding.name} at {originalGridPosition}, Size: {buildingSize}");

        // Grid 데이터에서 임시 제거 (이동 가능하게)
        RemoveFromGridData(originalGridPosition, buildingSize);

        // Phase 2로 전환: 위치 선택 모드
        currentPhase = Phase.SelectPosition;

        // 미리보기 설정
        ObjectData objData = database?.GetObjectByID(buildingID);
        if (objData != null)
        {
            GameObject previewPrefab = objData.BlueprintPrefab ?? objData.Prefab;
            if (previewPrefab != null)
            {
                previewSystem.StopShowingPreview();
                previewSystem.StartShowingPlacementPreview(previewPrefab, buildingSize);
            }
            else
            {
                previewSystem.StopShowingPreview();
                previewSystem.StartShowingPlacementPreviewCursorOnly(buildingSize);
            }
        }
        else
        {
            previewSystem.StopShowingPreview();
            previewSystem.StartShowingPlacementPreviewCursorOnly(buildingSize);
        }

        Debug.Log("[MovingState] 위치 선택 모드 - 새 위치를 클릭하세요 (ESC로 취소)");
    }

    // ==================== Phase 2: 위치 이동 ====================

    private void TryPlaceBuilding(Vector3Int gridPosition)
    {
        if (!CheckPlacementValidity(gridPosition))
        {
            Debug.Log("[MovingState] 이 위치에 배치할 수 없습니다.");
            return;
        }

        // 건물 이동!
        MoveBuilding(gridPosition);
        wasPlaced = true;

        Debug.Log($"[MovingState] 건물 이동 완료: {originalGridPosition} → {gridPosition}");

        // Phase 1로 돌아가기 (다른 건물 이동 가능)
        ResetToSelectPhase();
    }

    private void MoveBuilding(Vector3Int newGridPosition)
    {
        if (selectedBuilding == null) return;

        // 1. 월드 위치 계산
        Vector3 worldPos = grid.CellToWorld(newGridPosition);

        // 2. GameObject 위치 업데이트
        selectedBuilding.transform.position = worldPos;

        // 3. Building 내부 GridPosition 업데이트
        selectedBuilding.UpdateGridPosition(newGridPosition);

        // ★ 4. 건설 Task 위치 업데이트 (유닛들도 새 위치로 이동)
        selectedBuilding.UpdateTaskLocation(worldPos);

        // 5. Grid 데이터에 새 위치 등록
        AddToGridData(newGridPosition, buildingSize, buildingID, selectedBuilding.gameObject);

        // 6. placedBuildings 딕셔너리 업데이트
        UpdatePlacedBuildingsDictionary(newGridPosition);

        // 7. 이벤트 호출
        onBuildingMoved?.Invoke(selectedBuilding);
    }

    private void RestoreOriginalPosition()
    {
        if (selectedBuilding == null) return;

        Debug.Log($"[MovingState] 원래 위치로 복구: {originalGridPosition}");

        // Grid 데이터에 원래 위치 등록
        AddToGridData(originalGridPosition, buildingSize, buildingID, selectedBuilding.gameObject);

        // placedBuildings 딕셔너리 복구
        for (int x = 0; x < buildingSize.x; x++)
        {
            for (int y = 0; y < buildingSize.y; y++)
            {
                Vector3Int cellPos = originalGridPosition + new Vector3Int(x, 0, y);
                placedBuildings[cellPos] = selectedBuilding.gameObject;
            }
        }
    }

    private void ResetToSelectPhase()
    {
        selectedBuilding = null;
        currentPhase = Phase.SelectBuilding;
        wasPlaced = false;

        previewSystem.StopShowingPreview();
        previewSystem.StartShowingRemovePreview();

        Debug.Log("[MovingState] 건물 선택 모드로 돌아감");
    }

    // ==================== Grid 데이터 관리 ====================

    private void RemoveFromGridData(Vector3Int gridPosition, Vector2Int size)
    {
        // PlacementSystem의 GridData에서 제거
        GridData selectedData = buildingID == 0 ? floorData : furnitureData;

        try
        {
            selectedData.RemoveObjectAt(gridPosition);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MovingState] GridData 제거 실패: {e.Message}");
        }

        // GridDataManager에서 제거
        if (GridDataManager.Instance != null)
        {
            GridDataManager.Instance.RemoveObject(gridPosition);
        }

        // placedBuildings에서 제거
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector3Int cellPos = gridPosition + new Vector3Int(x, 0, y);
                placedBuildings.Remove(cellPos);
            }
        }
    }

    private void AddToGridData(Vector3Int gridPosition, Vector2Int size, int id, GameObject obj)
    {
        // PlacementSystem의 GridData에 등록
        GridData selectedData = id == 0 ? floorData : furnitureData;
        selectedData.AddObjectAt(gridPosition, size, id, 0);

        // GridDataManager에 등록
        if (GridDataManager.Instance != null)
        {
            PlacedObjectType objType = id == 0 ? PlacedObjectType.Floor : PlacedObjectType.Building;
            GridDataManager.Instance.PlaceObject(gridPosition, size, id, objType, obj);
        }
    }

    private void UpdatePlacedBuildingsDictionary(Vector3Int newGridPosition)
    {
        // 새 위치에 등록
        for (int x = 0; x < buildingSize.x; x++)
        {
            for (int y = 0; y < buildingSize.y; y++)
            {
                Vector3Int cellPos = newGridPosition + new Vector3Int(x, 0, y);
                placedBuildings[cellPos] = selectedBuilding.gameObject;
            }
        }
    }

    // ==================== 유효성 체크 ====================

    private bool CheckPlacementValidity(Vector3Int gridPosition)
    {
        // GridDataManager로 범위 체크
        if (GridDataManager.Instance != null)
        {
            if (!GridDataManager.Instance.CanPlaceAt(gridPosition, buildingSize))
            {
                return false;
            }
        }

        // 기존 GridData 체크
        GridData selectedData = buildingID == 0 ? floorData : furnitureData;
        if (!selectedData.CanPlaceObejctAt(gridPosition, buildingSize))
        {
            return false;
        }

        return true;
    }
}