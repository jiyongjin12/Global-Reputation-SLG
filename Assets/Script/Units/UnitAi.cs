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
    ExecutingCommand
}

public enum TaskPriorityLevel
{
    PlayerCommand = 0,
    Survival = 1,
    Construction = 2,
    ItemPickup = 3,
    Harvest = 4,
    FreeWill = 5
}

public enum TaskPhase
{
    None,
    MovingToWork,
    Working,
    Relocating
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

    private Vector3 debugBuildingCenter;
    private float debugBoxHalfX, debugBoxHalfZ;

    // ==================== Properties ====================

    public AIBehaviorState CurrentBehavior => currentBehavior;
    public TaskPriorityLevel CurrentPriority => currentPriority;
    public bool HasTask => taskContext.HasTask;

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

        // ★ 개인 아이템 줍기 중이면 유지 (currentPersonalItem도 체크!)
        if (currentPersonalItem != null || personalItems.Count > 0)
        {
            // 아직 PickingUpItem 상태가 아니면 시작
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
            if (TryPullTask())
                return;
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
            case AIBehaviorState.PickingUpItem:
                UpdatePickingUpItem();
                break;
            case AIBehaviorState.SeekingFood:
                UpdateSeekingFood();
                break;
            case AIBehaviorState.DeliveringToStorage:
                UpdateDeliveryToStorage();
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

        float gather = unit.DoGather(node.Data?.NodeType);
        var drops = node.Harvest(gather);

        // ★ 드롭된 아이템은 개인 아이템으로 등록만! (즉시 파괴하지 않음)
        // 애니메이션이 끝난 후 TryPickupPersonalItems()에서 줍게 됨
        foreach (var drop in drops)
        {
            AddPersonalItem(drop);
        }

        if (node.IsDepleted)
        {
            CompleteCurrentTask();
            TryPickupPersonalItems();
        }
        else if (unit.Inventory.IsFull)
        {
            previousTask = task;
            TaskManager.Instance?.LeaveTask(task, unit);
            taskContext.Clear();
            bb.CurrentTask = null;
            StartDeliveryToStorage();
        }
    }

    // ==================== 작업 완료 ====================

    private void CompleteCurrentTask()
    {
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

        if (currentPersonalItem == item)
            currentPersonalItem = null;
    }

    private void TryPickupPersonalItems()
    {
        personalItems.RemoveAll(item => item == null || item.Owner != unit);

        if (personalItems.Count == 0) return;

        currentPersonalItem = personalItems[0];
        personalItems.RemoveAt(0);

        if (!currentPersonalItem.IsAnimating)
            currentPersonalItem.Reserve(unit);

        unit.MoveTo(currentPersonalItem.transform.position);
        SetBehaviorAndPriority(AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
        pickupTimer = 0f;
    }

    private void UpdatePickingUpPersonalItem()
    {
        if (currentPersonalItem == null)
        {
            TryPickupPersonalItems();
            if (currentPersonalItem == null)
                SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        if (currentPersonalItem.IsAnimating)
        {
            float distToItem = Vector3.Distance(transform.position, currentPersonalItem.transform.position);
            if (distToItem > pickupRadius && unit.HasArrivedAtDestination())
                unit.MoveTo(currentPersonalItem.transform.position);
            pickupTimer = 0f;
            return;
        }

        if (!currentPersonalItem.IsReserved)
            currentPersonalItem.Reserve(unit);

        float dist = Vector3.Distance(transform.position, currentPersonalItem.transform.position);

        if (dist > pickupRadius)
        {
            if (unit.HasArrivedAtDestination())
                unit.MoveTo(currentPersonalItem.transform.position);
            pickupTimer = 0f;
            return;
        }

        pickupTimer += Time.deltaTime;

        if (pickupTimer >= itemPickupDuration)
        {
            if (!unit.Inventory.IsFull && currentPersonalItem != null)
            {
                var resource = currentPersonalItem.Resource;
                var amount = currentPersonalItem.Amount;

                if (currentPersonalItem.PickUp(unit))
                {
                    unit.Inventory.AddItem(resource, amount);
                    currentPersonalItem = null;
                    pickupTimer = 0f;
                    TryPickupPersonalItems();

                    if (personalItems.Count == 0 && currentPersonalItem == null)
                        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
                }
                else
                {
                    pickupTimer = 0f;
                }
            }
            else if (unit.Inventory.IsFull)
            {
                GiveUpRemainingPersonalItems();
                StartDeliveryToStorage();
            }
        }
    }

    private void GiveUpRemainingPersonalItems()
    {
        if (currentPersonalItem != null)
        {
            currentPersonalItem.OwnerGiveUp();
            currentPersonalItem = null;
        }

        foreach (var item in personalItems)
        {
            if (item != null)
                item.OwnerGiveUp();
        }
        personalItems.Clear();
    }

    // ==================== 창고 배달 ====================

    private void StartDeliveryToStorage()
    {
        unit.Inventory.DepositToStorage();

        Debug.Log($"[UnitAI] {unit.UnitName}: 자원 저장 완료");

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

        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    private void UpdateDeliveryToStorage()
    {
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
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
            AIBehaviorState.Idle => UnitState.Idle,
            AIBehaviorState.Working => UnitState.Working,
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
        if (currentBehavior != AIBehaviorState.Working || !taskContext.HasTask)
            return;

        if (taskContext.Task?.Data?.Type != TaskType.Construct)
            return;

        Gizmos.color = Color.white;
        Gizmos.DrawSphere(debugBuildingCenter, 0.15f);

        Gizmos.color = Color.black;
        Vector3 boxSize = new Vector3(debugBoxHalfX * 2, 0.1f, debugBoxHalfZ * 2);
        Gizmos.DrawWireCube(debugBuildingCenter, boxSize);

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