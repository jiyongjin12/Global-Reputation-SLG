using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 유닛 AI - 음식/배고픔 관련
/// 
/// ★ 배고픔 조건:
/// - 40% 이하: 밥 찾으러 이동 시작
/// - 70% 이상: 배부름 (먹기 중단)
/// 
/// ★ 우선순위:
/// 1. 저장고에서 음식 꺼내먹기
/// 2. 바닥에 떨어진 음식 먹기
/// </summary>
public partial class UnitAI
{
    // ==================== 음식 관련 상수 ====================

    /// <summary>이 값 이하면 밥 찾기 시작 (40%)</summary>
    private const float HUNGER_SEEK_THRESHOLD = 40f;

    /// <summary>이 값 이상이면 배부름 (70%)</summary>
    private const float HUNGER_SATISFIED_THRESHOLD = 70f;

    /// <summary>저장고 접근 거리</summary>
    private const float STORAGE_ACCESS_DISTANCE = 5f;

    /// <summary>바닥 음식 줍기 거리</summary>
    private const float FOOD_PICKUP_DISTANCE = 1.5f;

    /// <summary>먹는 시간</summary>
    private const float EAT_DURATION = 0.8f;

    // ==================== 음식 관련 상태 ====================

    private StorageComponent targetFoodStorage;
    private DroppedItem targetFoodItem;
    private bool isEatingFromStorage;
    private float eatTimer;
    private int foodSearchAttempts = 0;
    private const int MAX_FOOD_SEARCH_ATTEMPTS = 5;

    // ==================== 메인 로직 ====================

    /// <summary>
    /// 배고픈지 체크 (40% 이하)
    /// </summary>
    public bool IsHungryForFood()
    {
        return unit.Hunger <= HUNGER_SEEK_THRESHOLD;
    }

    /// <summary>
    /// 배부른지 체크 (70% 이상)
    /// </summary>
    public bool IsSatisfied()
    {
        return unit.Hunger >= HUNGER_SATISFIED_THRESHOLD;
    }

    /// <summary>
    /// 음식 찾기 시도
    /// </summary>
    protected bool TrySeekFoodNew()
    {
        // 이미 배부르면 찾지 않음
        if (IsSatisfied())
        {
            Debug.Log($"[Food] {unit.UnitName}: 이미 배부름 ({unit.Hunger:F0}%)");
            return false;
        }

        Debug.Log($"<color=yellow>[Food] {unit.UnitName}: ===== 음식 찾기 시작 ===== (허기: {unit.Hunger:F0}%)</color>");

        // 1. 저장고에서 음식 찾기
        StorageComponent storageWithFood = FindStorageWithFood();
        if (storageWithFood != null)
        {
            Debug.Log($"<color=green>[Food] {unit.UnitName}: 저장고 발견! → {storageWithFood.name}</color>");
            StartEatingFromStorage(storageWithFood);
            return true;
        }

        // 2. 바닥 음식 찾기
        DroppedItem groundFood = FindGroundFood();
        if (groundFood != null)
        {
            Debug.Log($"<color=green>[Food] {unit.UnitName}: 바닥 음식 발견! → {groundFood.Resource?.ResourceName}</color>");
            StartEatingFromGround(groundFood);
            return true;
        }

        Debug.Log($"<color=red>[Food] {unit.UnitName}: 음식을 찾지 못함!</color>");
        return false;
    }

    // ==================== 저장고 음식 ====================

    /// <summary>
    /// 음식이 있는 저장고 찾기
    /// </summary>
    private StorageComponent FindStorageWithFood()
    {
        // 씬의 모든 StorageComponent 찾기
        StorageComponent[] allStorages = GameObject.FindObjectsOfType<StorageComponent>();

        Debug.Log($"[Food] 저장고 검색 중... (총 {allStorages?.Length ?? 0}개)");

        if (allStorages == null || allStorages.Length == 0)
        {
            Debug.Log($"[Food] 저장고가 없음!");
            return null;
        }

        StorageComponent nearestStorage = null;
        float nearestDistance = foodSearchRadius;

        foreach (var storage in allStorages)
        {
            if (storage == null) continue;

            // 건물 상태 체크
            Building building = storage.GetComponent<Building>();
            if (building != null && building.CurrentState != BuildingState.Completed)
            {
                Debug.Log($"[Food] {storage.name}: 건설 미완료");
                continue;
            }

            // 음식 체크 - GetFoodItem() 사용
            StoredResource foodResource = storage.GetFoodItem();

            if (foodResource == null)
            {
                Debug.Log($"[Food] {storage.name}: GetFoodItem() = null");
                continue;
            }

            if (foodResource.Item == null)
            {
                Debug.Log($"[Food] {storage.name}: foodResource.Item = null");
                continue;
            }

            if (foodResource.Amount <= 0)
            {
                Debug.Log($"[Food] {storage.name}: 수량 0");
                continue;
            }

            // 음식 발견!
            float distance = Vector3.Distance(transform.position, storage.transform.position);
            Debug.Log($"<color=cyan>[Food] {storage.name}: 음식 있음! ({foodResource.Item.ResourceName} x{foodResource.Amount}) 거리={distance:F1}</color>");

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestStorage = storage;
            }
        }

        return nearestStorage;
    }

    /// <summary>
    /// 저장고에서 먹기 시작
    /// </summary>
    private void StartEatingFromStorage(StorageComponent storage)
    {
        targetFoodStorage = storage;
        targetFoodItem = null;
        isEatingFromStorage = true;
        eatTimer = 0f;
        foodSearchAttempts = 0;

        // 저장고로 이동
        Vector3 targetPos = storage.GetNearestAccessPoint(transform.position);

        Debug.Log($"[Food] {unit.UnitName}: 저장고로 이동 → {targetPos}");

        if (unit.IsStarving)
            unit.RunTo(targetPos);
        else
            unit.MoveTo(targetPos);

        SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
    }

    /// <summary>
    /// 저장고에서 먹기 업데이트
    /// </summary>
    private void UpdateEatingFromStorage()
    {
        // 저장고 유효성 체크
        if (targetFoodStorage == null)
        {
            Debug.Log($"[Food] {unit.UnitName}: 저장고가 사라짐, 다시 찾기...");
            RetryFoodSearch();
            return;
        }

        // 배부르면 중단
        if (IsSatisfied())
        {
            Debug.Log($"<color=cyan>[Food] {unit.UnitName}: 배부름! ({unit.Hunger:F0}%) 먹기 중단</color>");
            FinishEating();
            return;
        }

        // 거리 체크
        float distance = Vector3.Distance(transform.position, targetFoodStorage.transform.position);

        // 저장고 범위 체크 (여러 방법)
        bool inRange = false;

        // 방법 1: 직접 거리 체크 (가장 신뢰성 높음)
        if (distance <= STORAGE_ACCESS_DISTANCE)
        {
            inRange = true;
        }

        // 방법 2: IsInAccessArea
        if (!inRange)
        {
            try { inRange = targetFoodStorage.IsInAccessArea(transform.position); }
            catch { }
        }

        if (!inRange)
        {
            // 아직 도착 안 함 - 계속 이동
            if (unit.HasArrivedAtDestination())
            {
                // 도착했는데 범위 밖이면 더 가까이
                Debug.Log($"[Food] {unit.UnitName}: 저장고 더 가까이 이동 (거리={distance:F1})");
                Vector3 closerPos = Vector3.MoveTowards(transform.position, targetFoodStorage.transform.position, distance - 1f);
                unit.MoveTo(closerPos);
            }
            return;
        }

        // 범위 내 도착 - 이동 중지하고 먹기
        unit.StopMoving();

        eatTimer += Time.deltaTime;
        if (eatTimer >= EAT_DURATION)
        {
            EatFromStorage();
            eatTimer = 0f;
        }
    }

    /// <summary>
    /// 저장고에서 음식 꺼내 먹기
    /// </summary>
    private void EatFromStorage()
    {
        if (targetFoodStorage == null)
        {
            RetryFoodSearch();
            return;
        }

        // 음식 꺼내기
        ResourceItemSO foodItem = targetFoodStorage.TakeFoodItem(1);

        if (foodItem == null)
        {
            Debug.Log($"[Food] {unit.UnitName}: 저장고에서 음식 꺼내기 실패!");
            RetryFoodSearch();
            return;
        }

        // 먹기!
        float nutrition = foodItem.NutritionValue;
        float health = foodItem.HealthRestore;

        unit.Eat(nutrition);
        if (health > 0) unit.Heal(health);

        Debug.Log($"<color=cyan>[Food] {unit.UnitName}: {foodItem.ResourceName} 먹음! (영양: +{nutrition:F0}, 허기: {unit.Hunger:F0}%)</color>");

        // 아직 배고프면 계속 먹기
        if (!IsSatisfied())
        {
            // 저장고에 음식이 더 있는지 확인
            StoredResource moreFood = targetFoodStorage.GetFoodItem();
            if (moreFood != null && moreFood.Item != null && moreFood.Amount > 0)
            {
                Debug.Log($"[Food] {unit.UnitName}: 아직 배고픔, 계속 먹기... (허기: {unit.Hunger:F0}%)");
                return; // 계속 먹기
            }

            // 저장고에 음식 없으면 다른 곳 찾기
            Debug.Log($"[Food] {unit.UnitName}: 저장고 음식 소진, 다른 음식 찾기...");
            targetFoodStorage = null;
            TrySeekFoodNew();
            return;
        }

        // 배부르면 완료
        FinishEating();
    }

    // ==================== 바닥 음식 ====================

    /// <summary>
    /// 바닥에 떨어진 음식 찾기
    /// </summary>
    private DroppedItem FindGroundFood()
    {
        DroppedItem[] allItems = GameObject.FindObjectsOfType<DroppedItem>();

        Debug.Log($"[Food] 바닥 아이템 검색 중... (총 {allItems?.Length ?? 0}개)");

        if (allItems == null || allItems.Length == 0)
        {
            Debug.Log($"[Food] 바닥에 아이템 없음!");
            return null;
        }

        DroppedItem nearestFood = null;
        float nearestDistance = foodSearchRadius;

        foreach (var item in allItems)
        {
            if (item == null) continue;

            // 유효성 체크
            if (item.IsBeingCarried || item.IsBeingMagneted)
            {
                continue;
            }

            // 리소스 체크
            ResourceItemSO resource = item.Resource;
            if (resource == null)
            {
                continue;
            }

            // 음식인지 체크
            if (!resource.IsFood)
            {
                continue;
            }

            // 예약 체크
            if (item.IsReserved && item.ReservedBy != unit)
            {
                Debug.Log($"[Food] {resource.ResourceName}: 다른 유닛이 예약함");
                continue;
            }

            // 애니메이션 중이면 스킵
            if (item.IsAnimating)
            {
                continue;
            }

            // 거리 체크
            float distance = Vector3.Distance(transform.position, item.transform.position);

            Debug.Log($"<color=cyan>[Food] 바닥 음식 발견: {resource.ResourceName} (거리={distance:F1})</color>");

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestFood = item;
            }
        }

        return nearestFood;
    }

    /// <summary>
    /// 바닥 음식 먹기 시작
    /// </summary>
    private void StartEatingFromGround(DroppedItem food)
    {
        targetFoodItem = food;
        targetFoodStorage = null;
        isEatingFromStorage = false;
        eatTimer = 0f;
        foodSearchAttempts = 0;

        // 예약
        if (!food.IsReserved)
        {
            food.Reserve(unit);
        }

        bb.NearestFood = food;

        // 음식으로 이동
        Debug.Log($"[Food] {unit.UnitName}: 바닥 음식으로 이동 → {food.transform.position}");

        if (unit.IsStarving)
            unit.RunTo(food.transform.position);
        else
            unit.MoveTo(food.transform.position);

        SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
    }

    /// <summary>
    /// 바닥 음식 먹기 업데이트
    /// </summary>
    private void UpdateEatingFromGround()
    {
        // 음식 유효성 체크
        if (targetFoodItem == null || targetFoodItem.gameObject == null || targetFoodItem.IsBeingCarried)
        {
            Debug.Log($"[Food] {unit.UnitName}: 바닥 음식이 사라짐, 다시 찾기...");
            RetryFoodSearch();
            return;
        }

        // 배부르면 중단
        if (IsSatisfied())
        {
            Debug.Log($"<color=cyan>[Food] {unit.UnitName}: 배부름! ({unit.Hunger:F0}%) 먹기 중단</color>");
            if (targetFoodItem.ReservedBy == unit)
                targetFoodItem.CancelReservation();
            FinishEating();
            return;
        }

        // 다른 유닛이 예약했으면 다시 찾기
        if (targetFoodItem.IsReserved && targetFoodItem.ReservedBy != unit)
        {
            Debug.Log($"[Food] {unit.UnitName}: 음식이 다른 유닛에게 예약됨, 다시 찾기...");
            RetryFoodSearch();
            return;
        }

        // 거리 체크
        float distance = Vector3.Distance(transform.position, targetFoodItem.transform.position);

        if (distance > FOOD_PICKUP_DISTANCE)
        {
            // 아직 도착 안 함 - 계속 이동
            if (unit.HasArrivedAtDestination())
            {
                // 목표 위치 갱신
                unit.MoveTo(targetFoodItem.transform.position);
            }
            return;
        }

        // 도착 - 먹기!
        EatFromGround();
    }

    /// <summary>
    /// 바닥 음식 먹기
    /// </summary>
    private void EatFromGround()
    {
        if (targetFoodItem == null || targetFoodItem.gameObject == null)
        {
            RetryFoodSearch();
            return;
        }

        ResourceItemSO resource = targetFoodItem.Resource;
        if (resource == null || !resource.IsFood)
        {
            RetryFoodSearch();
            return;
        }

        // 먹기!
        float nutrition = resource.NutritionValue;
        float health = resource.HealthRestore;

        unit.Eat(nutrition);
        if (health > 0) unit.Heal(health);

        Debug.Log($"<color=cyan>[Food] {unit.UnitName}: 바닥 {resource.ResourceName} 먹음! (영양: +{nutrition:F0}, 허기: {unit.Hunger:F0}%)</color>");

        // 아이템 소비
        if (targetFoodItem.Amount <= 1)
        {
            targetFoodItem.PickUp(unit);
        }
        else
        {
            targetFoodItem.ConsumeAmount(1);
        }

        // 예약 해제
        targetFoodItem = null;
        bb.NearestFood = null;

        // 아직 배고프면 다시 찾기
        if (!IsSatisfied())
        {
            Debug.Log($"[Food] {unit.UnitName}: 아직 배고픔, 다시 찾기... (허기: {unit.Hunger:F0}%)");
            TrySeekFoodNew();
            return;
        }

        // 배부르면 완료
        FinishEating();
    }

    // ==================== 공통 ====================

    /// <summary>
    /// 음식 찾기 업데이트 (메인 루프에서 호출)
    /// </summary>
    protected void UpdateSeekingFoodNew()
    {
        if (isEatingFromStorage)
        {
            UpdateEatingFromStorage();
        }
        else
        {
            UpdateEatingFromGround();
        }
    }

    /// <summary>
    /// 음식 찾기 재시도
    /// </summary>
    private void RetryFoodSearch()
    {
        foodSearchAttempts++;

        if (foodSearchAttempts >= MAX_FOOD_SEARCH_ATTEMPTS)
        {
            Debug.Log($"[Food] {unit.UnitName}: 음식 찾기 최대 시도 횟수 초과, 포기");
            FinishEating();
            return;
        }

        // 상태 초기화
        targetFoodStorage = null;
        if (targetFoodItem != null && targetFoodItem.ReservedBy == unit)
        {
            targetFoodItem.CancelReservation();
        }
        targetFoodItem = null;
        isEatingFromStorage = false;

        // 다시 찾기
        if (!TrySeekFoodNew())
        {
            FinishEating();
        }
    }

    /// <summary>
    /// 먹기 완료
    /// </summary>
    private void FinishEating()
    {
        Debug.Log($"[Food] {unit.UnitName}: 먹기 완료 (허기: {unit.Hunger:F0}%)");

        // 예약 해제
        if (targetFoodItem != null && targetFoodItem.ReservedBy == unit)
        {
            targetFoodItem.CancelReservation();
        }

        targetFoodStorage = null;
        targetFoodItem = null;
        isEatingFromStorage = false;
        bb.NearestFood = null;
        eatTimer = 0f;
        foodSearchAttempts = 0;

        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    /// <summary>
    /// 음식 찾기 취소
    /// </summary>
    protected void CancelSeekingFood()
    {
        if (targetFoodItem != null && targetFoodItem.ReservedBy == unit)
        {
            targetFoodItem.CancelReservation();
        }

        targetFoodStorage = null;
        targetFoodItem = null;
        isEatingFromStorage = false;
        bb.NearestFood = null;
        eatTimer = 0f;
        foodSearchAttempts = 0;
    }
}