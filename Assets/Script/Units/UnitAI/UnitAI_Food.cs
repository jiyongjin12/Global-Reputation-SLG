using UnityEngine;

/// <summary>
/// 유닛 AI - 음식/배고픔 관련
/// - 배고프면 음식 찾기 (저장고 우선 → 바닥 음식)
/// - 한 번에 하나씩만 먹기 (독식 방지)
/// - 저장고에서 음식 꺼내 먹기
/// </summary>
public partial class UnitAI
{
    // 음식 관련 상태
    private StorageComponent targetFoodStorage;
    private bool isEatingFromStorage;
    private float eatTimer;
    private const float EAT_DURATION = 1f;

    // 현재 타겟 음식 (바닥)
    private DroppedItem targetFoodItem;

    /// <summary>
    /// 음식 찾기 시도 (저장고 우선 → 바닥 음식)
    /// </summary>
    protected bool TrySeekFoodNew()
    {
        // 1. 저장고에 음식 있는지 확인
        var storageWithFood = FindStorageWithFood();
        if (storageWithFood != null)
        {
            StartSeekingFoodFromStorage(storageWithFood);
            return true;
        }

        // 2. 바닥에 음식 있는지 확인
        var groundFood = FindNearestAvailableFood();
        if (groundFood != null)
        {
            StartSeekingFoodFromGround(groundFood);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 저장고에서 음식 가져오기 시작
    /// </summary>
    private void StartSeekingFoodFromStorage(StorageComponent storage)
    {
        targetFoodStorage = storage;
        isEatingFromStorage = true;
        targetFoodItem = null;

        Vector3 accessPoint = storage.GetNearestAccessPoint(transform.position);

        if (bb.IsStarving)
            unit.RunTo(accessPoint);
        else
            unit.MoveTo(accessPoint);

        SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
        Debug.Log($"[UnitAI] {unit.UnitName}: 저장고에서 음식 가져오러 이동");
    }

    /// <summary>
    /// 바닥 음식 줍기 시작
    /// </summary>
    private void StartSeekingFoodFromGround(DroppedItem food)
    {
        targetFoodItem = food;
        targetFoodStorage = null;
        isEatingFromStorage = false;

        // 예약 (다른 유닛이 못 가져가게)
        food.Reserve(unit);

        bb.NearestFood = food;
        bb.TargetPosition = food.transform.position;

        if (bb.IsStarving)
            unit.RunTo(food.transform.position);
        else
            unit.MoveTo(food.transform.position);

        SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
        Debug.Log($"[UnitAI] {unit.UnitName}: 바닥 음식 주우러 이동 ({food.Resource?.ResourceName})");
    }

    /// <summary>
    /// 음식 찾기 업데이트 (개선된 버전)
    /// </summary>
    protected void UpdateSeekingFoodNew()
    {
        if (isEatingFromStorage)
        {
            UpdateSeekingFoodFromStorage();
        }
        else
        {
            UpdateSeekingFoodFromGround();
        }
    }

    /// <summary>
    /// 저장고에서 음식 가져오기 업데이트
    /// </summary>
    private void UpdateSeekingFoodFromStorage()
    {
        if (targetFoodStorage == null)
        {
            // 저장고 없어짐 → 바닥 음식 찾기
            if (!TrySeekFoodNew())
            {
                SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            }
            return;
        }

        // 저장고 범위 내인지 확인
        bool inRange = targetFoodStorage.IsInAccessArea(transform.position);

        if (!inRange)
        {
            // 도착했는데 범위 밖이면 다시 이동
            if (unit.HasArrivedAtDestination())
            {
                Vector3 accessPoint = targetFoodStorage.GetNearestAccessPoint(transform.position);
                unit.MoveTo(accessPoint);
            }
            eatTimer = 0f;
            return;
        }

        // 범위 내 도착 → 음식 꺼내 먹기
        eatTimer += Time.deltaTime;
        if (eatTimer >= EAT_DURATION)
        {
            TakeAndEatFromStorage();
            eatTimer = 0f;
        }
    }

    /// <summary>
    /// 저장고에서 음식 꺼내 먹기
    /// </summary>
    private void TakeAndEatFromStorage()
    {
        if (targetFoodStorage == null) return;

        var foodItem = targetFoodStorage.TakeFoodItem(1);
        if (foodItem != null)
        {
            // 먹기
            float nutrition = foodItem.NutritionValue;
            float health = foodItem.HealthRestore;

            bb.Eat(nutrition);
            unit.Heal(health);

            Debug.Log($"[UnitAI] {unit.UnitName}: 저장고에서 {foodItem.ResourceName} 먹음 (허기 +{nutrition})");

            // 아직 배고프면 계속
            if (bb.Hunger <= hungerSeekThreshold)
            {
                // 저장고에 음식 더 있으면 계속 먹기
                if (targetFoodStorage.GetFoodItem() != null)
                {
                    eatTimer = 0f;
                    return;
                }
                // 없으면 다른 음식 찾기
                if (TrySeekFoodNew()) return;
            }
        }
        else
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 저장고에 음식 없음");
            // 바닥 음식 찾기
            if (bb.Hunger <= hungerSeekThreshold && TrySeekFoodNew()) return;
        }

        // 배 부르거나 음식 없음 → Idle
        targetFoodStorage = null;
        isEatingFromStorage = false;
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    /// <summary>
    /// 바닥 음식 줍기 업데이트
    /// </summary>
    private void UpdateSeekingFoodFromGround()
    {
        // 음식 유효성 체크
        if (targetFoodItem == null || !targetFoodItem || !targetFoodItem.CanBePickedUpBy(unit))
        {
            targetFoodItem = null;
            bb.NearestFood = null;

            // 다른 음식 찾기
            if (bb.Hunger <= hungerSeekThreshold && TrySeekFoodNew()) return;

            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        float dist = Vector3.Distance(transform.position, targetFoodItem.transform.position);

        // 굶주림 상태면 계속 달리기
        if (bb.IsStarving && dist > 3f)
        {
            unit.RunTo(targetFoodItem.transform.position);
        }

        // 도착
        if (dist < 1.5f)
        {
            EatGroundFood();
        }
    }

    /// <summary>
    /// 바닥 음식 먹기
    /// </summary>
    private void EatGroundFood()
    {
        if (targetFoodItem == null || !targetFoodItem) return;

        var resource = targetFoodItem.Resource;
        if (resource != null && resource.IsFood)
        {
            // 1개만 먹기
            int amountToEat = 1;
            float nutrition = resource.NutritionValue * amountToEat;
            float health = resource.HealthRestore * amountToEat;

            bb.Eat(nutrition);
            unit.Heal(health);

            Debug.Log($"[UnitAI] {unit.UnitName}: 바닥 음식 {resource.ResourceName} 먹음 (허기 +{nutrition})");

            // 아이템 수량 감소
            if (targetFoodItem.Amount <= amountToEat)
            {
                targetFoodItem.PickUp(unit);
            }
            else
            {
                // 부분 소비 (DroppedItem에 수량 감소 메서드 필요)
                targetFoodItem.ConsumeAmount(amountToEat);
            }
        }

        targetFoodItem = null;
        bb.NearestFood = null;

        // 아직 배고프면 다시 찾기
        if (bb.Hunger <= hungerSeekThreshold)
        {
            if (TrySeekFoodNew()) return;
        }

        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    /// <summary>
    /// 음식 있는 저장고 찾기
    /// </summary>
    private StorageComponent FindStorageWithFood()
    {
        var storages = FindObjectsOfType<StorageComponent>();
        StorageComponent nearest = null;
        float nearestDist = foodSearchRadius;

        foreach (var storage in storages)
        {
            // 건물 완성 체크
            var building = storage.GetComponent<Building>();
            if (building != null && building.CurrentState != BuildingState.Completed)
                continue;

            // 음식 있는지 체크
            if (storage.GetFoodItem() == null)
                continue;

            float dist = Vector3.Distance(transform.position, storage.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = storage;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 가장 가까운 이용 가능한 바닥 음식 찾기
    /// </summary>
    private DroppedItem FindNearestAvailableFood()
    {
        var items = FindObjectsOfType<DroppedItem>();
        DroppedItem nearest = null;
        float nearestDist = foodSearchRadius;

        foreach (var item in items)
        {
            // 사용 가능 & 음식 & 예약 안 됨 (또는 내가 예약)
            if (!item.CanBePickedUpBy(unit))
                continue;

            if (item.Resource == null || !item.Resource.IsFood)
                continue;

            // 이미 다른 유닛이 먹으러 가는 중이면 스킵
            if (item.IsReserved && item.ReservedBy != unit)
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

    /// <summary>
    /// 음식 찾기 취소 (상태 정리)
    /// </summary>
    protected void CancelSeekingFood()
    {
        if (targetFoodItem != null && targetFoodItem.ReservedBy == unit)
        {
            targetFoodItem.CancelReservation();
        }

        targetFoodItem = null;
        targetFoodStorage = null;
        isEatingFromStorage = false;
        bb.NearestFood = null;
        eatTimer = 0f;
    }
}