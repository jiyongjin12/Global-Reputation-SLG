using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 전역 작업 관리자
/// 건설, 채집 등 작업을 관리하고 유닛에게 할당
/// </summary>
public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    // 대기 중인 작업들
    private List<UnitTask> pendingTasks = new List<UnitTask>();

    // 등록된 유닛들
    private List<Unit> registeredUnits = new List<Unit>();

    // 건설 대기 중인 건물들
    private List<Building> pendingConstructions = new List<Building>();

    // 수거 대기 중인 드롭 아이템들 (음식 제외)
    private List<DroppedItem> pendingPickups = new List<DroppedItem>();

    // 각 건물에 할당된 작업자 수 추적
    private Dictionary<Building, int> buildingWorkerCount = new Dictionary<Building, int>();

    // 창고 건물 참조
    [SerializeField] private Building storageBuilding;

    [Header("Settings")]
    [SerializeField] private float taskAssignInterval = 0.5f;
    [SerializeField] private int maxWorkersPerBuilding = 3;      // 건물당 최대 작업자
    [SerializeField] private bool assignToAIUnits = true;        // AI 유닛에게도 작업 할당할지

    private float lastAssignTime;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (Time.time - lastAssignTime >= taskAssignInterval)
        {
            CleanupLists();
            AssignPendingTasks();
            lastAssignTime = Time.time;
        }
    }

    /// <summary>
    /// null 항목 정리
    /// </summary>
    private void CleanupLists()
    {
        pendingConstructions.RemoveAll(b => b == null);
        pendingPickups.RemoveAll(i => i == null);
        registeredUnits.RemoveAll(u => u == null);

        // 완료된 건물 작업자 수 정리
        var completedBuildings = buildingWorkerCount.Keys.Where(b => b == null || !b.NeedsConstruction).ToList();
        foreach (var b in completedBuildings)
        {
            buildingWorkerCount.Remove(b);
        }
    }

    /// <summary>
    /// 유닛 등록
    /// </summary>
    public void RegisterUnit(Unit unit)
    {
        if (!registeredUnits.Contains(unit))
        {
            registeredUnits.Add(unit);
            unit.OnUnitDeath += HandleUnitDeath;
            unit.OnTaskCompleted += HandleTaskCompleted;
        }
    }

    /// <summary>
    /// 유닛 등록 해제
    /// </summary>
    public void UnregisterUnit(Unit unit)
    {
        registeredUnits.Remove(unit);
        unit.OnUnitDeath -= HandleUnitDeath;
        unit.OnTaskCompleted -= HandleTaskCompleted;
    }

    /// <summary>
    /// 건설 작업 추가
    /// </summary>
    public void AddConstructionTask(Building building)
    {
        if (building == null) return;

        if (!pendingConstructions.Contains(building))
        {
            pendingConstructions.Add(building);
            buildingWorkerCount[building] = 0;
            building.OnConstructionComplete += HandleConstructionComplete;

            Debug.Log($"[TaskManager] 건설 작업 추가됨: {building.Data?.Name ?? "Unknown"} (대기 중: {pendingConstructions.Count}개)");
        }
    }

    /// <summary>
    /// 아이템 수거 작업 추가 (음식은 자동 추가 안 함)
    /// </summary>
    public void AddPickupTask(DroppedItem item)
    {
        if (item == null) return;

        // 음식은 자동 수거에서 제외 (유닛이 배고플 때 직접 가져감)
        if (item.Resource != null && item.Resource.IsFood)
        {
            return;
        }

        if (!pendingPickups.Contains(item))
        {
            pendingPickups.Add(item);
            item.OnPickedUp += HandleItemPickedUp;
        }
    }

    /// <summary>
    /// 일반 작업 추가
    /// </summary>
    public void AddTask(UnitTask task)
    {
        pendingTasks.Add(task);
    }

    /// <summary>
    /// 대기 중인 작업을 유닛에게 할당
    /// </summary>
    private void AssignPendingTasks()
    {
        // 할당 가능한 유닛 찾기
        var availableUnits = GetAvailableUnits();

        if (availableUnits.Count == 0) return;

        foreach (var unit in availableUnits)
        {
            // 1순위: 인벤토리가 차 있으면 창고로 배달
            if (!unit.Inventory.IsEmpty && storageBuilding != null && storageBuilding.CurrentState == BuildingState.Completed)
            {
                Debug.Log($"[TaskManager] {unit.UnitName}에게 창고 배달 작업 할당");
                var deliverTask = new DeliverToStorageTask(storageBuilding);
                unit.AssignTask(new MoveToTask(storageBuilding.transform.position));
                unit.AssignTask(deliverTask);
                continue;
            }

            // 2순위: 건설 작업 (작업자 수 제한 확인)
            var pendingBuilding = FindAvailableConstruction();
            if (pendingBuilding != null)
            {
                Debug.Log($"[TaskManager] {unit.UnitName}에게 '{pendingBuilding.Data?.Name}' 건설 작업 할당");
                var moveTask = new MoveToTask(pendingBuilding.transform.position);
                var constructTask = new ConstructTask(pendingBuilding);
                unit.AssignTask(moveTask);
                unit.AssignTask(constructTask);

                // 작업자 수 증가
                buildingWorkerCount[pendingBuilding]++;
                continue;
            }

            // 3순위: 아이템 수거 (음식 제외)
            var availableItem = pendingPickups.FirstOrDefault(i => i != null && i.IsAvailable);
            if (availableItem != null)
            {
                Debug.Log($"[TaskManager] {unit.UnitName}에게 아이템 수거 작업 할당");
                var pickupTask = new PickupItemTask(availableItem);
                unit.AssignTask(new MoveToTask(availableItem.transform.position));
                unit.AssignTask(pickupTask);
                continue;
            }

            // 4순위: 일반 대기 작업
            if (pendingTasks.Count > 0)
            {
                var task = pendingTasks[0];
                pendingTasks.RemoveAt(0);

                Debug.Log($"[TaskManager] {unit.UnitName}에게 일반 작업 할당: {task.Type}");
                unit.AssignTask(new MoveToTask(task.TargetPosition));
                unit.AssignTask(task);
            }

            // 할당할 작업 없음 - AI가 있으면 자유 행동, 없으면 그냥 대기
        }
    }

    /// <summary>
    /// 할당 가능한 유닛 목록
    /// </summary>
    private List<Unit> GetAvailableUnits()
    {
        var result = new List<Unit>();

        foreach (var unit in registeredUnits)
        {
            if (unit == null || !unit.Stats.IsAlive) continue;
            if (!unit.IsIdle) continue;

            // AI 유닛인 경우
            var ai = unit.GetComponent<UnitAI>();
            if (ai != null)
            {
                // 플레이어 명령이 있으면 스킵
                if (ai.HasPlayerCommand) continue;

                // AI 유닛에게 작업 할당 안 함 옵션
                if (!assignToAIUnits) continue;
            }

            result.Add(unit);
        }

        return result;
    }

    /// <summary>
    /// 작업자 수 제한을 고려해서 건설 가능한 건물 찾기
    /// </summary>
    private Building FindAvailableConstruction()
    {
        foreach (var building in pendingConstructions)
        {
            if (building == null || !building.NeedsConstruction) continue;

            int currentWorkers = buildingWorkerCount.TryGetValue(building, out int count) ? count : 0;
            if (currentWorkers < maxWorkersPerBuilding)
            {
                return building;
            }
        }

        return null;
    }

    /// <summary>
    /// 특정 위치에서 가장 가까운 유휴 유닛 찾기
    /// </summary>
    public Unit FindNearestIdleUnit(Vector3 position)
    {
        Unit nearest = null;
        float nearestDistance = float.MaxValue;

        foreach (var unit in registeredUnits)
        {
            if (!unit.IsIdle) continue;

            float distance = Vector3.Distance(unit.transform.position, position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = unit;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 모든 유휴 유닛 목록
    /// </summary>
    public List<Unit> GetIdleUnits()
    {
        return registeredUnits.Where(u => u != null && u.IsIdle && u.Stats.IsAlive).ToList();
    }

    /// <summary>
    /// 창고 건물 설정
    /// </summary>
    public void SetStorageBuilding(Building storage)
    {
        storageBuilding = storage;
    }

    /// <summary>
    /// 창고 건물 자동 찾기
    /// </summary>
    public void FindStorageBuilding()
    {
        if (storageBuilding != null && storageBuilding.CurrentState == BuildingState.Completed) return;

        var buildings = FindObjectsOfType<Building>();
        foreach (var b in buildings)
        {
            if (b.Data != null && b.Data.Type == BuildingType.Storage && b.CurrentState == BuildingState.Completed)
            {
                storageBuilding = b;
                Debug.Log($"[TaskManager] 창고 자동 발견: {b.Data.Name}");
                break;
            }
        }
    }

    // ===== 이벤트 핸들러 =====

    private void HandleUnitDeath(Unit unit)
    {
        UnregisterUnit(unit);
    }

    private void HandleTaskCompleted(Unit unit, UnitTask task)
    {
        // 건설 작업 완료 시 작업자 수 감소
        if (task is ConstructTask constructTask)
        {
            var building = constructTask.TargetObject?.GetComponent<Building>();
            if (building != null && buildingWorkerCount.ContainsKey(building))
            {
                buildingWorkerCount[building] = Mathf.Max(0, buildingWorkerCount[building] - 1);
            }
        }
    }

    private void HandleConstructionComplete(Building building)
    {
        pendingConstructions.Remove(building);
        buildingWorkerCount.Remove(building);
        building.OnConstructionComplete -= HandleConstructionComplete;

        // 창고가 완성되면 자동 설정
        if (building.Data != null && building.Data.Type == BuildingType.Storage)
        {
            if (storageBuilding == null)
            {
                storageBuilding = building;
                Debug.Log($"[TaskManager] 창고 설정됨: {building.Data.Name}");
            }
        }
    }

    private void HandleItemPickedUp(DroppedItem item)
    {
        pendingPickups.Remove(item);
        item.OnPickedUp -= HandleItemPickedUp;
    }

    // ===== 플레이어 명령 헬퍼 =====

    /// <summary>
    /// 특정 유닛에게 플레이어 명령으로 건설 지시
    /// </summary>
    public void CommandUnitToConstruct(Unit unit, Building building)
    {
        if (unit == null || building == null) return;

        var ai = unit.GetComponent<UnitAI>();
        if (ai != null)
        {
            ai.AddPlayerCommandImmediate(new MoveToTask(building.transform.position));
            ai.AddPlayerCommand(new ConstructTask(building));
        }
        else
        {
            unit.AssignTaskImmediate(new MoveToTask(building.transform.position));
            unit.AssignTask(new ConstructTask(building));
        }

        Debug.Log($"[TaskManager] 플레이어 명령: {unit.UnitName} → {building.Data?.Name} 건설");
    }

    /// <summary>
    /// 특정 유닛에게 플레이어 명령으로 이동 지시
    /// </summary>
    public void CommandUnitToMove(Unit unit, Vector3 position)
    {
        if (unit == null) return;

        var ai = unit.GetComponent<UnitAI>();
        if (ai != null)
        {
            ai.AddPlayerCommandImmediate(new MoveToTask(position, TaskPriority.High));
        }
        else
        {
            unit.AssignTaskImmediate(new MoveToTask(position, TaskPriority.High));
        }

        Debug.Log($"[TaskManager] 플레이어 명령: {unit.UnitName} → 이동 {position}");
    }

    // ===== 디버그 =====

    [ContextMenu("Print Status")]
    public void DebugPrintStatus()
    {
        Debug.Log($"[TaskManager] Units: {registeredUnits.Count}, " +
                  $"Pending Constructions: {pendingConstructions.Count}, " +
                  $"Pending Pickups: {pendingPickups.Count}, " +
                  $"Pending Tasks: {pendingTasks.Count}");
    }
}