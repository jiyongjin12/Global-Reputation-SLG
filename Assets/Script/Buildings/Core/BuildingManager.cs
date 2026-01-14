using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 건물 중앙 관리자
/// - 모든 건물 등록/해제
/// - 타입별 건물 검색
/// - 하위 매니저 참조 (BedManager, CraftingManager 등)
/// </summary>
public class BuildingManager : MonoBehaviour
{
    public static BuildingManager Instance { get; private set; }

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // 모든 건물
    private List<Building> allBuildings = new List<Building>();

    // 타입별 캐시
    private List<Building> workstations = new List<Building>();
    private List<Building> storages = new List<Building>();
    private List<Building> harvestables = new List<Building>();
    private List<Building> craftingBuildings = new List<Building>();

    // 하위 매니저 참조
    private BedManager _bedManager;
    private CraftingManager _craftingManager;

    // 이벤트
    public event Action<Building> OnBuildingRegistered;
    public event Action<Building> OnBuildingUnregistered;

    // Properties
    public IReadOnlyList<Building> AllBuildings => allBuildings;
    public IReadOnlyList<Building> Workstations => workstations;
    public IReadOnlyList<Building> Storages => storages;
    public IReadOnlyList<Building> CraftingBuildings => craftingBuildings;

    // 하위 매니저 접근
    public BedManager BedManager => _bedManager;
    public CraftingManager CraftingManager => _craftingManager;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(this);
            return;
        }
    }

    private void Start()
    {
        FindSubManagers();
    }

    /// <summary>
    /// 하위 매니저 찾기
    /// </summary>
    private void FindSubManagers()
    {
        _bedManager = FindObjectOfType<BedManager>();
        _craftingManager = FindObjectOfType<CraftingManager>();

        if (showDebugLogs)
        {
            Debug.Log($"[BuildingManager] BedManager: {(_bedManager != null ? "Found" : "Not Found")}");
            Debug.Log($"[BuildingManager] CraftingManager: {(_craftingManager != null ? "Found" : "Not Found")}");
        }
    }

    // ==================== 등록/해제 ====================

    public void RegisterBuilding(Building building)
    {
        if (building == null || allBuildings.Contains(building))
            return;

        allBuildings.Add(building);

        // 타입별 캐시 업데이트
        if (building.HasWorkstation) workstations.Add(building);
        if (building.HasStorage) storages.Add(building);
        if (building.HasHarvestable) harvestables.Add(building);

        // 제작 건물 체크
        var craftingComp = building.GetComponent<CraftingBuildingComponent>();
        if (craftingComp != null)
        {
            craftingBuildings.Add(building);
        }

        if (showDebugLogs)
            Debug.Log($"[BuildingManager] 건물 등록: {building.Data?.Name ?? building.name}");

        OnBuildingRegistered?.Invoke(building);
    }

    public void UnregisterBuilding(Building building)
    {
        if (building == null || !allBuildings.Contains(building))
            return;

        allBuildings.Remove(building);
        workstations.Remove(building);
        storages.Remove(building);
        harvestables.Remove(building);
        craftingBuildings.Remove(building);

        if (showDebugLogs)
            Debug.Log($"[BuildingManager] 건물 해제: {building.Data?.Name ?? building.name}");

        OnBuildingUnregistered?.Invoke(building);
    }

    // ==================== 검색 ====================

    public Building GetBuildingAt(Vector3Int gridPosition)
    {
        return allBuildings.FirstOrDefault(b => b.GridPosition == gridPosition);
    }

    public List<Building> GetBuildingsOfType<T>() where T : class
    {
        return allBuildings.Where(b => b.GetComponent<T>() != null).ToList();
    }

    public Building GetNearestBuilding(Vector3 position, Func<Building, bool> predicate = null)
    {
        Building nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var building in allBuildings)
        {
            if (predicate != null && !predicate(building))
                continue;

            float dist = Vector3.Distance(position, building.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = building;
            }
        }

        return nearest;
    }

    /// <summary>가장 가까운 작업 가능한 워크스테이션 찾기</summary>
    public Building GetNearestAvailableWorkstation(Vector3 position, WorkTaskType? taskType = null)
    {
        return GetNearestBuilding(position, b =>
        {
            if (!b.HasWorkstation) return false;
            if (b.Workstation.IsOccupied) return false;
            if (!b.Workstation.CanStartWork) return false;

            if (taskType.HasValue && b.Workstation.TaskType != taskType.Value)
                return false;

            return true;
        });
    }

    /// <summary>가장 가까운 저장소 찾기</summary>
    public Building GetNearestStorage(Vector3 position, bool mustHaveSpace = true)
    {
        return GetNearestBuilding(position, b =>
        {
            if (!b.HasStorage) return false;
            if (mustHaveSpace && b.Storage.IsFull) return false;
            return true;
        });
    }

    /// <summary>메인 저장소 가져오기</summary>
    public Building GetMainStorage()
    {
        var mainStorage = storages.FirstOrDefault(b =>
            b.GetComponent<StorageComponent>()?.IsMainStorage == true);

        return mainStorage ?? storages.FirstOrDefault();
    }

    /// <summary>특정 아이템이 있는 저장소 찾기</summary>
    public Building GetStorageWithItem(ResourceItemSO item, int minAmount = 1)
    {
        return storages.FirstOrDefault(b =>
            b.Storage != null && b.Storage.HasItem(item, minAmount));
    }

    /// <summary>수확 가능한 농경지 찾기</summary>
    public Building GetNearestHarvestable(Vector3 position, bool readyOnly = true)
    {
        return GetNearestBuilding(position, b =>
        {
            if (!b.HasHarvestable) return false;
            if (readyOnly && !b.Harvestable.IsReadyToHarvest) return false;
            return true;
        });
    }

    /// <summary>심을 수 있는 농경지 찾기</summary>
    public Building GetNearestPlantable(Vector3 position)
    {
        return GetNearestBuilding(position, b =>
        {
            if (!b.HasHarvestable) return false;
            return b.Harvestable.CurrentCrop == null;
        });
    }

    // ==================== 편의 메서드 (하위 매니저 연동) ====================

    /// <summary>
    /// Unit을 요리 건물에 배정 (플레이어 명령)
    /// </summary>
    public CraftingBuildingComponent AssignUnitToCooking(Unit unit)
    {
        if (_craftingManager == null)
            _craftingManager = FindObjectOfType<CraftingManager>();

        return _craftingManager?.AssignUnitByPlayerCommand(unit, "Cooking");
    }

    /// <summary>
    /// Unit을 제작 건물에 배정 (플레이어 명령)
    /// </summary>
    public CraftingBuildingComponent AssignUnitToCrafting(Unit unit)
    {
        if (_craftingManager == null)
            _craftingManager = FindObjectOfType<CraftingManager>();

        return _craftingManager?.AssignUnitByPlayerCommand(unit, "Crafting");
    }

    /// <summary>
    /// Unit에게 침대 배정 시도
    /// </summary>
    public bool AssignBedToUnit(Unit unit)
    {
        if (_bedManager == null)
            _bedManager = FindObjectOfType<BedManager>();

        if (_bedManager == null)
        {
            Debug.LogWarning("[BuildingManager] BedManager를 찾을 수 없습니다.");
            return false;
        }

        // BedManager의 실제 메서드에 맞게 수정 필요
        // 예: _bedManager.AssignBed(unit) 또는 _bedManager.RequestBed(unit)
        // 현재는 BedManager 메서드명을 모르므로 주석 처리
        // return _bedManager.TryAssignBed(unit);

        Debug.Log($"[BuildingManager] Unit {unit?.UnitName}에게 침대 배정 요청");
        return false; // BedManager 메서드 확인 후 수정
    }

    /// <summary>
    /// 대기열이 있는 제작 건물 가져오기
    /// </summary>
    public CraftingBuildingComponent GetAvailableCraftingBuilding(string buildingType)
    {
        if (_craftingManager == null)
            _craftingManager = FindObjectOfType<CraftingManager>();

        return _craftingManager?.GetAvailableBuilding(buildingType);
    }

    // ==================== 통계 ====================

    public int GetBuildingCount() => allBuildings.Count;

    public int GetBuildingCount<T>() where T : class
    {
        return allBuildings.Count(b => b.GetComponent<T>() != null);
    }

    public int GetBuildingCountByData(ObjectData data)
    {
        return allBuildings.Count(b => b.Data == data);
    }

#if UNITY_EDITOR
    [ContextMenu("Print All Buildings")]
    private void DebugPrintAllBuildings()
    {
        Debug.Log($"[BuildingManager] === 등록된 건물 ({allBuildings.Count}개) ===");
        foreach (var b in allBuildings)
        {
            string features = "";
            if (b.HasWorkstation) features += "[Work]";
            if (b.HasStorage) features += "[Store]";
            if (b.HasHarvestable) features += "[Farm]";
            if (b.GetComponent<CraftingBuildingComponent>() != null) features += "[Craft]";

            Debug.Log($"  - {b.Data?.Name ?? b.name} {features}");
        }
    }

    [ContextMenu("Print Sub Managers")]
    private void DebugPrintSubManagers()
    {
        FindSubManagers();
        Debug.Log($"[BuildingManager] BedManager: {(_bedManager != null ? _bedManager.name : "NULL")}");
        Debug.Log($"[BuildingManager] CraftingManager: {(_craftingManager != null ? _craftingManager.name : "NULL")}");
    }
#endif
}