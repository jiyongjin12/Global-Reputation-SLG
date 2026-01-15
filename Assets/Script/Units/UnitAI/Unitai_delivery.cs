using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ���� AI - ��� �� ������ ����
/// �� ����� ���/������ �ݱ� �Ϸ� �� ContinuePersistentCommand() ȣ��
/// </summary>
public partial class UnitAI
{
    // ==================== Item Pickup ====================

    public void AddPersonalItem(DroppedItem item)
    {
        if (item == null || personalItems.Contains(item)) return;

        // �ڼ� �������� ���� ����Ʈ��
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
    /// �� ���� ������ �ݱ� - �Ϸ� �� ���� ���� ������
    /// </summary>
    private void UpdatePickingUpPersonalItem()
    {
        // Null üũ
        if (currentPersonalItem == null || !currentPersonalItem || currentPersonalItem.IsBeingMagneted)
        {
            currentPersonalItem = null;
            TryPickupNextOrContinueLoop();
            return;
        }

        // �ִϸ��̼� ���̸� ���
        if (currentPersonalItem.IsAnimating)
        {
            float distToItem = Vector3.Distance(transform.position, currentPersonalItem.transform.position);
            if (distToItem > pickupRadius && unit.HasArrivedAtDestination())
                unit.MoveTo(currentPersonalItem.transform.position);
            pickupTimer = 0f;
            return;
        }

        // ����
        if (!currentPersonalItem.IsReserved)
            currentPersonalItem.Reserve(unit);

        // �Ÿ� üũ
        float dist = Vector3.Distance(transform.position, currentPersonalItem.transform.position);
        if (dist > pickupRadius)
        {
            if (unit.HasArrivedAtDestination())
                unit.MoveTo(currentPersonalItem.transform.position);
            pickupTimer = 0f;
            return;
        }

        // �ݱ� Ÿ�̸�
        pickupTimer += Time.deltaTime;
        if (pickupTimer < itemPickupDuration) return;

        // �ݱ� ����
        var resource = currentPersonalItem.Resource;
        int originalAmount = currentPersonalItem.Amount;

        // �� �κ� ���� üũ - ���� ���� ������
        if (unit.Inventory.IsFull || (resource != null && !unit.Inventory.CanAddAny(resource)))
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: �ݱ� �� �κ� ���� �� ������");
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
            // �Ϻθ� �ֿ����� �κ� ����
            GiveUpRemainingPersonalItems();
            ContinuePersistentCommand();
        }
    }

    /// <summary>
    /// �� ���� ������ �ݱ� �Ǵ� ���� ���� ������
    /// </summary>
    private void TryPickupNextOrContinueLoop()
    {
        CleanupItemLists();
        personalItems.RemoveAll(item => item == null || !item || item.Owner != unit || item.IsBeingMagneted);

        // �� ���� ������ ����?
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

        // �� ���� ������ ������ ���� ���
        if (bb.HasPersistentCommand)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: ������ �ݱ� �Ϸ� �� ���� ���");
            ContinuePersistentCommand();
            return;
        }

        // �Ϲ� ����
        ReturnToIdleOrWork();
    }

    /// <summary>
    /// �Ϲ� ���� (���� ���� ���� ��)
    /// </summary>
    private void ReturnToIdleOrWork()
    {
        pendingMagnetItems.RemoveAll(item => item == null || !item);

        // �κ� �� ���� �������
        if (unit.Inventory.IsFull)
        {
            StartDeliveryToStorage();
            return;
        }

        // ��� �ڼ� ������ ó��
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

        // ���� �۾� ����
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

        // �� �۾� ã��
        if (TryPullTask()) return;

        // �κ� ����
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

        Debug.Log($"[UnitAI] {unit.UnitName}: ������� �̵�");
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

    private void OnDeliveryComplete()
    {
        if (bb.HasPersistentCommand)
        {
            ContinuePersistentCommand();
            return;
        }

        ReturnToIdleOrWork();
    }

    protected void ClearDeliveryState()
    {
        deliveryPhase = DeliveryPhase.None;
        targetStorage = null;
        depositTimer = 0f;
    }

    protected void ReturnToPreviousTaskOrIdle()
    {
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

        // ���ָ� ���¿��� �ָ� ��� �޸���
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
        // IsHungryForFood() 사용 (40% 이하일 때만)
        if (!IsHungryForFood()) return;

        bb.TargetPosition = foodPosition;
        unit.MoveTo(foodPosition);
        SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
    }
}