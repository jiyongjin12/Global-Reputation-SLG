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
    WaitingForStorage,
    ExecutingCommand,
    WorkingAtStation,
    Socializing         // ★ 사회적 상호작용 중
}

public enum TaskPriorityLevel
{
    PlayerCommand = 0,
    Survival = 1,
    Construction = 2,
    Workstation = 3,
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

public enum DeliveryPhase
{
    None,
    MovingToStorage,
    Depositing
}

// ==================== Main Class ====================

[RequireComponent(typeof(Unit))]
public class UnitAI : MonoBehaviour
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

    // ==================== Serialized Fields ====================

    [Header("=== 기본 설정 ===")]
    [SerializeField] private float decisionInterval = 0.5f;
    [SerializeField] private float workRadius = 0.8f;
    [SerializeField] private float wanderRadius = 10f;

    [Header("=== 배고픔 설정 ===")]
    [SerializeField] private float hungerDecreasePerMinute = 3f;
    [SerializeField] private float foodSearchRadius = 20f;
    [SerializeField] private float hungerSeekThreshold = 50f;
    [SerializeField] private float hungerCriticalThreshold = 20f;

    [Header("=== 아이템 줍기 ===")]
    [SerializeField] private float itemPickupDuration = 1f;
    [SerializeField] private float pickupRadius = 1.5f;

    [Header("=== 저장고 배달 ===")]
    [SerializeField] private float depositDuration = 2f;
    [SerializeField] private float storageSearchRadius = 50f;

    [Header("=== 자석 흡수 ===")]
    [SerializeField] private float magnetAbsorbRadius = 3f;

    [Header("=== 사회적 상호작용 ===")]
    [SerializeField] private float socialInteractionChance = 0.4f;
    [SerializeField] private float socialSearchRadius = 8f;

    [Header("=== 디버그 ===")]
    [SerializeField] private AIBehaviorState currentBehavior = AIBehaviorState.Idle;
    [SerializeField] private TaskPriorityLevel currentPriority = TaskPriorityLevel.FreeWill;

    // ==================== Private Fields ====================

    // Components
    private Unit unit;
    private UnitBlackboard bb;
    private NavMeshAgent agent;
    private UnitMovement movement;
    private UnitSocialInteraction socialInteraction;

    // Task Context
    private TaskContext taskContext = new();
    private PostedTask previousTask;

    // Timers
    private float lastDecisionTime;
    private float pickupTimer;
    private float magnetAbsorbTimer;
    private float lastStorageCheckTime = -10f;

    // Item Management
    private List<DroppedItem> personalItems = new();
    private List<DroppedItem> pendingMagnetItems = new();
    private DroppedItem currentPersonalItem;

    // Workstation
    private IWorkstation currentWorkstation;
    private bool isWorkstationWorkStarted;

    // Delivery
    private DeliveryPhase deliveryPhase = DeliveryPhase.None;
    private Vector3 storagePosition;
    private StorageComponent targetStorage;
    private float depositTimer;

    // Social
    private Unit socialTarget;
    private bool isApproachingForSocial;

    // Cache
    private bool hasStorageBuilding;
    private const float STORAGE_CHECK_INTERVAL = 5f;
    private const float MAGNET_ABSORB_INTERVAL = 0.2f;

    // Debug
    private Vector3 debugBuildingCenter;
    private float debugBoxHalfX, debugBoxHalfZ;

    // ==================== Properties ====================

    public AIBehaviorState CurrentBehavior => currentBehavior;
    public TaskPriorityLevel CurrentPriority => currentPriority;
    public bool HasTask => taskContext.HasTask;
    public bool IsIdle => currentBehavior == AIBehaviorState.Idle || currentBehavior == AIBehaviorState.Wandering;

    // ==================== Unity Methods ====================

    private void Awake()
    {
        unit = GetComponent<Unit>();
        agent = GetComponent<NavMeshAgent>();
        movement = GetComponent<UnitMovement>();
        socialInteraction = GetComponent<UnitSocialInteraction>();
    }

    private void Start()
    {
        bb = unit.Blackboard;
        if (bb != null)
        {
            bb.OnHungerCritical += OnHungerCritical;
            bb.OnMentalHealthCritical += OnMentalHealthCritical;
        }
    }

    private void Update()
    {
        if (bb == null || !bb.IsAlive) return;

        // 배고픔 감소
        bb.DecreaseHunger((hungerDecreasePerMinute / 60f) * Time.deltaTime);
        if (bb.IsStarving)
            unit.TakeDamage(Time.deltaTime * 5f);

        // 의사결정
        if (Time.time - lastDecisionTime >= decisionInterval)
        {
            MakeDecision();
            lastDecisionTime = Time.time;
        }

        // 자석 아이템 흡수 체크
        TryAbsorbNearbyMagnetItems();

        // 현재 행동 실행
        ExecuteCurrentBehavior();
    }

    // ==================== Decision Making ====================

    private void MakeDecision()
    {
        // 1. 플레이어 명령 최우선
        if (bb.HasPlayerCommand && bb.PlayerCommand != null)
        {
            InterruptCurrentTask();
            ExecutePlayerCommand();
            return;
        }

        // 2. 생존 - 극심한 배고픔
        if (bb.Hunger <= hungerCriticalThreshold && currentPriority > TaskPriorityLevel.Survival)
        {
            if (TrySeekFood())
            {
                InterruptCurrentTask();
                SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
                return;
            }
        }

        // 3. 현재 작업 진행 중이면 유지
        if (taskContext.HasTask) return;

        // 4. 저장고 배달 중이면 유지
        if (currentBehavior == AIBehaviorState.DeliveringToStorage) return;

        // 5. 저장고 대기 중이면 건설 작업만 확인
        if (currentBehavior == AIBehaviorState.WaitingForStorage)
        {
            TryPullConstructionTask();
            return;
        }

        // 6. 상호작용 중이면 유지
        if (currentBehavior == AIBehaviorState.Socializing) return;

        // 7. 아이템 정리
        CleanupItemLists();

        // 8. 개인 아이템 줍기
        if (currentPersonalItem != null || personalItems.Count > 0)
        {
            if (currentBehavior != AIBehaviorState.PickingUpItem)
                TryPickupPersonalItems();
            return;
        }

        // 9. 대기 중인 자석 아이템 처리
        if (HandlePendingMagnetItems()) return;

        // 10. 인벤토리 꽉 참 → 저장고로
        if (unit.Inventory.IsFull)
        {
            if (currentBehavior != AIBehaviorState.DeliveringToStorage)
            {
                StartDeliveryToStorage();
                return;
            }
        }

        // 11. 일반 배고픔 시 음식 찾기
        if (bb.Hunger <= hungerSeekThreshold && TrySeekFood())
        {
            SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
            return;
        }

        // 12. Idle 상태에서의 행동
        if (currentBehavior == AIBehaviorState.Idle || currentBehavior == AIBehaviorState.Wandering)
        {
            // 작업 찾기
            if (TryPullTask()) return;

            // 인벤 비우기
            if (ShouldDepositWhenIdle())
            {
                StartDeliveryToStorage();
                return;
            }
        }

        // 13. 자유 행동
        if (currentBehavior == AIBehaviorState.Idle)
        {
            PerformFreeWill();
        }
    }

    /// <summary>
    /// 아이템 리스트 정리 (null, Destroy된 것, 흡수 중인 것 제거)
    /// </summary>
    private void CleanupItemLists()
    {
        // 현재 아이템 체크
        if (currentPersonalItem != null && (!currentPersonalItem || currentPersonalItem.IsBeingMagneted))
            currentPersonalItem = null;

        // 개인 아이템 리스트 정리
        personalItems.RemoveAll(item => item == null || !item || item.IsBeingMagneted);

        // 자석 아이템 리스트 정리
        pendingMagnetItems.RemoveAll(item => item == null || !item);
    }

    /// <summary>
    /// 대기 중인 자석 아이템 처리
    /// </summary>
    private bool HandlePendingMagnetItems()
    {
        if (pendingMagnetItems.Count == 0) return false;

        // 인벤 꽉 차면 저장고로
        if (unit.Inventory.IsFull)
        {
            StartDeliveryToStorage();
            return true;
        }

        // 흡수 가능한 아이템으로 이동
        if (HasAbsorbablePendingItems())
        {
            MoveToNearestAbsorbableItem();
            return true;
        }

        // 공간 부족 시 저장고로
        StartDeliveryToStorage();
        return true;
    }

    // ==================== Behavior Execution ====================

    private void ExecuteCurrentBehavior()
    {
        switch (currentBehavior)
        {
            case AIBehaviorState.Idle:
                break;
            case AIBehaviorState.Wandering:
                UpdateWandering();
                break;
            case AIBehaviorState.SeekingFood:
                UpdateSeekingFood();
                break;
            case AIBehaviorState.Working:
                UpdateWorking();
                break;
            case AIBehaviorState.WorkingAtStation:
                UpdateWorkingAtStation();
                break;
            case AIBehaviorState.PickingUpItem:
                UpdatePickingUpItem();
                break;
            case AIBehaviorState.DeliveringToStorage:
                UpdateDeliveryToStorage();
                break;
            case AIBehaviorState.WaitingForStorage:
                UpdateWaitingForStorage();
                break;
            case AIBehaviorState.ExecutingCommand:
                UpdatePlayerCommand();
                break;
            case AIBehaviorState.Socializing:
                UpdateSocializing();
                break;
        }
    }

    // ==================== Task Management ====================

    private bool TryPullTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task == null) return false;

        // 채집 작업 시 인벤토리 체크
        if (task.Data.Type == TaskType.Harvest && !CanAcceptHarvestTask(task))
            return false;

        if (!TaskManager.Instance.TakeTask(task, unit))
            return false;

        AssignTask(task);
        return true;
    }

    private bool TryPullConstructionTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task == null || task.Data.Type != TaskType.Construct) return false;

        if (!TaskManager.Instance.TakeTask(task, unit)) return false;

        ClearDeliveryState();
        AssignTask(task);
        return true;
    }

    private bool CanAcceptHarvestTask(PostedTask task)
    {
        if (unit.Inventory.IsFull) return false;

        var node = task.Owner as ResourceNode;
        if (node?.Data?.Drops == null) return true;

        foreach (var drop in node.Data.Drops)
        {
            if (drop.Resource != null && !unit.Inventory.CanAddAny(drop.Resource))
                return false;
        }
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
            case TaskType.Workstation:
                AssignWorkstationTask(task);
                break;
            default:
                AssignGenericTask(task);
                break;
        }
    }

    private void AssignConstructionTask(PostedTask task)
    {
        var building = task.Owner as Building;
        Vector2Int size = building?.Data?.Size ?? Vector2Int.one;
        taskContext.TargetSize = size;

        Vector3 workPos = CalculateDistributedWorkPosition(task.Data.TargetPosition, size);
        MoveToTaskPosition(workPos, AIBehaviorState.Working, TaskPriorityLevel.Construction);
    }

    private void AssignHarvestTask(PostedTask task)
    {
        MoveToTaskPosition(task.Data.TargetPosition, AIBehaviorState.Working, TaskPriorityLevel.Harvest);
    }

    private void AssignPickupTask(PostedTask task)
    {
        MoveToTaskPosition(task.Data.TargetPosition, AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
    }

    private void AssignWorkstationTask(PostedTask task)
    {
        currentWorkstation = task.Owner as IWorkstation;
        isWorkstationWorkStarted = false;

        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        Vector3 workPos = currentWorkstation.WorkPoint?.position ?? task.Data.TargetPosition;
        MoveToTaskPosition(workPos, AIBehaviorState.WorkingAtStation, TaskPriorityLevel.Workstation);
    }

    private void AssignGenericTask(PostedTask task)
    {
        MoveToTaskPosition(task.Data.TargetPosition, AIBehaviorState.Working, TaskPriorityLevel.FreeWill);
    }

    /// <summary>
    /// 공통 이동 로직
    /// </summary>
    private void MoveToTaskPosition(Vector3 position, AIBehaviorState behavior, TaskPriorityLevel priority)
    {
        taskContext.SetMoving(position);
        bb.TargetPosition = position;
        unit.MoveTo(position);
        SetBehaviorAndPriority(behavior, priority);
    }

    private Vector3 CalculateDistributedWorkPosition(Vector3 origin, Vector2Int size)
    {
        Vector3 center = origin + new Vector3(size.x / 2f, 0, size.y / 2f);
        float halfX = size.x / 2f + 0.3f;
        float halfZ = size.y / 2f + 0.3f;

        debugBuildingCenter = center;
        debugBoxHalfX = halfX;
        debugBoxHalfZ = halfZ;

        Vector3 targetPos = center + new Vector3(
            Random.Range(-halfX, halfX), 0,
            Random.Range(-halfZ, halfZ)
        );

        return NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)
            ? hit.position : center;
    }

    public void UpdateAssignedWorkPosition(Vector3 newOrigin, Vector2Int size)
    {
        if (!taskContext.HasTask) return;

        Vector3 newWorkPos = CalculateDistributedWorkPosition(newOrigin, size);
        taskContext.SetRelocating(newWorkPos);
        taskContext.TargetSize = size;
        bb.TargetPosition = newWorkPos;
        unit.MoveTo(newWorkPos);
    }

    // ==================== Working ====================

    private void UpdateWorking()
    {
        if (!taskContext.HasTask || !ValidateTaskTarget(taskContext.Task))
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

    private void UpdateMovingToWork()
    {
        float dist = Vector3.Distance(transform.position, taskContext.WorkPosition);

        if (dist <= workRadius)
        {
            taskContext.SetWorking();
            return;
        }

        if (unit.HasArrivedAtDestination() || agent.velocity.magnitude < 0.1f)
        {
            unit.MoveTo(taskContext.WorkPosition);
        }
    }

    private void UpdateMovingToWorkstation()
    {
        float dist = Vector3.Distance(transform.position, taskContext.WorkPosition);

        if (dist <= workRadius)
        {
            if (!currentWorkstation.AssignWorker(unit))
            {
                CompleteCurrentTask();
                return;
            }
            taskContext.SetWorking();
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

    private void UpdateExecutingWorkstation()
    {
        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        // 작업 시작
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

        // 완료 체크
        var wsComponent = currentWorkstation as WorkstationComponent;
        if (wsComponent != null && !wsComponent.IsWorking && isWorkstationWorkStarted)
        {
            if (currentWorkstation.CanStartWork)
            {
                isWorkstationWorkStarted = false;
            }
            else
            {
                currentWorkstation.ReleaseWorker();
                TaskManager.Instance?.CompleteTask(taskContext.Task);
                CompleteCurrentTask();
            }
        }
    }

    private bool ValidateTaskTarget(PostedTask task)
    {
        return task.Data.Type switch
        {
            TaskType.Construct => task.Owner is Building b && b.CurrentState != BuildingState.Completed,
            TaskType.Harvest => task.Owner is ResourceNode n && !n.IsDepleted,
            TaskType.Workstation => task.Owner is IWorkstation,
            _ => task.Owner != null
        };
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

        // 인벤토리 공간 체크
        ResourceItemSO nodeResource = GetNodeResource(node);
        if (nodeResource != null && ShouldDepositInventory(nodeResource))
        {
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
            previousTask = task;
            CompleteCurrentTask();
            TryPickupPersonalItems();
        }
    }

    private ResourceItemSO GetNodeResource(ResourceNode node)
    {
        if (node?.Data?.Drops == null) return null;
        foreach (var drop in node.Data.Drops)
        {
            if (drop.Resource != null) return drop.Resource;
        }
        return null;
    }

    // ==================== Task Completion ====================

    private void CompleteCurrentTask()
    {
        // 워크스테이션 정리
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
    }

    private void InterruptCurrentTask()
    {
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

    private void TryPickupPersonalItems()
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

    private void UpdatePickingUpPersonalItem()
    {
        // Null 체크
        if (currentPersonalItem == null || !currentPersonalItem || currentPersonalItem.IsBeingMagneted)
        {
            currentPersonalItem = null;
            TryPickupPersonalItems();
            if (currentPersonalItem == null) ReturnToPreviousTaskOrIdle();
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

        if (ShouldDepositInventory(resource))
        {
            GiveUpRemainingPersonalItems();
            StartDeliveryToStorage();
            return;
        }

        int pickedAmount = currentPersonalItem.PickUpPartial(unit);
        pickupTimer = 0f;

        if (pickedAmount > 0) unit.OnItemPickedUp();

        if (pickedAmount >= originalAmount)
        {
            currentPersonalItem = null;
            TryPickupPersonalItems();
            if (currentPersonalItem == null) ReturnToPreviousTaskOrIdle();
        }
        else
        {
            GiveUpRemainingPersonalItems();
            StartDeliveryToStorage();
        }
    }

    private void GiveUpRemainingPersonalItems()
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

    private void TryAbsorbNearbyMagnetItems()
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

    private bool HasAbsorbablePendingItems()
    {
        foreach (var item in pendingMagnetItems)
        {
            if (item == null || !item || item.Resource == null) continue;
            if (unit.Inventory.GetAvailableSpaceFor(item.Resource) >= item.Amount)
                return true;
        }
        return false;
    }

    private void MoveToNearestAbsorbableItem()
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

    private void StartDeliveryToStorage()
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
    }

    private void UpdateWaitingForStorage()
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
            ReturnToPreviousTaskOrIdle();
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
            ReturnToPreviousTaskOrIdle();
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

    private void ClearDeliveryState()
    {
        deliveryPhase = DeliveryPhase.None;
        targetStorage = null;
        depositTimer = 0f;
    }

    private void ReturnToPreviousTaskOrIdle()
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

    private bool ShouldDepositInventory(ResourceItemSO resourceToAdd = null)
    {
        if (unit.Inventory.IsEmpty) return false;

        bool allSlotsUsed = unit.Inventory.UsedSlots >= unit.Inventory.MaxSlots;
        if (!allSlotsUsed) return false;

        if (resourceToAdd != null)
            return !unit.Inventory.CanAddAny(resourceToAdd);

        return unit.Inventory.IsFull;
    }

    private bool ShouldDepositWhenIdle()
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

    private bool TrySeekFood()
    {
        var food = FindNearestFood();
        if (food == null) return false;

        bb.NearestFood = food;
        bb.TargetPosition = food.transform.position;

        // ★ 굶주림 상태면 달리기
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

    // ==================== Player Command ====================

    /// <summary>
    /// 플레이어 명령 받기 (★ 충성도에 따라 무시 가능)
    /// </summary>
    public void GiveCommand(UnitCommand command)
    {
        // 충성도 체크 - 명령 무시 확률
        if (bb.ShouldIgnoreCommand())
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 명령 무시! (충성도: {bb.Loyalty:F0}, 확률: {bb.CommandIgnoreChance * 100:F0}%)");
            bb.IncreaseMentalHealth(2f);  // 반항의 쾌감
            return;
        }

        bb.HasPlayerCommand = true;
        bb.PlayerCommand = command;

        // 상호작용 중이면 취소
        if (currentBehavior == AIBehaviorState.Socializing)
            CancelSocialInteraction();

        InterruptCurrentTask();
        ExecutePlayerCommand();
    }

    /// <summary>
    /// 현재 작업 취소 (명령 대기 상태로 전환 시)
    /// </summary>
    public void CancelCurrentTask()
    {
        // 현재 작업 중단
        InterruptCurrentTask();

        // 상호작용 중단
        if (currentBehavior == AIBehaviorState.Socializing)
            CancelSocialInteraction();

        // 상태 초기화
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        bb.CurrentTask = null;
        bb.HasPlayerCommand = false;
        bb.PlayerCommand = null;

        Debug.Log($"[UnitAI] {unit.UnitName}: 현재 작업 취소됨");
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

    // ==================== Free Will & Social ====================

    /// <summary>
    /// 자유 행동 - ★ 상호작용 우선, 없으면 배회
    /// </summary>
    private void PerformFreeWill()
    {
        // 상호작용 시도
        if (socialInteraction != null && bb.CanSocialize)
        {
            if (Random.value < socialInteractionChance && TryStartSocialInteraction())
                return;
        }

        // 배회
        if (Random.value < 0.3f)
            StartWandering();
    }

    private bool TryStartSocialInteraction()
    {
        var target = FindNearbyIdleUnit();
        if (target == null) return false;

        socialTarget = target;
        float dist = Vector3.Distance(transform.position, target.transform.position);

        if (dist <= socialInteraction.InteractionRadius)
        {
            StartSocialInteraction(target);
            return true;
        }

        // 접근 필요
        isApproachingForSocial = true;
        unit.MoveTo(target.transform.position);
        SetBehaviorAndPriority(AIBehaviorState.Socializing, TaskPriorityLevel.FreeWill);
        return true;
    }

    private Unit FindNearbyIdleUnit()
    {
        var colliders = Physics.OverlapSphere(transform.position, socialSearchRadius);
        List<Unit> candidates = new();

        foreach (var col in colliders)
        {
            if (col.gameObject == gameObject) continue;

            var otherUnit = col.GetComponent<Unit>();
            if (otherUnit == null || !otherUnit.IsAlive) continue;

            var otherBB = otherUnit.Blackboard;
            if (otherBB == null || !otherBB.IsIdle || !otherBB.CanSocialize) continue;

            var otherSocial = otherUnit.GetComponent<UnitSocialInteraction>();
            if (otherSocial != null && otherSocial.IsInteracting) continue;

            candidates.Add(otherUnit);
        }

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    private void StartSocialInteraction(Unit target)
    {
        isApproachingForSocial = false;

        if (socialInteraction != null && socialInteraction.StartInteraction(target))
        {
            SetBehaviorAndPriority(AIBehaviorState.Socializing, TaskPriorityLevel.FreeWill);
        }
        else
        {
            socialTarget = null;
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        }
    }

    private void UpdateSocializing()
    {
        // 접근 중
        if (isApproachingForSocial)
        {
            if (socialTarget == null || !socialTarget.IsAlive)
            {
                CancelSocialInteraction();
                return;
            }

            float dist = Vector3.Distance(transform.position, socialTarget.transform.position);
            if (dist <= socialInteraction.InteractionRadius || unit.HasArrivedAtDestination())
            {
                unit.StopMoving();
                StartSocialInteraction(socialTarget);
            }
            return;
        }

        // 상호작용 진행
        if (socialInteraction != null && !socialInteraction.UpdateInteraction())
        {
            OnSocialInteractionComplete();
        }
    }

    private void OnSocialInteractionComplete()
    {
        socialTarget = null;
        isApproachingForSocial = false;
        unit.GainExpFromAction(ExpGainAction.Social);
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    private void CancelSocialInteraction()
    {
        socialInteraction?.InterruptInteraction();
        socialTarget = null;
        isApproachingForSocial = false;
        unit.StopMoving();
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    private void StartWandering()
    {
        Vector3 randomPoint = transform.position + Random.insideUnitSphere * wanderRadius;
        randomPoint.y = transform.position.y;

        if (NavMesh.SamplePosition(randomPoint, out var hit, wanderRadius, NavMesh.AllAreas))
        {
            // ★ 느긋한 산책 스타일
            if (movement != null)
                movement.StrollTo(hit.position);
            else
                unit.MoveTo(hit.position);

            SetBehaviorAndPriority(AIBehaviorState.Wandering, TaskPriorityLevel.FreeWill);
        }
    }

    private void UpdateWandering()
    {
        if (unit.HasArrivedAtDestination())
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    // ==================== Utility ====================

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
            AIBehaviorState.Socializing => UnitState.Socializing,
            _ => UnitState.Idle
        });
    }

    private void OnHungerCritical()
    {
        Debug.Log($"[UnitAI] {unit.UnitName}: 배고파!");
    }

    private void OnMentalHealthCritical()
    {
        Debug.Log($"[UnitAI] {unit.UnitName}: 정신력 위험!");
    }

    // ==================== Gizmos ====================

    private void OnDrawGizmos()
    {
        if (currentBehavior != AIBehaviorState.Working && currentBehavior != AIBehaviorState.WorkingAtStation)
            return;

        if (!taskContext.HasTask) return;

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

    // ==================== 호환용 ====================

    public void AddPlayerCommand(UnitTask task) { }
    public void AddPlayerCommandImmediate(UnitTask task) { }
    public void OnTaskCompleted(UnitTask task) { }
    public void OnFoodEaten(float nutrition) => bb?.Eat(nutrition);
}