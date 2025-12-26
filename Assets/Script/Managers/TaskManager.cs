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

    // 수거 대기 중인 드롭 아이템들
    private List<DroppedItem> pendingPickups = new List<DroppedItem>();

    // 창고 건물 참조
    [SerializeField] private Building storageBuilding;

    [Header("Settings")]
    [SerializeField] private float taskAssignInterval = 0.5f; // 작업 할당 주기

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
            AssignPendingTasks();
            lastAssignTime = Time.time;
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
        }
    }

    /// <summary>
    /// 유닛 등록 해제
    /// </summary>
    public void UnregisterUnit(Unit unit)
    {
        registeredUnits.Remove(unit);
        unit.OnUnitDeath -= HandleUnitDeath;
    }

    /// <summary>
    /// 건설 작업 추가
    /// </summary>
    public void AddConstructionTask(Building building)
    {
        if (!pendingConstructions.Contains(building))
        {
            pendingConstructions.Add(building);
            building.OnConstructionComplete += HandleConstructionComplete;
        }
    }

    /// <summary>
    /// 아이템 수거 작업 추가
    /// </summary>
    public void AddPickupTask(DroppedItem item)
    {
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
        // 유휴 유닛 찾기
        var idleUnits = registeredUnits.Where(u => u.IsIdle && u.Stats.IsAlive).ToList();

        if (idleUnits.Count == 0) return;

        foreach (var unit in idleUnits)
        {
            // 1순위: 인벤토리가 차 있으면 창고로 배달
            if (!unit.Inventory.IsEmpty && storageBuilding != null)
            {
                var deliverTask = new DeliverToStorageTask(storageBuilding);
                unit.AssignTask(new MoveToTask(storageBuilding.transform.position));
                unit.AssignTask(deliverTask);
                continue;
            }

            // 2순위: 건설 작업
            var pendingBuilding = pendingConstructions.FirstOrDefault(b => b.NeedsConstruction);
            if (pendingBuilding != null)
            {
                var moveTask = new MoveToTask(pendingBuilding.transform.position);
                var constructTask = new ConstructTask(pendingBuilding);
                unit.AssignTask(moveTask);
                unit.AssignTask(constructTask);
                continue;
            }

            // 3순위: 아이템 수거
            var availableItem = pendingPickups.FirstOrDefault(i => i != null && i.IsAvailable);
            if (availableItem != null)
            {
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

                // 이동 작업 추가
                unit.AssignTask(new MoveToTask(task.TargetPosition));
                unit.AssignTask(task);
            }
        }
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
    /// 창고 건물 설정
    /// </summary>
    public void SetStorageBuilding(Building storage)
    {
        storageBuilding = storage;
    }

    private void HandleUnitDeath(Unit unit)
    {
        UnregisterUnit(unit);
    }

    private void HandleConstructionComplete(Building building)
    {
        pendingConstructions.Remove(building);
        building.OnConstructionComplete -= HandleConstructionComplete;
    }

    private void HandleItemPickedUp(DroppedItem item)
    {
        pendingPickups.Remove(item);
        item.OnPickedUp -= HandleItemPickedUp;
    }

    /// <summary>
    /// 디버그: 현재 상태 출력
    /// </summary>
    public void DebugPrintStatus()
    {
        Debug.Log($"[TaskManager] Units: {registeredUnits.Count}, " +
                  $"Pending Constructions: {pendingConstructions.Count}, " +
                  $"Pending Pickups: {pendingPickups.Count}, " +
                  $"Pending Tasks: {pendingTasks.Count}");
    }
}