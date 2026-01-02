using UnityEngine;
using UnityEngine.AI;

public enum AIBehaviorState
{
    Idle,
    Wandering,
    SeekingFood,      // 생존
    Resting,          // 생존
    Working,          // 건축/채집
    PickingUpItem,    // 아이템 줍기
    DeliveringToStorage,
    ExecutingCommand  // 플레이어 명령
}

/// <summary>
/// 작업 우선순위 등급
/// 숫자가 낮을수록 높은 우선순위
/// </summary>
public enum TaskPriorityLevel
{
    PlayerCommand = 0,  // 1순위: 무조건 인터럽트
    Survival = 1,       // 2순위: 플레이어 제외 인터럽트
    Construction = 2,   // 3순위: 플레이어/생존 제외 인터럽트
    ItemPickup = 3,     // 4순위: 현재 일 끝내고 이동
    Harvest = 4,        // 5순위: 현재 일 끝내고 이동
    FreeWill = 5        // 6순위: 대기, 언제든 인터럽트 가능
}

/// <summary>
/// 유닛 AI - 우선순위별 인터럽트 시스템
/// 
/// 인터럽트 규칙:
/// - 플레이어(1순위): 어떤 일을 하던 중단하고 이동
/// - 생존(2순위): 플레이어 제외 중단하고 이동
/// - 건축(3순위): 플레이어/생존 제외 중단하고 이동
/// - 아이템/채집(4~5순위): 현재 일 끝내고 이동
/// - 자유행동(6순위): 대기, 언제든 인터럽트 가능
/// </summary>
public class UnitAI : MonoBehaviour
{
    [Header("=== 기본 설정 ===")]
    [SerializeField] private float decisionInterval = 0.5f;
    [SerializeField] private float workRadius = 0.8f; // ★ 목표 위치 근처면 작업 시작
    [SerializeField] private float wanderRadius = 10f;

    [Header("=== 배고픔 설정 ===")]
    [SerializeField] private float hungerDecreasePerMinute = 3f;
    [SerializeField] private float foodSearchRadius = 20f;
    [SerializeField] private float hungerSeekThreshold = 50f;
    [SerializeField] private float hungerCriticalThreshold = 20f;

    [Header("=== 아이템 줍기 설정 ===")]
    [Tooltip("아이템을 줍는데 걸리는 시간 (초)")]
    [SerializeField] private float itemPickupDuration = 1f;
    [SerializeField] private float pickupRadius = 1.5f;

    [Header("=== 디버그 ===")]
    [SerializeField] private AIBehaviorState currentBehavior = AIBehaviorState.Idle;
    [SerializeField] private TaskPriorityLevel currentPriority = TaskPriorityLevel.FreeWill;

    private Unit unit;
    private UnitBlackboard bb;
    private NavMeshAgent agent;

    private float lastDecisionTime;
    private float workTimer;
    private float pickupTimer; // 아이템 줍기 타이머

    // 창고 배달 후 복귀
    private PostedTask previousTask;
    private bool returningFromDelivery = false;

    // 건물 작업 분산 위치
    private Vector3 assignedWorkPosition;
    private bool hasAssignedWorkPosition = false;

    // ★ 대기 중인 작업 (현재 작업 완료 후 이동)
    private PostedTask pendingTask;
    private bool hasPendingTask = false;

    // ★ 개인 아이템 목록 (자신이 드랍시킨 아이템)
    private System.Collections.Generic.List<DroppedItem> personalItems = new();
    private DroppedItem currentPersonalItem; // 현재 줍고 있는 개인 아이템

    // Properties
    public AIBehaviorState CurrentBehavior => currentBehavior;
    public TaskPriorityLevel CurrentPriority => currentPriority;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        bb = unit.Blackboard;
        if (bb != null)
            bb.OnHungerCritical += OnHungerCritical;
    }

    private void Update()
    {
        if (bb == null || !bb.IsAlive) return;

        // 배고픔 감소
        bb.DecreaseHunger((hungerDecreasePerMinute / 60f) * Time.deltaTime);

        if (bb.IsStarving)
            unit.TakeDamage(Time.deltaTime * 5f);

        // 주기적 의사결정
        if (Time.time - lastDecisionTime >= decisionInterval)
        {
            MakeDecision();
            lastDecisionTime = Time.time;
        }

        ExecuteCurrentBehavior();
    }

    // ==================== 의사결정 ====================

    /// <summary>
    /// 의사결정 (우선순위별 인터럽트)
    /// 
    /// 인터럽트 규칙:
    /// - 플레이어(1순위): 무조건 중단
    /// - 생존(2순위): 플레이어 제외 중단
    /// - 건축(3순위): 플레이어/생존 제외 중단 (채집 중에도!)
    /// - 아이템/채집(4~5순위): 현재 일 끝내고 이동
    /// </summary>
    private void MakeDecision()
    {
        // ===== 1순위: 플레이어 명령 (무조건 인터럽트) =====
        if (bb.HasPlayerCommand && bb.PlayerCommand != null)
        {
            InterruptCurrentTask();
            ExecutePlayerCommand();
            return;
        }

        // ===== 2순위: 생존 (플레이어 제외 인터럽트) =====
        if (bb.Hunger <= hungerCriticalThreshold)
        {
            // 플레이어 명령 중이 아니면 인터럽트 가능
            if (currentPriority != TaskPriorityLevel.PlayerCommand)
            {
                if (TrySeekFood())
                {
                    InterruptCurrentTask();
                    SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
                    return;
                }
            }
        }

        // ===== 3순위: 건설 (채집/아이템/자유행동 중이면 인터럽트) =====
        // 이미 건설 작업 중이면 스킵
        if (currentPriority != TaskPriorityLevel.Construction)
        {
            // 플레이어/생존 중이 아니면
            if (currentPriority > TaskPriorityLevel.Survival)
            {
                int constructCount = TaskManager.Instance?.GetAvailableTaskCount(TaskType.Construct) ?? 0;

                if (constructCount > 0)
                {
                    Debug.Log($"[UnitAI] {unit.UnitName}: 건설 작업 {constructCount}개 발견! 현재 상태: {currentBehavior}, 우선순위: {currentPriority}");
                    InterruptCurrentTask();

                    if (TryPullTask())
                    {
                        Debug.Log($"[UnitAI] {unit.UnitName}: 작업 할당 성공! → {bb.CurrentTask?.Data.Type}");
                        return;
                    }
                    else
                    {
                        Debug.LogWarning($"[UnitAI] {unit.UnitName}: 작업 할당 실패!");
                    }
                }
                else
                {
                    // 건설 작업이 0인 이유 확인
                    TaskManager.Instance?.DebugPrintConstructionStatus();
                }
            }
        }

        // ===== 여기서부터는 인터럽트 불가 (현재 작업 계속) =====

        // 배달 중이면 계속
        if (currentBehavior == AIBehaviorState.DeliveringToStorage)
            return;

        // ★ 개인 아이템 줍기 중이면 계속
        if (currentPersonalItem != null)
            return;

        // 채집/아이템 줍기 중이면 계속 (나무/아이템 완료 후 다음 작업)
        if (bb.CurrentTask != null &&
            (currentBehavior == AIBehaviorState.Working || currentBehavior == AIBehaviorState.PickingUpItem))
            return;

        // 인벤토리 가득 차면 창고로
        if (unit.Inventory.IsFull)
        {
            // ★ 못 주운 개인 아이템 공용 전환
            GiveUpRemainingPersonalItems();
            StartDeliveryToStorage();
            return;
        }

        // ★ 개인 아이템이 있으면 먼저 줍기 (건설보다 낮지만 공용 아이템/채집보다 높음)
        if (HasPersonalItems())
        {
            TryPickupPersonalItems();
            return;
        }

        // ===== 대기 중인 작업 처리 =====
        if (hasPendingTask && pendingTask != null)
        {
            AssignTask(pendingTask);
            hasPendingTask = false;
            pendingTask = null;
            return;
        }

        // ===== Idle 상태: 새 작업 찾기 =====
        if (bb.IsIdle || currentBehavior == AIBehaviorState.Idle || currentBehavior == AIBehaviorState.Wandering)
        {
            // 배고프면 음식 먼저 (비 크리티컬)
            if (bb.Hunger <= hungerSeekThreshold && TrySeekFood())
            {
                SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
                return;
            }

            // TaskManager에서 Pull (건설 > 아이템 > 채집 순)
            if (TryPullTask())
                return;

            // 자유 행동
            PerformFreeWill();
        }
    }

    // ==================== 인터럽트 시스템 ====================

    /// <summary>
    /// 새 우선순위가 현재 작업을 인터럽트할 수 있는지 체크
    /// </summary>
    private bool CanInterrupt(TaskPriorityLevel newPriority)
    {
        // 자유행동은 언제든 인터럽트 가능
        if (currentPriority == TaskPriorityLevel.FreeWill)
            return true;

        // 플레이어 명령: 무조건 인터럽트
        if (newPriority == TaskPriorityLevel.PlayerCommand)
            return true;

        // 생존: 플레이어 제외 인터럽트
        if (newPriority == TaskPriorityLevel.Survival)
            return currentPriority > TaskPriorityLevel.PlayerCommand;

        // 건축: 플레이어/생존 제외 인터럽트
        if (newPriority == TaskPriorityLevel.Construction)
            return currentPriority > TaskPriorityLevel.Survival;

        // 아이템/채집: 인터럽트 불가 (대기열에 추가)
        return false;
    }

    /// <summary>
    /// 현재 작업 인터럽트 (즉시 중단)
    /// </summary>
    private void InterruptCurrentTask()
    {
        if (bb.CurrentTask != null)
        {
            TaskManager.Instance?.LeaveTask(bb.CurrentTask, unit);
            bb.CurrentTask = null;
        }

        bb.TargetObject = null;
        bb.TargetPosition = null;
        hasAssignedWorkPosition = false;
        previousTask = null;
        returningFromDelivery = false;
        hasPendingTask = false;
        pendingTask = null;
        workTimer = 0f;
        pickupTimer = 0f;
    }

    /// <summary>
    /// 대기열에 작업 추가 (현재 작업 완료 후 수행)
    /// </summary>
    private void SetPendingTask(PostedTask task)
    {
        pendingTask = task;
        hasPendingTask = true;
    }

    // ==================== 작업 Pull ====================

    private bool TryPullTask()
    {
        if (TaskManager.Instance == null) return false;

        // 배달 후 복귀
        if (returningFromDelivery && previousTask != null)
        {
            if (previousTask.State != PostedTaskState.Completed &&
                previousTask.State != PostedTaskState.Cancelled)
            {
                if (TaskManager.Instance.TakeTask(previousTask, unit))
                {
                    AssignTask(previousTask);
                    previousTask = null;
                    returningFromDelivery = false;
                    return true;
                }
            }
            previousTask = null;
            returningFromDelivery = false;
        }

        // 새 작업 찾기
        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task != null && TaskManager.Instance.TakeTask(task, unit))
        {
            AssignTask(task);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 작업 할당
    /// </summary>
    private void AssignTask(PostedTask task)
    {
        bb.CurrentTask = task;
        bb.TargetObject = task.Data.TargetObject;
        hasAssignedWorkPosition = false;
        workTimer = 0f;
        pickupTimer = 0f;

        switch (task.Data.Type)
        {
            case TaskType.Construct:
                // 건물 작업: 분산 위치 계산
                assignedWorkPosition = CalculateDistributedWorkPosition(task);
                hasAssignedWorkPosition = true;
                bb.TargetPosition = assignedWorkPosition;
                unit.MoveTo(assignedWorkPosition);
                SetBehaviorAndPriority(AIBehaviorState.Working, TaskPriorityLevel.Construction);
                break;

            case TaskType.PickupItem:
                bb.TargetPosition = task.Data.TargetPosition;
                unit.MoveTo(task.Data.TargetPosition);
                SetBehaviorAndPriority(AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
                break;

            case TaskType.Harvest:
                bb.TargetPosition = task.Data.TargetPosition;
                unit.MoveTo(task.Data.TargetPosition);
                SetBehaviorAndPriority(AIBehaviorState.Working, TaskPriorityLevel.Harvest);
                break;

            default:
                bb.TargetPosition = task.Data.TargetPosition;
                unit.MoveTo(task.Data.TargetPosition);
                SetBehaviorAndPriority(AIBehaviorState.Working, TaskPriorityLevel.FreeWill);
                break;
        }

        Debug.Log($"[UnitAI] {unit.UnitName}: 작업 시작 - {task.Data.Type} (우선순위: {currentPriority})");
    }

    // ==================== 건물 분산 위치 ====================

    private Vector3 CalculateDistributedWorkPosition(PostedTask task)
    {
        Vector3 buildingOrigin = task.Data.TargetPosition; // 좌하단 모서리
        Vector2Int buildingSize = Vector2Int.one;

        var building = task.Owner as Building;
        if (building?.Data != null)
            buildingSize = building.Data.Size;

        // ★ 실제 건물 중심 계산 (좌하단 + 크기의 절반)
        Vector3 buildingCenter = buildingOrigin + new Vector3(buildingSize.x / 2f, 0, buildingSize.y / 2f);

        // 건물 크기/2 + 0.2 = 작업 가능 박스 범위
        float halfX = buildingSize.x / 2f + 0.2f;
        float halfZ = buildingSize.y / 2f + 0.2f;

        // 디버그용 저장
        debugBuildingCenter = buildingCenter;
        debugBoxHalfX = halfX;
        debugBoxHalfZ = halfZ;

        // 박스 안에서 랜덤 위치
        float randomX = Random.Range(-halfX, halfX);
        float randomZ = Random.Range(-halfZ, halfZ);

        Vector3 targetPos = buildingCenter + new Vector3(randomX, 0, randomZ);

        // NavMesh 위치 보정
        if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 1f, NavMesh.AllAreas))
            return hit.position;

        return buildingCenter;
    }

    // ==================== 디버그 시각화 ====================

    private Vector3 debugBuildingCenter;
    private float debugBoxHalfX;
    private float debugBoxHalfZ;

    private void OnDrawGizmos()
    {
        // 작업 중일 때만 표시
        if (currentBehavior != AIBehaviorState.Working || bb?.CurrentTask == null)
            return;

        if (bb.CurrentTask.Data.Type != TaskType.Construct)
            return;

        // 중심점 (흰색)
        Gizmos.color = Color.white;
        Gizmos.DrawSphere(debugBuildingCenter, 0.15f);

        // 작업 범위 박스 (검은색)
        Gizmos.color = Color.black;
        Vector3 boxSize = new Vector3(debugBoxHalfX * 2, 0.1f, debugBoxHalfZ * 2);
        Gizmos.DrawWireCube(debugBuildingCenter, boxSize);

        // 할당된 작업 위치 (노란색)
        if (hasAssignedWorkPosition)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(assignedWorkPosition, 0.1f);
            Gizmos.DrawLine(transform.position, assignedWorkPosition);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 선택했을 때 추가 정보 표시
        if (bb?.CurrentTask == null) return;

        var task = bb.CurrentTask;
        var building = task.Owner as Building;

        if (building == null) return;

        Vector3 buildingPos = task.Data.TargetPosition;
        Vector2Int size = building.Data?.Size ?? Vector2Int.one;

        // 건물 실제 영역 (빨간색)
        Gizmos.color = Color.red;
        Vector3 buildingBox = new Vector3(size.x, 0.2f, size.y);
        Gizmos.DrawWireCube(buildingPos, buildingBox);

        // 건물 Transform 위치 (파란색)
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(building.transform.position, 0.12f);
    }

    // ==================== 행동 실행 ====================

    private void ExecuteCurrentBehavior()
    {
        switch (currentBehavior)
        {
            case AIBehaviorState.SeekingFood:
                UpdateSeekingFood();
                break;
            case AIBehaviorState.Working:
                UpdateWorking();
                break;
            case AIBehaviorState.PickingUpItem:
                UpdatePickingUpItem();
                break;
            case AIBehaviorState.DeliveringToStorage:
                UpdateDelivery();
                break;
            case AIBehaviorState.ExecutingCommand:
                UpdatePlayerCommand();
                break;
            case AIBehaviorState.Wandering:
                UpdateWandering();
                break;
        }
    }

    // ==================== 음식 ====================

    private bool TrySeekFood()
    {
        var food = FindNearestFood();
        if (food != null)
        {
            bb.NearestFood = food;
            bb.TargetPosition = food.transform.position;
            unit.MoveTo(food.transform.position);
            return true;
        }
        return false;
    }

    private void UpdateSeekingFood()
    {
        if (bb.NearestFood == null)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        float dist = Vector3.Distance(transform.position, bb.NearestFood.transform.position);
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
            if (!item.IsAvailable) continue;
            if (item.Resource == null || !item.Resource.IsFood) continue;

            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = item;
            }
        }
        return nearest;
    }

    // ==================== 작업 (건축/채집) ====================

    private void UpdateWorking()
    {
        if (bb.CurrentTask == null)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        var task = bb.CurrentTask;
        Vector3 targetPos = hasAssignedWorkPosition ? assignedWorkPosition : task.Data.TargetPosition;
        float dist = Vector3.Distance(transform.position, targetPos);

        if (dist > workRadius)
        {
            if (unit.HasArrivedAtDestination())
                unit.MoveTo(targetPos);
            return;
        }

        // 작업 수행
        workTimer += Time.deltaTime;
        if (workTimer >= 1f)
        {
            workTimer = 0f;
            PerformWork(task);
        }
    }

    private void PerformWork(PostedTask task)
    {
        switch (task.Data.Type)
        {
            case TaskType.Construct:
                PerformConstruction(task);
                break;
            case TaskType.Harvest:
                PerformHarvest(task);
                break;
        }
    }

    private void PerformConstruction(PostedTask task)
    {
        var building = task.Owner as Building;
        if (building == null || building.CurrentState == BuildingState.Completed)
        {
            CompleteCurrentTask();
            return;
        }

        float work = unit.DoWork();
        if (building.DoConstructionWork(work))
        {
            TaskManager.Instance?.CompleteTask(task);
            CompleteCurrentTask();
        }
    }

    private void PerformHarvest(PostedTask task)
    {
        var node = task.Owner as ResourceNode;
        if (node == null || node.IsDepleted)
        {
            CompleteCurrentTask();
            return;
        }

        if (unit.Inventory.IsFull)
        {
            previousTask = task;
            TaskManager.Instance?.LeaveTask(task, unit);
            bb.CurrentTask = null;
            StartDeliveryToStorage();
            return;
        }

        float gather = unit.DoGather(node.Data?.NodeType);

        // ★ 채집 시 드랍된 아이템 받아오기
        var droppedItems = node.Harvest(gather);

        // ★ 드랍된 아이템에 Owner(나) 설정 + 개인 목록에 추가
        foreach (var item in droppedItems)
        {
            if (item != null)
            {
                item.SetOwner(unit);
                AddPersonalItem(item);
            }
        }

        if (node.IsDepleted)
        {
            CompleteCurrentTask();

            // ★ 자원 고갈 시 개인 아이템 줍기 시작
            TryPickupPersonalItems();
        }
        else if (unit.Inventory.IsFull)
        {
            previousTask = task;
            TaskManager.Instance?.LeaveTask(task, unit);
            bb.CurrentTask = null;
            StartDeliveryToStorage();
        }
    }

    // ==================== 아이템 줍기 (시간 소요) ====================

    private void UpdatePickingUpItem()
    {
        // ★ 개인 아이템 줍기 중이면 별도 처리
        if (currentPersonalItem != null)
        {
            UpdatePickingUpPersonalItem();
            return;
        }

        // TaskManager의 공용 아이템 줍기
        if (bb.CurrentTask == null)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        var task = bb.CurrentTask;
        var item = task.Owner as DroppedItem;

        // 아이템이 사라졌으면 (다른 유닛이 주움)
        if (item == null)
        {
            CompleteCurrentTask();
            return;
        }

        float dist = Vector3.Distance(transform.position, item.transform.position);

        // 아이템 위치로 이동
        if (dist > pickupRadius)
        {
            if (unit.HasArrivedAtDestination())
                unit.MoveTo(item.transform.position);
            pickupTimer = 0f; // 이동 중이면 타이머 리셋
            return;
        }

        // ★ 아이템 줍기 (시간 소요)
        pickupTimer += Time.deltaTime;

        if (pickupTimer >= itemPickupDuration)
        {
            // 줍기 완료!
            if (!unit.Inventory.IsFull && item != null)
            {
                unit.Inventory.AddItem(item.Resource, item.Amount);
                item.PickUp(unit);
                Debug.Log($"[UnitAI] {unit.UnitName}: {item.Resource?.ResourceName} 줍기 완료! ({itemPickupDuration}초)");
            }
            CompleteCurrentTask();
        }
    }

    // ==================== 창고 배달 ====================

    private void StartDeliveryToStorage()
    {
        var storagePos = TaskManager.Instance?.GetStoragePosition();

        if (storagePos == null)
        {
            unit.Inventory.DepositToStorage();
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        bb.TargetPosition = storagePos.Value;
        unit.MoveTo(storagePos.Value);
        SetBehaviorAndPriority(AIBehaviorState.DeliveringToStorage, TaskPriorityLevel.ItemPickup);
    }

    private void UpdateDelivery()
    {
        if (!bb.TargetPosition.HasValue)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        float dist = Vector3.Distance(transform.position, bb.TargetPosition.Value);

        if (dist < 2f || unit.HasArrivedAtDestination())
        {
            unit.Inventory.DepositToStorage();

            if (previousTask != null)
            {
                returningFromDelivery = true;
            }

            bb.TargetPosition = null;
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        }
    }

    private void CompleteCurrentTask()
    {
        if (bb.CurrentTask != null)
            TaskManager.Instance?.LeaveTask(bb.CurrentTask, unit);

        bb.CurrentTask = null;
        bb.TargetObject = null;
        bb.TargetPosition = null;
        hasAssignedWorkPosition = false;
        workTimer = 0f;
        pickupTimer = 0f;
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    // ==================== 플레이어 명령 ====================

    public void GiveCommand(UnitCommand command)
    {
        bb.HasPlayerCommand = true;
        bb.PlayerCommand = command;

        // 즉시 인터럽트
        InterruptCurrentTask();
        ExecutePlayerCommand();
    }

    private void ExecutePlayerCommand()
    {
        var cmd = bb.PlayerCommand;
        if (cmd == null) return;

        SetBehaviorAndPriority(AIBehaviorState.ExecutingCommand, TaskPriorityLevel.PlayerCommand);

        switch (cmd.Type)
        {
            case UnitCommandType.MoveTo:
                if (cmd.TargetPosition.HasValue)
                    unit.MoveTo(cmd.TargetPosition.Value);
                break;
            case UnitCommandType.Stop:
                unit.StopMoving();
                ClearPlayerCommand();
                break;
        }
    }

    private void UpdatePlayerCommand()
    {
        if (!bb.HasPlayerCommand || bb.PlayerCommand == null)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        if (bb.PlayerCommand.Type == UnitCommandType.MoveTo && unit.HasArrivedAtDestination())
        {
            ClearPlayerCommand();
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        }
    }

    private void ClearPlayerCommand()
    {
        bb.HasPlayerCommand = false;
        bb.PlayerCommand = null;
    }

    // ==================== 자유 행동 ====================

    private void PerformFreeWill()
    {
        if (Random.value < 0.5f)
            StartWandering();
        else
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    private void StartWandering()
    {
        Vector3 randomPoint = transform.position + Random.insideUnitSphere * wanderRadius;
        randomPoint.y = transform.position.y;

        if (NavMesh.SamplePosition(randomPoint, out var hit, wanderRadius, NavMesh.AllAreas))
        {
            unit.MoveTo(hit.position);
            SetBehaviorAndPriority(AIBehaviorState.Wandering, TaskPriorityLevel.FreeWill);
        }
    }

    private void UpdateWandering()
    {
        if (unit.HasArrivedAtDestination())
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    // ==================== 유틸리티 ====================

    private void SetBehaviorAndPriority(AIBehaviorState behavior, TaskPriorityLevel priority)
    {
        currentBehavior = behavior;
        currentPriority = priority;

        bb?.SetState(behavior switch
        {
            AIBehaviorState.Idle => UnitState.Idle,
            AIBehaviorState.Working => UnitState.Working,
            AIBehaviorState.PickingUpItem => UnitState.Working,
            AIBehaviorState.DeliveringToStorage => UnitState.Moving,
            AIBehaviorState.SeekingFood or AIBehaviorState.Resting => UnitState.Eating,
            AIBehaviorState.Wandering or AIBehaviorState.ExecutingCommand => UnitState.Moving,
            _ => UnitState.Idle
        });
    }

    private void OnHungerCritical()
    {
        Debug.Log($"[UnitAI] {unit.UnitName}: 배고픔 위험!");
    }

    // ==================== 개인 아이템 시스템 ====================

    /// <summary>
    /// 개인 아이템 목록에 추가 (자신이 드랍시킨 아이템)
    /// </summary>
    public void AddPersonalItem(DroppedItem item)
    {
        if (item != null && !personalItems.Contains(item))
        {
            personalItems.Add(item);
            Debug.Log($"[UnitAI] {unit.UnitName}: 개인 아이템 추가 ({personalItems.Count}개)");
        }
    }

    /// <summary>
    /// 개인 아이템 목록에서 제거
    /// </summary>
    public void RemovePersonalItem(DroppedItem item)
    {
        if (personalItems.Contains(item))
        {
            personalItems.Remove(item);

            if (currentPersonalItem == item)
                currentPersonalItem = null;
        }
    }

    /// <summary>
    /// 개인 아이템 줍기 시도
    /// </summary>
    private void TryPickupPersonalItems()
    {
        // 유효하지 않은 아이템 제거
        personalItems.RemoveAll(item => item == null);

        if (personalItems.Count == 0)
            return;

        // 인벤토리 가득 차면 창고로
        if (unit.Inventory.IsFull)
        {
            // 못 주운 아이템들은 공용으로 전환
            GiveUpRemainingPersonalItems();
            StartDeliveryToStorage();
            return;
        }

        // 가장 가까운 개인 아이템 찾기
        DroppedItem nearest = FindNearestPersonalItem();
        if (nearest != null)
        {
            StartPickingUpPersonalItem(nearest);
        }
    }

    /// <summary>
    /// 가장 가까운 개인 아이템 찾기
    /// </summary>
    private DroppedItem FindNearestPersonalItem()
    {
        DroppedItem nearest = null;
        float minDist = float.MaxValue;

        foreach (var item in personalItems)
        {
            if (item == null) continue;

            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = item;
            }
        }
        return nearest;
    }

    /// <summary>
    /// 개인 아이템 줍기 시작
    /// </summary>
    private void StartPickingUpPersonalItem(DroppedItem item)
    {
        currentPersonalItem = item;

        // ★ 애니메이션 중이 아니면 예약 (중이면 나중에 예약)
        if (!item.IsAnimating)
        {
            item.Reserve(unit);
        }

        unit.MoveTo(item.transform.position);
        SetBehaviorAndPriority(AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
        pickupTimer = 0f;

        Debug.Log($"[UnitAI] {unit.UnitName}: 개인 아이템 줍기 시작 ({item.Resource?.ResourceName}) {(item.IsAnimating ? "[애니메이션 대기]" : "")}");
    }

    /// <summary>
    /// 개인 아이템 줍기 상태 업데이트
    /// </summary>
    private void UpdatePickingUpPersonalItem()
    {
        // 현재 줍고 있는 개인 아이템이 없으면
        if (currentPersonalItem == null)
        {
            // 다음 개인 아이템 찾기
            TryPickupPersonalItems();

            if (currentPersonalItem == null)
            {
                SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            }
            return;
        }

        // ★ 아이템이 사라졌으면 (다른 방식으로 수거됨)
        if (currentPersonalItem == null || currentPersonalItem.gameObject == null)
        {
            currentPersonalItem = null;
            TryPickupPersonalItems();
            return;
        }

        // ★ 애니메이션 중이면 대기 (아이템이 튀어오르는 중)
        if (currentPersonalItem.IsAnimating)
        {
            // 가까이 가면서 대기
            float distToItem = Vector3.Distance(transform.position, currentPersonalItem.transform.position);
            if (distToItem > pickupRadius && unit.HasArrivedAtDestination())
            {
                unit.MoveTo(currentPersonalItem.transform.position);
            }
            pickupTimer = 0f; // 타이머 리셋
            return;
        }

        // ★ 예약 안 되어 있으면 다시 시도
        if (!currentPersonalItem.IsReserved)
        {
            currentPersonalItem.Reserve(unit);
        }

        float dist = Vector3.Distance(transform.position, currentPersonalItem.transform.position);

        // 아이템 위치로 이동
        if (dist > pickupRadius)
        {
            if (unit.HasArrivedAtDestination())
                unit.MoveTo(currentPersonalItem.transform.position);
            pickupTimer = 0f;
            return;
        }

        // 줍기 (시간 소요)
        pickupTimer += Time.deltaTime;

        if (pickupTimer >= itemPickupDuration)
        {
            // 줍기 완료!
            if (!unit.Inventory.IsFull && currentPersonalItem != null)
            {
                // ★ 먼저 데이터 저장 (PickUp 성공 시 오브젝트 파괴됨)
                var resource = currentPersonalItem.Resource;
                var amount = currentPersonalItem.Amount;

                // ★ PickUp 성공 여부 확인
                if (currentPersonalItem.PickUp(unit))
                {
                    unit.Inventory.AddItem(resource, amount);
                    Debug.Log($"[UnitAI] {unit.UnitName}: 개인 아이템 줍기 완료! ({resource?.ResourceName})");

                    RemovePersonalItem(currentPersonalItem);
                    currentPersonalItem = null;
                    pickupTimer = 0f;

                    // 다음 개인 아이템 찾기
                    TryPickupPersonalItems();

                    if (personalItems.Count == 0 && currentPersonalItem == null)
                    {
                        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
                    }
                }
                else
                {
                    // ★ PickUp 실패 (아직 애니메이션 중이거나 다른 이유)
                    // 타이머 리셋해서 재시도
                    Debug.Log($"[UnitAI] {unit.UnitName}: 아이템 줍기 실패, 재시도...");
                    pickupTimer = 0f;
                }
            }
            else if (unit.Inventory.IsFull)
            {
                // 인벤토리 가득 참 → 남은 아이템 포기
                GiveUpRemainingPersonalItems();
                StartDeliveryToStorage();
            }
        }
    }

    /// <summary>
    /// 남은 개인 아이템들 포기 (공용으로 전환)
    /// </summary>
    private void GiveUpRemainingPersonalItems()
    {
        foreach (var item in personalItems)
        {
            if (item != null)
            {
                item.OwnerGiveUp();
            }
        }
        personalItems.Clear();
        currentPersonalItem = null;

        Debug.Log($"[UnitAI] {unit.UnitName}: 남은 개인 아이템 포기 → 공용 전환");
    }

    /// <summary>
    /// 개인 아이템이 있는지 확인
    /// </summary>
    public bool HasPersonalItems()
    {
        personalItems.RemoveAll(item => item == null);
        return personalItems.Count > 0 || currentPersonalItem != null;
    }

    // 호환성
    public void AddPlayerCommand(UnitTask task) { }
    public void AddPlayerCommandImmediate(UnitTask task) { }
    public void OnTaskCompleted(UnitTask task) { }
    public void OnFoodEaten(float nutrition) => bb?.Eat(nutrition);
}