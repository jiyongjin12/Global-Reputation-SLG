using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 유닛 AI - 배달 및 아이템 관련
/// ★ 저장고 배달/아이템 줍기 완료 시 ContinuePersistentCommand() 호출
/// </summary>
public partial class UnitAI
{
    // ==================== Item Pickup ====================

    public void AddPersonalItem(DroppedItem item)
    {
        if (item == null || personalItems.Contains(item)) return;

        // 자석 아이템은 별도 리스트로
        if (item.EnableMagnet)
        {
            if (!pendingMagnetItems.Contains(item))
            {
                pendingMagnetItems.Add(item);
                item.SetOwner(unit);
            }
            return;
        }

        personalItems.Add(item);
        item.SetOwner(unit);
    }

    public void RemovePersonalItem(DroppedItem item)
    {
        if (item == null) return;
        personalItems.Remove(item);
        if (currentPersonalItem == item) currentPersonalItem = null;
    }

    protected void TryPickupPersonalItems()
    {
        CleanupItemLists();
        personalItems.RemoveAll(item => item == null || !item || item.Owner != unit || item.IsBeingMagneted);

        if (personalItems.Count == 0) return;

        currentPersonalItem = personalItems[0];
        personalItems.RemoveAt(0);

        if (currentPersonalItem == null || !currentPersonalItem || currentPersonalItem.IsBeingMagneted)
        {
            currentPersonalItem = null;
            TryPickupPersonalItems();
            return;
        }

        if (!currentPersonalItem.IsAnimating)
            currentPersonalItem.Reserve(unit);

        unit.MoveTo(currentPersonalItem.transform.position);
        SetBehaviorAndPriority(AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
        pickupTimer = 0f;
    }

    private void UpdatePickingUpItem()
    {
        if (currentPersonalItem != null)
        {
            UpdatePickingUpPersonalItem();
            return;
        }

        if (!taskContext.HasTask)
        {
            CompleteCurrentTask();
            return;
        }

        var item = taskContext.Task.Owner as DroppedItem;
        if (item == null)
        {
            CompleteCurrentTask();
            return;
        }

        float dist = Vector3.Distance(transform.position, item.transform.position);

        if (dist > pickupRadius)
        {
            if (unit.HasArrivedAtDestination())
                unit.MoveTo(item.transform.position);
            pickupTimer = 0f;
            return;
        }

        pickupTimer += Time.deltaTime;

        if (pickupTimer >= itemPickupDuration)
        {
            if (!unit.Inventory.IsFull && item != null)
            {
                unit.Inventory.AddItem(item.Resource, item.Amount);
                item.PickUp(unit);
                unit.OnItemPickedUp();
            }
            CompleteCurrentTask();
            pickupTimer = 0f;
        }
    }

    /// <summary>
    /// ★ 개인 아이템 줍기 - 완료 시 지속 명령 루프로
    /// </summary>
    private void UpdatePickingUpPersonalItem()
    {
        // Null 체크
        if (currentPersonalItem == null || !currentPersonalItem || currentPersonalItem.IsBeingMagneted)
        {
            currentPersonalItem = null;
            TryPickupNextOrContinueLoop();
            return;
        }

        // 애니메이션 중이면 대기
        if (currentPersonalItem.IsAnimating)
        {
            float distToItem = Vector3.Distance(transform.position, currentPersonalItem.transform.position);
            if (distToItem > pickupRadius && unit.HasArrivedAtDestination())
                unit.MoveTo(currentPersonalItem.transform.position);
            pickupTimer = 0f;
            return;
        }

        // 예약
        if (!currentPersonalItem.IsReserved)
            currentPersonalItem.Reserve(unit);

        // 거리 체크
        float dist = Vector3.Distance(transform.position, currentPersonalItem.transform.position);
        if (dist > pickupRadius)
        {
            if (unit.HasArrivedAtDestination())
                unit.MoveTo(currentPersonalItem.transform.position);
            pickupTimer = 0f;
            return;
        }

        // 줍기 타이머
        pickupTimer += Time.deltaTime;
        if (pickupTimer < itemPickupDuration) return;

        // 줍기 실행
        var resource = currentPersonalItem.Resource;
        int originalAmount = currentPersonalItem.Amount;

        // ★ 인벤 가득 체크 - 지속 명령 루프로
        if (unit.Inventory.IsFull || (resource != null && !unit.Inventory.CanAddAny(resource)))
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 줍기 중 인벤 가득 → 루프로");
            GiveUpRemainingPersonalItems();
            ContinuePersistentCommand();
            return;
        }

        int pickedAmount = currentPersonalItem.PickUpPartial(unit);
        pickupTimer = 0f;

        if (pickedAmount > 0) unit.OnItemPickedUp();

        if (pickedAmount >= originalAmount)
        {
            currentPersonalItem = null;
            TryPickupNextOrContinueLoop();
        }
        else
        {
            // 일부만 주웠으면 인벤 가득
            GiveUpRemainingPersonalItems();
            ContinuePersistentCommand();
        }
    }

    /// <summary>
    /// ★ 다음 아이템 줍기 또는 지속 명령 루프로
    /// </summary>
    private void TryPickupNextOrContinueLoop()
    {
        CleanupItemLists();
        personalItems.RemoveAll(item => item == null || !item || item.Owner != unit || item.IsBeingMagneted);

        // 더 줍을 아이템 있음?
        if (personalItems.Count > 0)
        {
            currentPersonalItem = personalItems[0];
            personalItems.RemoveAt(0);

            if (currentPersonalItem != null && currentPersonalItem && !currentPersonalItem.IsBeingMagneted)
            {
                if (!currentPersonalItem.IsAnimating)
                    currentPersonalItem.Reserve(unit);

                unit.MoveTo(currentPersonalItem.transform.position);
                SetBehaviorAndPriority(AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
                pickupTimer = 0f;
                return;
            }
            else
            {
                currentPersonalItem = null;
                TryPickupNextOrContinueLoop();
                return;
            }
        }

        // ★ 지속 명령이 있으면 루프 계속
        if (bb.HasPersistentCommand)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 아이템 줍기 완료 → 루프 계속");
            ContinuePersistentCommand();
            return;
        }

        // 일반 복귀
        ReturnToIdleOrWork();
    }

    /// <summary>
    /// 일반 복귀 (지속 명령 없을 때)
    /// </summary>
    private void ReturnToIdleOrWork()
    {
        pendingMagnetItems.RemoveAll(item => item == null || !item);

        // 인벤 꽉 차면 저장고로
        if (unit.Inventory.IsFull)
        {
            StartDeliveryToStorage();
            return;
        }

        // 대기 자석 아이템 처리
        if (pendingMagnetItems.Count > 0)
        {
            if (HasAbsorbablePendingItems())
            {
                MoveToNearestAbsorbableItem();
                return;
            }
            StartDeliveryToStorage();
            return;
        }

        // 이전 작업 복귀
        if (previousTask != null && TaskManager.Instance != null)
        {
            var task = previousTask;
            previousTask = null;

            if (task.State != PostedTaskState.Completed &&
                task.State != PostedTaskState.Cancelled &&
                TaskManager.Instance.TakeTask(task, unit))
            {
                AssignTask(task);
                return;
            }
        }

        // 새 작업 찾기
        if (TryPullTask()) return;

        // 인벤 비우기
        if (!unit.Inventory.IsEmpty && ShouldDepositWhenIdle())
        {
            StartDeliveryToStorage();
            return;
        }

        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    protected void GiveUpRemainingPersonalItems()
    {
        if (currentPersonalItem != null && currentPersonalItem)
            currentPersonalItem.OwnerGiveUp();
        currentPersonalItem = null;

        foreach (var item in personalItems)
        {
            if (item != null && item) item.OwnerGiveUp();
        }
        personalItems.Clear();
    }

    // ==================== Magnet Absorption ====================

    protected void TryAbsorbNearbyMagnetItems()
    {
        magnetAbsorbTimer += Time.deltaTime;
        if (magnetAbsorbTimer < MAGNET_ABSORB_INTERVAL) return;
        magnetAbsorbTimer = 0f;

        pendingMagnetItems.RemoveAll(item => item == null || !item);

        if (unit.Inventory.IsFull) return;

        var virtualSpaceMap = new Dictionary<ResourceItemSO, int>();
        var itemsToAbsorb = new List<DroppedItem>();

        for (int i = pendingMagnetItems.Count - 1; i >= 0; i--)
        {
            var item = pendingMagnetItems[i];
            if (item == null || !item || item.IsBeingMagneted || item.IsAnimating)
            {
                if (item == null || !item || item.IsBeingMagneted)
                    pendingMagnetItems.RemoveAt(i);
                continue;
            }

            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist > magnetAbsorbRadius) continue;

            var resource = item.Resource;
            if (resource == null)
            {
                pendingMagnetItems.RemoveAt(i);
                continue;
            }

            if (!virtualSpaceMap.ContainsKey(resource))
                virtualSpaceMap[resource] = unit.Inventory.GetAvailableSpaceFor(resource);

            int availableSpace = virtualSpaceMap[resource];
            int itemAmount = item.Amount;

            if (availableSpace >= itemAmount)
            {
                virtualSpaceMap[resource] -= itemAmount;
                itemsToAbsorb.Add(item);
                pendingMagnetItems.RemoveAt(i);
            }
        }

        foreach (var item in itemsToAbsorb)
        {
            item.PlayAbsorbAnimation(unit, (res, amt) =>
            {
                unit.Inventory.AddItem(res, amt);
                unit.OnItemPickedUp();
            });
        }
    }

    protected bool HasAbsorbablePendingItems()
    {
        foreach (var item in pendingMagnetItems)
        {
            if (item == null || !item || item.Resource == null) continue;
            if (unit.Inventory.GetAvailableSpaceFor(item.Resource) >= item.Amount)
                return true;
        }
        return false;
    }

    protected void MoveToNearestAbsorbableItem()
    {
        DroppedItem nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var item in pendingMagnetItems)
        {
            if (item == null || !item || item.Resource == null) continue;
            if (unit.Inventory.GetAvailableSpaceFor(item.Resource) < item.Amount) continue;

            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = item;
            }
        }

        if (nearest != null)
        {
            unit.MoveTo(nearest.transform.position);
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        }
    }

    // ==================== Storage Delivery ====================

    protected void StartDeliveryToStorage()
    {
        targetStorage = FindNearestStorageDirectly();

        if (targetStorage == null)
        {
            SetBehaviorAndPriority(AIBehaviorState.WaitingForStorage, TaskPriorityLevel.FreeWill);
            return;
        }

        storagePosition = targetStorage.GetNearestAccessPoint(transform.position);
        deliveryPhase = DeliveryPhase.MovingToStorage;
        depositTimer = 0f;

        unit.MoveTo(storagePosition);
        SetBehaviorAndPriority(AIBehaviorState.DeliveringToStorage, TaskPriorityLevel.ItemPickup);

        Debug.Log($"[UnitAI] {unit.UnitName}: 저장고로 이동");
    }

    protected void UpdateWaitingForStorage()
    {
        if (Time.time - lastStorageCheckTime < 1f) return;
        lastStorageCheckTime = Time.time;

        if (TryPullConstructionTask()) return;

        targetStorage = FindNearestStorageDirectly();
        if (targetStorage != null)
        {
            storagePosition = targetStorage.GetNearestAccessPoint(transform.position);
            deliveryPhase = DeliveryPhase.MovingToStorage;
            depositTimer = 0f;
            unit.MoveTo(storagePosition);
            SetBehaviorAndPriority(AIBehaviorState.DeliveringToStorage, TaskPriorityLevel.ItemPickup);
        }
    }

    private void UpdateDeliveryToStorage()
    {
        switch (deliveryPhase)
        {
            case DeliveryPhase.MovingToStorage:
                UpdateMovingToStorage();
                break;
            case DeliveryPhase.Depositing:
                UpdateDepositing();
                break;
            default:
                ClearDeliveryState();
                SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
                break;
        }
    }

    private void UpdateMovingToStorage()
    {
        if (targetStorage == null)
        {
            unit.Inventory.DepositToStorage();
            unit.OnDeliveryComplete();
            ClearDeliveryState();
            OnDeliveryComplete();
            return;
        }

        bool isInRange = targetStorage.IsInAccessArea(transform.position);

        if (isInRange || unit.HasArrivedAtDestination())
        {
            deliveryPhase = DeliveryPhase.Depositing;
            depositTimer = 0f;
            unit.StopMoving();
            return;
        }

        // 도착했는데 범위 밖이면 다시 이동
        if (unit.HasArrivedAtDestination() && !isInRange)
        {
            unit.MoveTo(storagePosition);
        }
    }

    private void UpdateDepositing()
    {
        depositTimer += Time.deltaTime;

        if (depositTimer >= depositDuration)
        {
            PerformDeposit();
            unit.OnDeliveryComplete();
            ClearDeliveryState();
            OnDeliveryComplete();
        }
    }

    private void PerformDeposit()
    {
        if (targetStorage != null && targetStorage.IsMainStorage)
        {
            unit.Inventory.DepositToStorage();
        }
        else if (targetStorage != null)
        {
            foreach (var slot in unit.Inventory.Slots)
            {
                if (!slot.IsEmpty)
                    targetStorage.AddItem(slot.Resource, slot.Amount);
            }
            unit.Inventory.Clear();
        }
        else
        {
            unit.Inventory.DepositToStorage();
        }
    }

    /// <summary>
    /// ★ 배달 완료 시 지속 명령 루프로
    /// </summary>
    private void OnDeliveryComplete()
    {
        // ★ 지속 명령이 있으면 루프 계속
        if (bb.HasPersistentCommand)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 저장 완료 → 루프 계속");
            ContinuePersistentCommand();
            return;
        }

        // 일반 복귀
        ReturnToIdleOrWork();
    }

    protected void ClearDeliveryState()
    {
        deliveryPhase = DeliveryPhase.None;
        targetStorage = null;
        depositTimer = 0f;
    }

    /// <summary>
    /// 기존 호환용 (지속 명령 없는 경우에만 사용)
    /// </summary>
    protected void ReturnToPreviousTaskOrIdle()
    {
        // ★ 지속 명령이 있으면 루프 계속
        if (bb.HasPersistentCommand)
        {
            ContinuePersistentCommand();
            return;
        }

        ReturnToIdleOrWork();
    }

    // ==================== Storage Helpers ====================

    private StorageComponent FindNearestStorageDirectly()
    {
        var storages = FindObjectsOfType<StorageComponent>();
        StorageComponent nearest = null;
        float nearestDist = storageSearchRadius;

        foreach (var storage in storages)
        {
            var building = storage.GetComponent<Building>();
            if (building != null && building.CurrentState != BuildingState.Completed)
                continue;

            float dist = Vector3.Distance(transform.position, storage.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = storage;
            }
        }

        hasStorageBuilding = nearest != null;
        return nearest;
    }

    private StorageComponent FindNearestStorage()
    {
        if (BuildingManager.Instance != null)
        {
            var storage = BuildingManager.Instance.GetNearestStorage(transform.position, mustHaveSpace: false);
            if (storage != null) return storage.GetComponent<StorageComponent>();
        }
        return FindNearestStorageDirectly();
    }

    protected bool ShouldDepositInventory(ResourceItemSO resourceToAdd = null)
    {
        if (unit.Inventory.IsEmpty) return false;

        bool allSlotsUsed = unit.Inventory.UsedSlots >= unit.Inventory.MaxSlots;
        if (!allSlotsUsed) return false;

        if (resourceToAdd != null)
            return !unit.Inventory.CanAddAny(resourceToAdd);

        return unit.Inventory.IsFull;
    }

    protected bool ShouldDepositWhenIdle()
    {
        if (unit.Inventory.IsEmpty) return false;
        if (FindNearestStorage() == null) return false;

        if (TaskManager.Instance != null)
        {
            var availableTask = TaskManager.Instance.FindNearestTask(unit);
            if (availableTask != null)
            {
                if (unit.Inventory.IsFull && availableTask.Data.Type == TaskType.Harvest)
                    return true;
                return false;
            }
        }

        return true;
    }

    // ==================== Food Seeking ====================

    protected bool TrySeekFood()
    {
        var food = FindNearestFood();
        if (food == null) return false;

        bb.NearestFood = food;
        bb.TargetPosition = food.transform.position;

        // 굶주림 상태면 달리기
        if (bb.IsStarving)
            unit.RunTo(food.transform.position);
        else
            unit.MoveTo(food.transform.position);

        return true;
    }

    private void UpdateSeekingFood()
    {
        if (bb.NearestFood == null)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        float dist = Vector3.Distance(transform.position, bb.NearestFood.transform.position);

        // 굶주림 상태에서 멀면 계속 달리기
        if (bb.IsStarving && dist > 3f)
        {
            unit.RunTo(bb.NearestFood.transform.position);
        }

        if (dist < 1.5f)
        {
            var food = bb.NearestFood;
            if (food.Resource != null && food.Resource.IsFood)
            {
                bb.Eat(food.Resource.NutritionValue * food.Amount);
                unit.Heal(food.Resource.HealthRestore * food.Amount);
                food.PickUp(unit);
            }
            bb.NearestFood = null;
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        }
    }

    private DroppedItem FindNearestFood()
    {
        var items = FindObjectsOfType<DroppedItem>();
        DroppedItem nearest = null;
        float nearestDist = foodSearchRadius;

        foreach (var item in items)
        {
            if (!item.IsAvailable || item.Resource == null || !item.Resource.IsFood)
                continue;

            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = item;
            }
        }
        return nearest;
    }

    public void SetFoodTarget(Vector3 foodPosition)
    {
        if (bb.Hunger > hungerSeekThreshold) return;

        bb.TargetPosition = foodPosition;
        unit.MoveTo(foodPosition);
        SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
    }
}