using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ==================== Enums ====================

public enum AIBehaviorState
{
    Idle,
    Wandering,
    SeekingFood,
    Working,
    PickingUpItem,
    DeliveringToStorage,
    WaitingForStorage,  // ★ 저장고 대기 상태
    ExecutingCommand,
    WorkingAtStation  // ★ 추가
}

public enum TaskPriorityLevel
{
    PlayerCommand = 0,
    Survival = 1,
    Construction = 2,
    Workstation = 3,  // ★ 추가
    ItemPickup = 4,
    Harvest = 5,
    FreeWill = 6
}

public enum TaskPhase
{
    None,
    MovingToWork,
    Working,
    Relocating
}

/// <summary>
/// 저장고 배달 단계
/// </summary>
public enum DeliveryPhase
{
    None,
    MovingToStorage,    // 저장고로 이동 중
    Depositing          // 저장 중
}

// ==================== Main Class ====================

public class UnitAi : MonoBehaviour
{
    // ==================== Nested Class ====================

    private class TaskContext
    {
        public PostedTask Task;
        public TaskPhase Phase = TaskPhase.None;
        public Vector3 WorkPosition;
        public Vector2Int TargetSize = Vector2Int.one;
        public float WorkTimer;

        public bool HasTask => Task != null && Phase != TaskPhase.None;
        public bool IsMoving => Phase == TaskPhase.MovingToWork || Phase == TaskPhase.Relocating;
        public bool IsWorking => Phase == TaskPhase.Working;

        public void Clear()
        {
            Task = null;
            Phase = TaskPhase.None;
            WorkPosition = Vector3.zero;
            TargetSize = Vector2Int.one;
            WorkTimer = 0f;
        }

        public void SetMoving(Vector3 position)
        {
            Phase = TaskPhase.MovingToWork;
            WorkPosition = position;
            WorkTimer = 0f;
        }

        public void SetWorking()
        {
            Phase = TaskPhase.Working;
            WorkTimer = 0f;
        }

        public void SetRelocating(Vector3 newPosition)
        {
            Phase = TaskPhase.Relocating;
            WorkPosition = newPosition;
            WorkTimer = 0f;
        }
    }

    // ==================== Fields ====================

    [Header("=== 기본 설정 ===")]
    [SerializeField] private float decisionInterval = 0.5f;
    [SerializeField] private float workRadius = 0.8f;
    [SerializeField] private float wanderRadius = 10f;

    [Header("=== 배고픔 설정 ===")]
    [SerializeField] private float hungerDecreasePerMinute = 3f;
    [SerializeField] private float foodSearchRadius = 20f;
    [SerializeField] private float hungerSeekThreshold = 50f;
    [SerializeField] private float hungerCriticalThreshold = 20f;

    [Header("=== 아이템 줍기 설정 ===")]
    [SerializeField] private float itemPickupDuration = 1f;
    [SerializeField] private float pickupRadius = 1.5f;

    [Header("=== 디버그 ===")]
    [SerializeField] private AIBehaviorState currentBehavior = AIBehaviorState.Idle;
    [SerializeField] private TaskPriorityLevel currentPriority = TaskPriorityLevel.FreeWill;

    private Unit unit;
    private UnitBlackboard bb;
    private NavMeshAgent agent;
    private TaskContext taskContext = new();

    private float lastDecisionTime;
    private float pickupTimer;

    private PostedTask previousTask;
    private List<DroppedItem> personalItems = new();
    private DroppedItem currentPersonalItem;

    // ★ 워크스테이션 작업용
    private IWorkstation currentWorkstation;
    private bool isWorkstationWorkStarted = false;

    // ★ 저장고 배달용
    private DeliveryPhase deliveryPhase = DeliveryPhase.None;
    private Vector3 storagePosition;
    private StorageComponent targetStorage;
    private float depositTimer = 0f;
    [Header("=== 저장고 배달 설정 ===")]
    [SerializeField] private float depositDuration = 2f;  // 저장에 걸리는 시간
    [SerializeField] private float storageSearchRadius = 50f;

    private Vector3 debugBuildingCenter;
    private float debugBoxHalfX, debugBoxHalfZ;

    // ==================== Properties ====================

    public AIBehaviorState CurrentBehavior => currentBehavior;
    public TaskPriorityLevel CurrentPriority => currentPriority;
    public bool HasTask => taskContext.HasTask;
    public bool IsIdle => currentBehavior == AIBehaviorState.Idle || currentBehavior == AIBehaviorState.Wandering;

    // ★ 저장고 존재 여부 캐시
    private bool hasStorageBuilding = false;
    private float lastStorageCheckTime = -10f;
    private const float STORAGE_CHECK_INTERVAL = 5f;  // 5초마다 체크

    // ==================== Unity Methods ====================

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

        bb.DecreaseHunger((hungerDecreasePerMinute / 60f) * Time.deltaTime);
        if (bb.IsStarving)
            unit.TakeDamage(Time.deltaTime * 5f);

        if (Time.time - lastDecisionTime >= decisionInterval)
        {
            MakeDecision();
            lastDecisionTime = Time.time;
        }

        ExecuteCurrentBehavior();
    }

    // ==================== 의사결정 ====================

    private void MakeDecision()
    {
        if (bb.HasPlayerCommand && bb.PlayerCommand != null)
        {
            InterruptCurrentTask();
            ExecutePlayerCommand();
            return;
        }

        if (bb.Hunger <= hungerCriticalThreshold && currentPriority > TaskPriorityLevel.Survival)
        {
            if (TrySeekFood())
            {
                InterruptCurrentTask();
                SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
                return;
            }
        }

        if (taskContext.HasTask)
            return;

        // ★ 저장고 배달 중이면 계속
        if (currentBehavior == AIBehaviorState.DeliveringToStorage)
            return;

        // ★ 저장고 대기 중이면 건설 작업만 받기
        if (currentBehavior == AIBehaviorState.WaitingForStorage)
        {
            if (TryPullConstructionTask())
                return;
            // 건설 작업 없으면 계속 대기
            return;
        }

        // ★ Unity null 체크로 Destroy된 오브젝트 정리
        if (currentPersonalItem != null && !currentPersonalItem)
            currentPersonalItem = null;
        personalItems.RemoveAll(item => item == null || !item);

        // 개인 아이템 줍기
        if (currentPersonalItem != null || personalItems.Count > 0)
        {
            if (currentBehavior != AIBehaviorState.PickingUpItem)
                TryPickupPersonalItems();
            return;
        }

        if (bb.Hunger <= hungerSeekThreshold && TrySeekFood())
        {
            SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
            return;
        }

        if (currentBehavior == AIBehaviorState.Idle || currentBehavior == AIBehaviorState.Wandering)
        {
            // ★ 작업을 먼저 찾고
            if (TryPullTask())
                return;

            // ★ 작업이 없고 인벤이 꽉 찼으면 저장고로
            if (ShouldDepositWhenIdle())
            {
                Debug.Log($"[UnitAI] {unit.UnitName}: 작업 없음 + 인벤토리 가득참 → 저장고로");
                StartDeliveryToStorage();
                return;
            }
        }

        if (currentBehavior == AIBehaviorState.Idle)
        {
            PerformFreeWill();
        }
    }

    // ==================== 작업 Pull ====================

    private bool TryPullTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task == null) return false;

        if (!TaskManager.Instance.TakeTask(task, unit))
            return false;

        AssignTask(task);
        return true;
    }

    /// <summary>
    /// ★ 건설 작업만 가져오기 (저장고 대기 중에도 건설은 함)
    /// </summary>
    private bool TryPullConstructionTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task == null) return false;

        // 건설 작업만 받음
        if (task.Data.Type != TaskType.Construct)
            return false;

        if (!TaskManager.Instance.TakeTask(task, unit))
            return false;

        // 저장고 대기 상태 해제
        ClearDeliveryState();

        AssignTask(task);
        Debug.Log($"[UnitAI] {unit.UnitName}: 저장고 대기 중 건설 작업 시작");
        return true;
    }

    private void AssignTask(PostedTask task)
    {
        taskContext.Clear();
        taskContext.Task = task;

        bb.CurrentTask = task;
        bb.TargetObject = task.Data.TargetObject;

        switch (task.Data.Type)
        {
            case TaskType.Construct:
                AssignConstructionTask(task);
                break;
            case TaskType.Harvest:
                AssignHarvestTask(task);
                break;
            case TaskType.PickupItem:
                AssignPickupTask(task);
                break;
            case TaskType.Workstation:  // ★ 추가
                AssignWorkstationTask(task);
                break;
            default:
                AssignGenericTask(task);
                break;
        }

        Debug.Log($"[UnitAI] {unit.UnitName}: 작업 할당 - {task.Data.Type}, Phase: {taskContext.Phase}");
    }

    private void AssignConstructionTask(PostedTask task)
    {
        var building = task.Owner as Building;
        Vector2Int size = building?.Data?.Size ?? Vector2Int.one;
        taskContext.TargetSize = size;

        Vector3 workPos = CalculateDistributedWorkPosition(task.Data.TargetPosition, size);

        taskContext.SetMoving(workPos);
        bb.TargetPosition = workPos;
        unit.MoveTo(workPos);

        SetBehaviorAndPriority(AIBehaviorState.Working, TaskPriorityLevel.Construction);
    }

    private void AssignHarvestTask(PostedTask task)
    {
        taskContext.SetMoving(task.Data.TargetPosition);
        bb.TargetPosition = task.Data.TargetPosition;
        unit.MoveTo(task.Data.TargetPosition);

        SetBehaviorAndPriority(AIBehaviorState.Working, TaskPriorityLevel.Harvest);
    }

    private void AssignPickupTask(PostedTask task)
    {
        taskContext.SetMoving(task.Data.TargetPosition);
        bb.TargetPosition = task.Data.TargetPosition;
        unit.MoveTo(task.Data.TargetPosition);

        SetBehaviorAndPriority(AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
    }

    // ★ 워크스테이션 작업 할당
    private void AssignWorkstationTask(PostedTask task)
    {
        currentWorkstation = task.Owner as IWorkstation;
        isWorkstationWorkStarted = false;

        if (currentWorkstation == null)
        {
            Debug.LogWarning("[UnitAI] 워크스테이션을 찾을 수 없습니다.");
            CompleteCurrentTask();
            return;
        }

        Vector3 workPos = currentWorkstation.WorkPoint?.position ?? task.Data.TargetPosition;

        taskContext.SetMoving(workPos);
        bb.TargetPosition = workPos;
        unit.MoveTo(workPos);

        SetBehaviorAndPriority(AIBehaviorState.WorkingAtStation, TaskPriorityLevel.Workstation);

        Debug.Log($"[UnitAI] {unit.UnitName}: 워크스테이션 작업 할당 - {currentWorkstation.TaskType}");
    }

    private void AssignGenericTask(PostedTask task)
    {
        taskContext.SetMoving(task.Data.TargetPosition);
        bb.TargetPosition = task.Data.TargetPosition;
        unit.MoveTo(task.Data.TargetPosition);

        SetBehaviorAndPriority(AIBehaviorState.Working, TaskPriorityLevel.FreeWill);
    }

    // ==================== 작업 위치 계산 ====================

    private Vector3 CalculateDistributedWorkPosition(Vector3 origin, Vector2Int size)
    {
        Vector3 center = origin + new Vector3(size.x / 2f, 0, size.y / 2f);

        float halfX = size.x / 2f + 0.3f;
        float halfZ = size.y / 2f + 0.3f;

        debugBuildingCenter = center;
        debugBoxHalfX = halfX;
        debugBoxHalfZ = halfZ;

        float randomX = Random.Range(-halfX, halfX);
        float randomZ = Random.Range(-halfZ, halfZ);
        Vector3 targetPos = center + new Vector3(randomX, 0, randomZ);

        if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
            return hit.position;

        return center;
    }

    public void UpdateAssignedWorkPosition(Vector3 newOrigin, Vector2Int size)
    {
        if (!taskContext.HasTask) return;

        Vector3 newWorkPos = CalculateDistributedWorkPosition(newOrigin, size);

        taskContext.SetRelocating(newWorkPos);
        taskContext.TargetSize = size;

        bb.TargetPosition = newWorkPos;
        unit.MoveTo(newWorkPos);

        Debug.Log($"[UnitAI] {unit.UnitName}: 재배치 → {newWorkPos}");
    }

    // ==================== 행동 실행 ====================

    private void ExecuteCurrentBehavior()
    {
        switch (currentBehavior)
        {
            case AIBehaviorState.Working:
                UpdateWorking();
                break;
            case AIBehaviorState.WorkingAtStation:  // ★ 추가
                UpdateWorkingAtStation();
                break;
            case AIBehaviorState.PickingUpItem:
                UpdatePickingUpItem();
                break;
            case AIBehaviorState.SeekingFood:
                UpdateSeekingFood();
                break;
            case AIBehaviorState.DeliveringToStorage:
                UpdateDeliveryToStorage();
                break;
            case AIBehaviorState.WaitingForStorage:  // ★ 저장고 대기
                UpdateWaitingForStorage();
                break;
            case AIBehaviorState.ExecutingCommand:
                UpdatePlayerCommand();
                break;
            case AIBehaviorState.Wandering:
                UpdateWandering();
                break;
        }
    }

    // ==================== Working ====================

    private void UpdateWorking()
    {
        if (!taskContext.HasTask)
        {
            CompleteCurrentTask();
            return;
        }

        var task = taskContext.Task;

        if (!ValidateTaskTarget(task))
        {
            CompleteCurrentTask();
            return;
        }

        switch (taskContext.Phase)
        {
            case TaskPhase.MovingToWork:
            case TaskPhase.Relocating:
                UpdateMovingToWork();
                break;
            case TaskPhase.Working:
                UpdateExecutingWork();
                break;
        }
    }

    // ★ 워크스테이션 작업 업데이트
    private void UpdateWorkingAtStation()
    {
        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        switch (taskContext.Phase)
        {
            case TaskPhase.MovingToWork:
                UpdateMovingToWorkstation();
                break;
            case TaskPhase.Working:
                UpdateExecutingWorkstation();
                break;
        }
    }

    private void UpdateMovingToWorkstation()
    {
        float dist = Vector3.Distance(transform.position, taskContext.WorkPosition);

        if (dist <= workRadius)
        {
            // 작업자 배정
            if (!currentWorkstation.AssignWorker(unit))
            {
                Debug.LogWarning($"[UnitAI] {unit.UnitName}: 워크스테이션 작업자 배정 실패!");
                CompleteCurrentTask();
                return;
            }

            taskContext.SetWorking();
            Debug.Log($"[UnitAI] {unit.UnitName}: 워크스테이션 도착, 작업 시작");
            return;
        }

        if (unit.HasArrivedAtDestination() || agent.velocity.magnitude < 0.1f)
        {
            unit.MoveTo(taskContext.WorkPosition);
        }
    }

    private void UpdateExecutingWorkstation()
    {
        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        // 작업 시작 (한 번만)
        if (!isWorkstationWorkStarted && currentWorkstation.CanStartWork)
        {
            currentWorkstation.StartWork();
            isWorkstationWorkStarted = true;
        }

        // 작업 수행
        taskContext.WorkTimer += Time.deltaTime;
        if (taskContext.WorkTimer >= 1f)
        {
            taskContext.WorkTimer = 0f;
            float workAmount = unit.DoWork();
            currentWorkstation.DoWork(workAmount);
        }

        // 작업 완료 체크
        var wsComponent = currentWorkstation as WorkstationComponent;
        if (wsComponent != null)
        {
            if (!wsComponent.IsWorking && isWorkstationWorkStarted)
            {
                // 다음 작업이 있는지 확인
                if (currentWorkstation.CanStartWork)
                {
                    isWorkstationWorkStarted = false;
                    // 다시 작업 시작
                }
                else
                {
                    // 모든 작업 완료
                    currentWorkstation.ReleaseWorker();
                    TaskManager.Instance?.CompleteTask(taskContext.Task);
                    CompleteCurrentTask();
                }
            }
        }
    }

    private void UpdateMovingToWork()
    {
        float dist = Vector3.Distance(transform.position, taskContext.WorkPosition);

        if (dist <= workRadius)
        {
            taskContext.SetWorking();
            Debug.Log($"[UnitAI] {unit.UnitName}: 작업 위치 도착, 작업 시작");
            return;
        }

        if (unit.HasArrivedAtDestination() || agent.velocity.magnitude < 0.1f)
        {
            unit.MoveTo(taskContext.WorkPosition);
        }
    }

    private void UpdateExecutingWork()
    {
        taskContext.WorkTimer += Time.deltaTime;

        if (taskContext.WorkTimer >= 1f)
        {
            taskContext.WorkTimer = 0f;
            PerformWork(taskContext.Task);
        }
    }

    private bool ValidateTaskTarget(PostedTask task)
    {
        switch (task.Data.Type)
        {
            case TaskType.Construct:
                var building = task.Owner as Building;
                return building != null && building.CurrentState != BuildingState.Completed;
            case TaskType.Harvest:
                var node = task.Owner as ResourceNode;
                return node != null && !node.IsDepleted;
            case TaskType.Workstation:
                var ws = task.Owner as IWorkstation;
                return ws != null;
            default:
                return task.Owner != null;
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
            TryPickupPersonalItems();
            return;
        }

        // ★ 채집할 자원 확인
        ResourceItemSO nodeResource = null;
        if (node.Data?.Drops != null)
        {
            foreach (var drop in node.Data.Drops)
            {
                if (drop.Resource != null)
                {
                    nodeResource = drop.Resource;
                    break;
                }
            }
        }

        // ★ 저장고로 가야 하는지 확인
        // 조건: 모든 슬롯 사용 중 AND 해당 아이템 공간 없음
        if (nodeResource != null && ShouldDepositInventory(nodeResource))
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 인벤 가득참 + {nodeResource.ResourceName} 공간 없음 → 저장고로 이동");
            previousTask = task;
            TaskManager.Instance?.LeaveTask(task, unit);
            taskContext.Clear();
            bb.CurrentTask = null;
            StartDeliveryToStorage();
            return;
        }

        float gather = unit.DoGather(node.Data?.NodeType);
        var drops = node.Harvest(gather);

        foreach (var drop in drops)
        {
            AddPersonalItem(drop);
        }

        if (node.IsDepleted)
        {
            CompleteCurrentTask();
            TryPickupPersonalItems();
        }
    }

    // ==================== 작업 완료 ====================

    private void CompleteCurrentTask()
    {
        // ★ 워크스테이션 정리
        if (currentWorkstation != null)
        {
            currentWorkstation.ReleaseWorker();
            currentWorkstation = null;
            isWorkstationWorkStarted = false;
        }

        if (taskContext.Task != null)
        {
            TaskManager.Instance?.LeaveTask(taskContext.Task, unit);
        }

        taskContext.Clear();

        bb.CurrentTask = null;
        bb.TargetObject = null;
        bb.TargetPosition = null;

        pickupTimer = 0f;

        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);

        Debug.Log($"[UnitAI] {unit.UnitName}: 작업 완료, Idle 상태");
    }

    private void InterruptCurrentTask()
    {
        // ★ 워크스테이션 정리
        if (currentWorkstation != null)
        {
            currentWorkstation.CancelWork();
            currentWorkstation.ReleaseWorker();
            currentWorkstation = null;
            isWorkstationWorkStarted = false;
        }

        if (taskContext.Task != null)
        {
            TaskManager.Instance?.LeaveTask(taskContext.Task, unit);
        }
        taskContext.Clear();
        bb.CurrentTask = null;
    }

    // ==================== 아이템 줍기 ====================

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

        var task = taskContext.Task;
        var item = task.Owner as DroppedItem;

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
            }
            CompleteCurrentTask();
            pickupTimer = 0f;
        }
    }

    // ==================== 개인 아이템 ====================

    public void AddPersonalItem(DroppedItem item)
    {
        if (item == null || personalItems.Contains(item)) return;

        personalItems.Add(item);
        item.SetOwner(unit);
    }

    public void RemovePersonalItem(DroppedItem item)
    {
        if (item == null) return;

        personalItems.Remove(item);

        // ★ Unity null 체크로 비교
        if (currentPersonalItem != null && currentPersonalItem == item)
            currentPersonalItem = null;
    }

    private void TryPickupPersonalItems()
    {
        // ★ Unity null 체크로 Destroy된 오브젝트 정리
        if (currentPersonalItem != null && !currentPersonalItem)
            currentPersonalItem = null;
        personalItems.RemoveAll(item => item == null || !item || item.Owner != unit);

        if (personalItems.Count == 0)
        {
            // 줍을 아이템 없음
            return;
        }

        currentPersonalItem = personalItems[0];
        personalItems.RemoveAt(0);

        // ★ 다시 한번 체크 (혹시 모를 상황 대비)
        if (currentPersonalItem == null || !currentPersonalItem)
        {
            currentPersonalItem = null;
            TryPickupPersonalItems();  // 재귀 호출로 다음 아이템 시도
            return;
        }

        if (!currentPersonalItem.IsAnimating)
            currentPersonalItem.Reserve(unit);

        unit.MoveTo(currentPersonalItem.transform.position);
        SetBehaviorAndPriority(AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
        pickupTimer = 0f;
    }

    private void UpdatePickingUpPersonalItem()
    {
        // ★ Unity null 체크 (Destroy된 오브젝트 감지)
        if (currentPersonalItem == null || !currentPersonalItem)
        {
            currentPersonalItem = null;

            // 다음 아이템 시도
            TryPickupPersonalItems();

            // 줍을 아이템이 없으면 다음 작업으로
            if (currentPersonalItem == null)
            {
                ReturnToPreviousTaskOrIdle();
            }
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
        if (pickupTimer < itemPickupDuration)
            return;

        // === 줍기 실행 ===
        var resource = currentPersonalItem.Resource;

        // 인벤토리 공간 체크
        if (ShouldDepositInventory(resource))
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 인벤 가득참 + {resource?.ResourceName} 공간 없음 → 저장고로 이동");
            GiveUpRemainingPersonalItems();
            StartDeliveryToStorage();
            return;
        }

        // 부분 줍기
        int pickedAmount = currentPersonalItem.PickUpPartial(unit);
        pickupTimer = 0f;

        if (pickedAmount > 0)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: {resource?.ResourceName} x{pickedAmount} 주움");
        }

        // 아이템이 Destroy되었는지 확인 (다음 프레임에서 처리됨)
        // PickUpPartial에서 Destroy 호출 시 바로 null이 됨
        if (currentPersonalItem == null || !currentPersonalItem || currentPersonalItem.Amount <= 0)
        {
            currentPersonalItem = null;

            // 다음 아이템 시도
            TryPickupPersonalItems();

            // 줍을 아이템이 없으면 다음 작업으로
            if (currentPersonalItem == null)
            {
                ReturnToPreviousTaskOrIdle();
            }
        }
        else
        {
            // 같은 아이템이 남아있음 = 인벤 꽉 참
            Debug.Log($"[UnitAI] {unit.UnitName}: 부분 줍기 후 인벤 가득 → 저장고로");
            GiveUpRemainingPersonalItems();
            StartDeliveryToStorage();
        }
    }

    private void GiveUpRemainingPersonalItems()
    {
        // ★ Unity null 체크
        if (currentPersonalItem != null && currentPersonalItem)
        {
            currentPersonalItem.OwnerGiveUp();
        }
        currentPersonalItem = null;

        foreach (var item in personalItems)
        {
            // ★ Unity null 체크
            if (item != null && item)
                item.OwnerGiveUp();
        }
        personalItems.Clear();
    }

    // ==================== 창고 배달 ====================

    private void StartDeliveryToStorage()
    {
        // 저장고 찾기 (캐시 무시하고 직접 검색)
        targetStorage = FindNearestStorageDirectly();

        if (targetStorage == null)
        {
            // ★ 저장고가 없으면 대기 상태로 전환
            Debug.Log($"[UnitAI] {unit.UnitName}: 저장고 없음 → 대기 상태");
            SetBehaviorAndPriority(AIBehaviorState.WaitingForStorage, TaskPriorityLevel.FreeWill);
            return;
        }

        // 저장고 접근 위치 계산
        storagePosition = targetStorage.GetNearestAccessPoint(transform.position);

        // 저장고로 이동 시작
        deliveryPhase = DeliveryPhase.MovingToStorage;
        depositTimer = 0f;

        unit.MoveTo(storagePosition);
        SetBehaviorAndPriority(AIBehaviorState.DeliveringToStorage, TaskPriorityLevel.ItemPickup);

        Debug.Log($"[UnitAI] {unit.UnitName}: 저장고로 이동 시작 → {targetStorage.name}");
    }

    /// <summary>
    /// ★ 저장고 대기 상태 업데이트
    /// </summary>
    private void UpdateWaitingForStorage()
    {
        // 주기적으로 체크 (1초마다)
        if (Time.time - lastStorageCheckTime < 1f) return;
        lastStorageCheckTime = Time.time;

        // ★ 건설 작업 먼저 찾기
        if (TryPullConstructionTask())
        {
            return;
        }

        // 저장고 다시 찾기
        targetStorage = FindNearestStorageDirectly();

        if (targetStorage != null)
        {
            // 저장고 발견! → 저장하러 이동
            Debug.Log($"[UnitAI] {unit.UnitName}: 저장고 발견! → {targetStorage.name}");

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
                // 잘못된 상태면 Idle로
                ClearDeliveryState();
                SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
                break;
        }
    }

    private void UpdateMovingToStorage()
    {
        if (targetStorage == null)
        {
            // 저장고가 사라졌으면 즉시 저장
            unit.Inventory.DepositToStorage();
            ClearDeliveryState();
            ReturnToPreviousTaskOrIdle();
            return;
        }

        // ★ 저장고 범위 내에 있는지 확인 (사각형)
        bool isInRange = targetStorage.IsInAccessArea(transform.position);

        // 저장고에 도착했는지 확인
        if (isInRange || unit.HasArrivedAtDestination())
        {
            // 저장 단계로 전환
            deliveryPhase = DeliveryPhase.Depositing;
            depositTimer = 0f;
            unit.StopMoving();
            Debug.Log($"[UnitAI] {unit.UnitName}: 저장고 도착, 저장 시작 ({depositDuration}초)");
            return;
        }

        // 아직 이동 중 - 경로가 막혔으면 다시 시도
        if (unit.HasArrivedAtDestination() && !isInRange)
        {
            unit.MoveTo(storagePosition);
        }
    }

    private void UpdateDepositing()
    {
        depositTimer += Time.deltaTime;

        // 저장 진행 중 (애니메이션 등 추가 가능)
        // TODO: 저장 애니메이션 트리거

        if (depositTimer >= depositDuration)
        {
            // 저장 완료
            PerformDeposit();
            ClearDeliveryState();

            Debug.Log($"[UnitAI] {unit.UnitName}: 저장 완료!");

            // 이전 작업으로 복귀하거나 새 작업 찾기
            ReturnToPreviousTaskOrIdle();
        }
    }

    /// <summary>
    /// 실제 저장 수행
    /// </summary>
    private void PerformDeposit()
    {
        if (targetStorage != null && targetStorage.IsMainStorage)
        {
            // 메인 저장고면 ResourceManager로 저장
            unit.Inventory.DepositToStorage();
        }
        else if (targetStorage != null)
        {
            // 일반 저장고면 StorageComponent에 저장
            foreach (var slot in unit.Inventory.Slots)
            {
                if (!slot.IsEmpty)
                {
                    targetStorage.AddItem(slot.Resource, slot.Amount);
                }
            }
            // 인벤토리 비우기 (드롭 없이)
            unit.Inventory.Clear();
        }
        else
        {
            // 저장고가 없으면 ResourceManager로
            unit.Inventory.DepositToStorage();
        }
    }

    /// <summary>
    /// 저장소 배달 상태 초기화
    /// </summary>
    private void ClearDeliveryState()
    {
        deliveryPhase = DeliveryPhase.None;
        targetStorage = null;
        depositTimer = 0f;
    }

    /// <summary>
    /// 이전 작업으로 복귀하거나 새 작업 찾기 또는 Idle
    /// </summary>
    private void ReturnToPreviousTaskOrIdle()
    {
        // 1. 이전 작업으로 복귀 시도
        if (previousTask != null && TaskManager.Instance != null)
        {
            var task = previousTask;
            previousTask = null;

            if (task.State != PostedTaskState.Completed &&
                task.State != PostedTaskState.Cancelled &&
                TaskManager.Instance.TakeTask(task, unit))
            {
                AssignTask(task);
                Debug.Log($"[UnitAI] {unit.UnitName}: 이전 작업으로 복귀");
                return;
            }
        }

        // 2. 새 작업 찾기
        if (TryPullTask())
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 새 작업 시작");
            return;
        }

        // 3. 작업 없으면 Idle
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    /// <summary>
    /// 가장 가까운 저장고 찾기
    /// </summary>
    private StorageComponent FindNearestStorage()
    {
        // BuildingManager를 통해 찾기
        if (BuildingManager.Instance != null)
        {
            var storage = BuildingManager.Instance.GetNearestStorage(transform.position, mustHaveSpace: false);
            if (storage != null)
            {
                return storage.GetComponent<StorageComponent>();
            }
        }

        // BuildingManager가 없으면 직접 찾기
        var storages = FindObjectsOfType<StorageComponent>();
        StorageComponent nearest = null;
        float nearestDist = storageSearchRadius;

        foreach (var storage in storages)
        {
            // 완성된 건물만
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

        return nearest;
    }

    /// <summary>
    /// ★ 저장고 직접 검색 (캐시 사용 안함)
    /// </summary>
    private StorageComponent FindNearestStorageDirectly()
    {
        var storages = FindObjectsOfType<StorageComponent>();
        StorageComponent nearest = null;
        float nearestDist = storageSearchRadius;

        foreach (var storage in storages)
        {
            // 완성된 건물만 (Building 컴포넌트가 없으면 완성된 것으로 간주)
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

        // 캐시 갱신
        hasStorageBuilding = nearest != null;

        return nearest;
    }

    /// <summary>
    /// ★ 저장고가 존재하는지 확인 (캐시 사용)
    /// </summary>
    private bool HasAnyStorage()
    {
        // 캐시된 결과 사용 (5초마다 갱신)
        if (Time.time - lastStorageCheckTime < STORAGE_CHECK_INTERVAL)
        {
            return hasStorageBuilding;
        }

        lastStorageCheckTime = Time.time;

        // BuildingManager를 통해 확인
        if (BuildingManager.Instance != null)
        {
            var storage = BuildingManager.Instance.GetNearestStorage(transform.position, mustHaveSpace: false);
            hasStorageBuilding = storage != null;
            return hasStorageBuilding;
        }

        // BuildingManager가 없으면 직접 확인
        var storages = FindObjectsOfType<StorageComponent>();
        foreach (var storage in storages)
        {
            var building = storage.GetComponent<Building>();
            if (building == null || building.CurrentState == BuildingState.Completed)
            {
                hasStorageBuilding = true;
                return true;
            }
        }

        hasStorageBuilding = false;
        return false;
    }

    /// <summary>
    /// ★ 인벤토리를 비워야 하는지 확인
    /// 조건: 모든 슬롯 사용 중 AND 해당 아이템 공간 없음
    /// </summary>
    private bool ShouldDepositInventory(ResourceItemSO resourceToAdd = null)
    {
        // 인벤토리가 비어있으면 저장할 필요 없음
        if (unit.Inventory.IsEmpty) return false;

        // 모든 슬롯이 사용 중인지 확인
        bool allSlotsUsed = unit.Inventory.UsedSlots >= unit.Inventory.MaxSlots;

        if (!allSlotsUsed)
        {
            // 빈 슬롯이 있으면 저장 필요 없음
            return false;
        }

        // 모든 슬롯이 사용 중일 때
        if (resourceToAdd != null)
        {
            // 추가하려는 아이템이 들어갈 공간이 없으면 저장 필요
            return !unit.Inventory.CanAddAny(resourceToAdd);
        }

        // 추가하려는 아이템이 없으면 (일반 IsFull 체크)
        return unit.Inventory.IsFull;
    }

    /// <summary>
    /// ★ 작업이 없을 때 인벤토리 정리가 필요한지 확인
    /// 조건: 작업이 없고 + 인벤이 꽉 찼을 때만
    /// </summary>
    private bool ShouldDepositWhenIdle()
    {
        // 인벤토리가 비어있으면 저장할 필요 없음
        if (unit.Inventory.IsEmpty) return false;

        // 작업이 있으면 저장하지 않음
        if (TaskManager.Instance != null)
        {
            var availableTask = TaskManager.Instance.FindNearestTask(unit);
            if (availableTask != null) return false;
        }

        // ★ 인벤이 꽉 찼을 때만 저장 (모든 슬롯이 가득 찬 상태)
        return unit.Inventory.IsFull;
    }

    // ==================== 음식 찾기 ====================

    private bool TrySeekFood()
    {
        var food = FindNearestFood();
        if (food == null) return false;

        bb.NearestFood = food;
        bb.TargetPosition = food.transform.position;
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

    // ★ 외부에서 음식 위치 설정
    public void SetFoodTarget(Vector3 foodPosition)
    {
        if (bb.Hunger > hungerSeekThreshold) return;

        bb.TargetPosition = foodPosition;
        unit.MoveTo(foodPosition);
        SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
    }

    // ==================== 플레이어 명령 ====================

    public void GiveCommand(UnitCommand command)
    {
        bb.HasPlayerCommand = true;
        bb.PlayerCommand = command;
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
        if (Random.value < 0.3f)
            StartWandering();
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
            AIBehaviorState.Idle or AIBehaviorState.WaitingForStorage => UnitState.Idle,
            AIBehaviorState.Working or AIBehaviorState.WorkingAtStation => UnitState.Working,
            AIBehaviorState.SeekingFood => UnitState.Eating,
            AIBehaviorState.Wandering or AIBehaviorState.ExecutingCommand => UnitState.Moving,
            AIBehaviorState.PickingUpItem or AIBehaviorState.DeliveringToStorage => UnitState.Moving,
            _ => UnitState.Idle
        });
    }

    private void OnHungerCritical()
    {
        Debug.Log($"[UnitAI] {unit.UnitName}: 배고픔!");
    }

    // ==================== 디버그 ====================

    private void OnDrawGizmos()
    {
        if (currentBehavior != AIBehaviorState.Working && currentBehavior != AIBehaviorState.WorkingAtStation)
            return;

        if (!taskContext.HasTask)
            return;

        if (taskContext.Task?.Data?.Type == TaskType.Construct)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawSphere(debugBuildingCenter, 0.15f);

            Gizmos.color = Color.black;
            Vector3 boxSize = new Vector3(debugBoxHalfX * 2, 0.1f, debugBoxHalfZ * 2);
            Gizmos.DrawWireCube(debugBuildingCenter, boxSize);
        }

        Gizmos.color = taskContext.Phase switch
        {
            TaskPhase.MovingToWork => Color.yellow,
            TaskPhase.Working => Color.green,
            TaskPhase.Relocating => Color.cyan,
            _ => Color.gray
        };
        Gizmos.DrawSphere(taskContext.WorkPosition, 0.1f);
        Gizmos.DrawLine(transform.position, taskContext.WorkPosition);
    }

    // ==================== 호환성 ====================

    public void AddPlayerCommand(UnitTask task) { }
    public void AddPlayerCommandImmediate(UnitTask task) { }
    public void OnTaskCompleted(UnitTask task) { }
    public void OnFoodEaten(float nutrition) => bb?.Eat(nutrition);
}