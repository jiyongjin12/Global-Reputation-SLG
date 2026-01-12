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
    WaitingForStorage,  // �� ����� ��� ����
    ExecutingCommand,
    WorkingAtStation  // �� �߰�
}

public enum TaskPriorityLevel
{
    PlayerCommand = 0,
    Survival = 1,
    Construction = 2,
    Workstation = 3,  // �� �߰�
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
/// ����� ��� �ܰ�
/// </summary>
public enum DeliveryPhase
{
    None,
    MovingToStorage,    // ������� �̵� ��
    Depositing          // ���� ��
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

    [Header("=== �⺻ ���� ===")]
    [SerializeField] private float decisionInterval = 0.5f;
    [SerializeField] private float workRadius = 0.8f;
    [SerializeField] private float wanderRadius = 10f;

    [Header("=== ����� ���� ===")]
    [SerializeField] private float hungerDecreasePerMinute = 3f;
    [SerializeField] private float foodSearchRadius = 20f;
    [SerializeField] private float hungerSeekThreshold = 50f;
    [SerializeField] private float hungerCriticalThreshold = 20f;

    [Header("=== ������ �ݱ� ���� ===")]
    [SerializeField] private float itemPickupDuration = 1f;
    [SerializeField] private float pickupRadius = 1.5f;

    [Header("=== ����� ===")]
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

    // �� ��ũ�����̼� �۾���
    private IWorkstation currentWorkstation;
    private bool isWorkstationWorkStarted = false;

    // �� ����� ��޿�
    private DeliveryPhase deliveryPhase = DeliveryPhase.None;
    private Vector3 storagePosition;
    private StorageComponent targetStorage;
    private float depositTimer = 0f;
    [Header("=== ����� ��� ���� ===")]
    [SerializeField] private float depositDuration = 2f;  // ���忡 �ɸ��� �ð�
    [SerializeField] private float storageSearchRadius = 50f;

    private Vector3 debugBuildingCenter;
    private float debugBoxHalfX, debugBoxHalfZ;

    // ==================== Properties ====================

    public AIBehaviorState CurrentBehavior => currentBehavior;
    public TaskPriorityLevel CurrentPriority => currentPriority;
    public bool HasTask => taskContext.HasTask;
    public bool IsIdle => currentBehavior == AIBehaviorState.Idle || currentBehavior == AIBehaviorState.Wandering;

    // �� ����� ���� ���� ĳ��
    private bool hasStorageBuilding = false;
    private float lastStorageCheckTime = -10f;
    private const float STORAGE_CHECK_INTERVAL = 5f;  // 5�ʸ��� üũ

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

        // ★ 자석 아이템 흡수 체크 (매 프레임)
        TryAbsorbNearbyMagnetItems();

        ExecuteCurrentBehavior();
    }

    // ★ 자석 흡수 관련 변수
    [Header("=== 자석 흡수 ===")]
    [SerializeField] private float magnetAbsorbRadius = 3f;
    private float magnetAbsorbTimer = 0f;
    private const float MAGNET_ABSORB_INTERVAL = 0.2f;  // 0.2초마다 체크

    /// <summary>
    /// ★ 근처의 enableMagnet 아이템 흡수 호출
    /// Unit이 판단 → 인벤 계산 → PlayAbsorbAnimation 호출 → 콜백에서 인벤 추가
    /// </summary>
    private void TryAbsorbNearbyMagnetItems()
    {
        // 흡수 간격 체크
        magnetAbsorbTimer += Time.deltaTime;
        if (magnetAbsorbTimer < MAGNET_ABSORB_INTERVAL) return;
        magnetAbsorbTimer = 0f;

        // ★ pendingMagnetItems 정리 (Destroy되거나 흡수 완료된 것 제거)
        pendingMagnetItems.RemoveAll(item => item == null || !item);

        // 인벤토리가 꽉 찼으면 대기 리스트 비우고 종료
        if (unit.Inventory.IsFull)
        {
            if (pendingMagnetItems.Count > 0)
            {
                Debug.Log($"[UnitAI] {unit.UnitName}: 인벤 꽉 참 → 남은 자석 아이템 {pendingMagnetItems.Count}개 포기");
                pendingMagnetItems.Clear();
            }
            return;
        }

        // ★ pendingMagnetItems에서 흡수 가능한 아이템만 필터링
        var itemsToAbsorb = new List<DroppedItem>();

        for (int i = pendingMagnetItems.Count - 1; i >= 0; i--)
        {
            var item = pendingMagnetItems[i];
            if (item == null || !item)
            {
                pendingMagnetItems.RemoveAt(i);
                continue;
            }

            // 이미 흡수 중이면 리스트에서 제거
            if (item.IsBeingMagneted)
            {
                pendingMagnetItems.RemoveAt(i);
                continue;
            }

            // 바닥 튕기기 중이면 대기
            if (item.IsAnimating) continue;

            // 거리 체크
            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist > magnetAbsorbRadius) continue;

            // 인벤에 넣을 수 있는지
            if (!unit.Inventory.CanAddAny(item.Resource))
            {
                // 이 자원은 넣을 수 없음 → 포기
                pendingMagnetItems.RemoveAt(i);
                Debug.Log($"[UnitAI] {unit.UnitName}: {item.Resource?.ResourceName} 공간 없음 → 포기");
                continue;
            }

            int availableSpace = unit.Inventory.GetAvailableSpaceFor(item.Resource);
            if (availableSpace <= 0)
            {
                pendingMagnetItems.RemoveAt(i);
                continue;
            }

            // 흡수 가능! 리스트에 추가
            itemsToAbsorb.Add(item);
            pendingMagnetItems.RemoveAt(i);
        }

        // ★ 흡수 애니메이션 시작
        foreach (var item in itemsToAbsorb)
        {
            // 콜백에서 인벤토리 추가
            item.PlayAbsorbAnimation(unit, (resource, amount) =>
            {
                // ★ 콜백: 인벤에 실제로 추가
                int added = unit.Inventory.AddItem(resource, amount);
                Debug.Log($"[UnitAI] {unit.UnitName}: {resource?.ResourceName} x{added} 인벤 추가 완료");
            });

            Debug.Log($"[UnitAI] {unit.UnitName}: {item.Resource?.ResourceName} x{item.Amount} 흡수 시작");
        }
    }

    // ==================== �ǻ���� ====================

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

        // �� ����� ��� ���̸� ���
        if (currentBehavior == AIBehaviorState.DeliveringToStorage)
            return;

        // �� ����� ��� ���̸� �Ǽ� �۾��� �ޱ�
        if (currentBehavior == AIBehaviorState.WaitingForStorage)
        {
            if (TryPullConstructionTask())
                return;
            // �Ǽ� �۾� ������ ��� ���
            return;
        }

        // ★ Unity null 체크 + 흡수 중인 아이템 제외
        if (currentPersonalItem != null && !currentPersonalItem)
            currentPersonalItem = null;
        if (currentPersonalItem != null && currentPersonalItem.IsBeingMagneted)
            currentPersonalItem = null;
        personalItems.RemoveAll(item => item == null || !item || item.IsBeingMagneted);

        // ★ 흡수 대기 아이템 정리 (Destroy되거나 흡수 완료된 것 제거)
        pendingMagnetItems.RemoveAll(item => item == null || !item);

        // 개인 아이템 줍기 (흡수 중 아닌 것만)
        if (currentPersonalItem != null || personalItems.Count > 0)
        {
            if (currentBehavior != AIBehaviorState.PickingUpItem)
                TryPickupPersonalItems();
            return;
        }

        // ★ 흡수 대기 아이템이 있으면 새 작업 받지 않고 대기
        if (pendingMagnetItems.Count > 0)
        {
            // TryAbsorbNearbyMagnetItems()에서 처리됨 (Update에서 호출)
            // 새 작업 받지 않고 현재 위치에서 대기
            return;
        }

        if (bb.Hunger <= hungerSeekThreshold && TrySeekFood())
        {
            SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
            return;
        }

        if (currentBehavior == AIBehaviorState.Idle || currentBehavior == AIBehaviorState.Wandering)
        {
            // �� �۾��� ���� ã��
            if (TryPullTask())
                return;

            // �� �۾��� ���� �κ��� �� á���� �������
            if (ShouldDepositWhenIdle())
            {
                Debug.Log($"[UnitAI] {unit.UnitName}: �۾� ���� + �κ��丮 ������ �� �������");
                StartDeliveryToStorage();
                return;
            }
        }

        if (currentBehavior == AIBehaviorState.Idle)
        {
            PerformFreeWill();
        }
    }

    // ==================== �۾� Pull ====================

    private bool TryPullTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task == null) return false;

        // ★ 채집 작업인 경우 인벤토리 체크
        if (task.Data.Type == TaskType.Harvest)
        {
            // 인벤이 완전히 꽉 찼으면 채집 작업 받지 않음
            if (unit.Inventory.IsFull)
            {
                Debug.Log($"[UnitAI] {unit.UnitName}: 인벤 꽉 참 → 채집 작업 스킵, 저장고로");
                return false;
            }

            // 해당 자원 공간이 없으면 채집 작업 받지 않음
            var node = task.Owner as ResourceNode;
            if (node?.Data?.Drops != null)
            {
                foreach (var drop in node.Data.Drops)
                {
                    if (drop.Resource != null)
                    {
                        if (!unit.Inventory.CanAddAny(drop.Resource))
                        {
                            Debug.Log($"[UnitAI] {unit.UnitName}: {drop.Resource.ResourceName} 공간 없음 → 채집 스킵");
                            return false;
                        }
                        break;
                    }
                }
            }
        }

        if (!TaskManager.Instance.TakeTask(task, unit))
            return false;

        AssignTask(task);
        return true;
    }

    /// <summary>
    /// �� �Ǽ� �۾��� �������� (����� ��� �߿��� �Ǽ��� ��)
    /// </summary>
    private bool TryPullConstructionTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task == null) return false;

        // �Ǽ� �۾��� ����
        if (task.Data.Type != TaskType.Construct)
            return false;

        if (!TaskManager.Instance.TakeTask(task, unit))
            return false;

        // ����� ��� ���� ����
        ClearDeliveryState();

        AssignTask(task);
        Debug.Log($"[UnitAI] {unit.UnitName}: ����� ��� �� �Ǽ� �۾� ����");
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
            case TaskType.Workstation:  // �� �߰�
                AssignWorkstationTask(task);
                break;
            default:
                AssignGenericTask(task);
                break;
        }

        Debug.Log($"[UnitAI] {unit.UnitName}: �۾� �Ҵ� - {task.Data.Type}, Phase: {taskContext.Phase}");
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

    // �� ��ũ�����̼� �۾� �Ҵ�
    private void AssignWorkstationTask(PostedTask task)
    {
        currentWorkstation = task.Owner as IWorkstation;
        isWorkstationWorkStarted = false;

        if (currentWorkstation == null)
        {
            Debug.LogWarning("[UnitAI] ��ũ�����̼��� ã�� �� �����ϴ�.");
            CompleteCurrentTask();
            return;
        }

        Vector3 workPos = currentWorkstation.WorkPoint?.position ?? task.Data.TargetPosition;

        taskContext.SetMoving(workPos);
        bb.TargetPosition = workPos;
        unit.MoveTo(workPos);

        SetBehaviorAndPriority(AIBehaviorState.WorkingAtStation, TaskPriorityLevel.Workstation);

        Debug.Log($"[UnitAI] {unit.UnitName}: ��ũ�����̼� �۾� �Ҵ� - {currentWorkstation.TaskType}");
    }

    private void AssignGenericTask(PostedTask task)
    {
        taskContext.SetMoving(task.Data.TargetPosition);
        bb.TargetPosition = task.Data.TargetPosition;
        unit.MoveTo(task.Data.TargetPosition);

        SetBehaviorAndPriority(AIBehaviorState.Working, TaskPriorityLevel.FreeWill);
    }

    // ==================== �۾� ��ġ ��� ====================

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

        Debug.Log($"[UnitAI] {unit.UnitName}: ���ġ �� {newWorkPos}");
    }

    // ==================== �ൿ ���� ====================

    private void ExecuteCurrentBehavior()
    {
        switch (currentBehavior)
        {
            case AIBehaviorState.Working:
                UpdateWorking();
                break;
            case AIBehaviorState.WorkingAtStation:  // �� �߰�
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
            case AIBehaviorState.WaitingForStorage:  // �� ����� ���
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

    // �� ��ũ�����̼� �۾� ������Ʈ
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
            // �۾��� ����
            if (!currentWorkstation.AssignWorker(unit))
            {
                Debug.LogWarning($"[UnitAI] {unit.UnitName}: ��ũ�����̼� �۾��� ���� ����!");
                CompleteCurrentTask();
                return;
            }

            taskContext.SetWorking();
            Debug.Log($"[UnitAI] {unit.UnitName}: ��ũ�����̼� ����, �۾� ����");
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

        // �۾� ���� (�� ����)
        if (!isWorkstationWorkStarted && currentWorkstation.CanStartWork)
        {
            currentWorkstation.StartWork();
            isWorkstationWorkStarted = true;
        }

        // �۾� ����
        taskContext.WorkTimer += Time.deltaTime;
        if (taskContext.WorkTimer >= 1f)
        {
            taskContext.WorkTimer = 0f;
            float workAmount = unit.DoWork();
            currentWorkstation.DoWork(workAmount);
        }

        // �۾� �Ϸ� üũ
        var wsComponent = currentWorkstation as WorkstationComponent;
        if (wsComponent != null)
        {
            if (!wsComponent.IsWorking && isWorkstationWorkStarted)
            {
                // ���� �۾��� �ִ��� Ȯ��
                if (currentWorkstation.CanStartWork)
                {
                    isWorkstationWorkStarted = false;
                    // �ٽ� �۾� ����
                }
                else
                {
                    // ��� �۾� �Ϸ�
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
            Debug.Log($"[UnitAI] {unit.UnitName}: �۾� ��ġ ����, �۾� ����");
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

        // �� ä���� �ڿ� Ȯ��
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

        // �� ������� ���� �ϴ��� Ȯ��
        // ����: ��� ���� ��� �� AND �ش� ������ ���� ����
        if (nodeResource != null && ShouldDepositInventory(nodeResource))
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: �κ� ������ + {nodeResource.ResourceName} ���� ���� �� ������� �̵�");
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
            // �� ��� ���� �� ���� �۾� ���� (���� ���� �ٸ� ��� ã���)
            previousTask = task;
            CompleteCurrentTask();
            TryPickupPersonalItems();
        }
    }

    // ==================== �۾� �Ϸ� ====================

    private void CompleteCurrentTask()
    {
        // �� ��ũ�����̼� ����
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

        Debug.Log($"[UnitAI] {unit.UnitName}: �۾� �Ϸ�, Idle ����");
    }

    private void InterruptCurrentTask()
    {
        // �� ��ũ�����̼� ����
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

    // ==================== ������ �ݱ� ====================

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

    // ==================== ���� ������ ====================

    public void AddPersonalItem(DroppedItem item)
    {
        if (item == null || personalItems.Contains(item)) return;

        // ★ enableMagnet 아이템은 별도 리스트로 관리
        if (item.EnableMagnet)
        {
            if (!pendingMagnetItems.Contains(item))
            {
                pendingMagnetItems.Add(item);
                item.SetOwner(unit);
                Debug.Log($"[UnitAI] {unit.UnitName}: enableMagnet 아이템 → 흡수 대기 리스트 추가 ({pendingMagnetItems.Count}개)");
            }
            return;
        }

        personalItems.Add(item);
        item.SetOwner(unit);
    }

    // ★ 흡수 대기 중인 자석 아이템 리스트
    private List<DroppedItem> pendingMagnetItems = new List<DroppedItem>();

    public void RemovePersonalItem(DroppedItem item)
    {
        if (item == null) return;

        personalItems.Remove(item);

        // �� Unity null üũ�� ��
        if (currentPersonalItem != null && currentPersonalItem == item)
            currentPersonalItem = null;
    }

    private void TryPickupPersonalItems()
    {
        // ★ Unity null 체크 + 흡수 중인 아이템 제외
        if (currentPersonalItem != null && !currentPersonalItem)
            currentPersonalItem = null;

        // ★ 흡수 중인 아이템(IsBeingMagneted)도 제외
        personalItems.RemoveAll(item =>
            item == null ||
            !item ||
            item.Owner != unit ||
            item.IsBeingMagneted);  // 흡수 중이면 줍기 대상에서 제외

        if (personalItems.Count == 0)
        {
            return;
        }

        currentPersonalItem = personalItems[0];
        personalItems.RemoveAt(0);

        // ★ 다시 체크 (흡수 중이면 스킵)
        if (currentPersonalItem == null || !currentPersonalItem || currentPersonalItem.IsBeingMagneted)
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
            TryPickupPersonalItems();
            if (currentPersonalItem == null)
            {
                ReturnToPreviousTaskOrIdle();
            }
            return;
        }

        // ★ 흡수 중인 아이템이면 스킵 (다른 곳에서 흡수 처리 중)
        if (currentPersonalItem.IsBeingMagneted)
        {
            currentPersonalItem = null;
            TryPickupPersonalItems();
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
        if (pickupTimer < itemPickupDuration)
            return;

        // === �ݱ� ���� ===
        var resource = currentPersonalItem.Resource;
        int originalAmount = currentPersonalItem.Amount;  // �� ���� �� ����

        // �κ��丮 ���� üũ
        if (ShouldDepositInventory(resource))
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: �κ� ������ + {resource?.ResourceName} ���� ���� �� ������� �̵�");
            GiveUpRemainingPersonalItems();
            StartDeliveryToStorage();
            return;
        }

        // �κ� �ݱ�
        int pickedAmount = currentPersonalItem.PickUpPartial(unit);
        pickupTimer = 0f;

        if (pickedAmount > 0)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: {resource?.ResourceName} x{pickedAmount}/{originalAmount} �ֿ�");
        }

        // �� �ݱ� ����� ���� �б�
        // pickedAmount == originalAmount: ���� �ֿ� �� ���� ������
        // pickedAmount < originalAmount && pickedAmount > 0: �κ� �ݱ� �� �������
        // pickedAmount == 0: �� �ֿ� �� �������

        if (pickedAmount >= originalAmount)
        {
            // ���� �ֿ��� �� ������ Destroy��
            currentPersonalItem = null;

            // ���� ������ �õ�
            TryPickupPersonalItems();

            // ���� �������� ������ ���� �۾�����
            if (currentPersonalItem == null)
            {
                ReturnToPreviousTaskOrIdle();
            }
        }
        else if (pickedAmount > 0)
        {
            // �κ� �ݱ� ���� = �κ� �� �� �� �������
            Debug.Log($"[UnitAI] {unit.UnitName}: �κ� �ݱ� ({pickedAmount}/{originalAmount}) �� �������");
            GiveUpRemainingPersonalItems();
            StartDeliveryToStorage();
        }
        else
        {
            // �� �ֿ� = ���� ���� �� �������
            Debug.Log($"[UnitAI] {unit.UnitName}: ���� ��� �� �ֿ� �� �������");
            GiveUpRemainingPersonalItems();
            StartDeliveryToStorage();
        }
    }

    private void GiveUpRemainingPersonalItems()
    {
        // �� Unity null üũ
        if (currentPersonalItem != null && currentPersonalItem)
        {
            currentPersonalItem.OwnerGiveUp();
        }
        currentPersonalItem = null;

        foreach (var item in personalItems)
        {
            // �� Unity null üũ
            if (item != null && item)
                item.OwnerGiveUp();
        }
        personalItems.Clear();
    }

    // ==================== â�� ��� ====================

    private void StartDeliveryToStorage()
    {
        // ����� ã�� (ĳ�� �����ϰ� ���� �˻�)
        targetStorage = FindNearestStorageDirectly();

        if (targetStorage == null)
        {
            // �� ������� ������ ��� ���·� ��ȯ
            Debug.Log($"[UnitAI] {unit.UnitName}: ����� ���� �� ��� ����");
            SetBehaviorAndPriority(AIBehaviorState.WaitingForStorage, TaskPriorityLevel.FreeWill);
            return;
        }

        // ����� ���� ��ġ ���
        storagePosition = targetStorage.GetNearestAccessPoint(transform.position);

        // ������� �̵� ����
        deliveryPhase = DeliveryPhase.MovingToStorage;
        depositTimer = 0f;

        unit.MoveTo(storagePosition);
        SetBehaviorAndPriority(AIBehaviorState.DeliveringToStorage, TaskPriorityLevel.ItemPickup);

        Debug.Log($"[UnitAI] {unit.UnitName}: ������� �̵� ���� �� {targetStorage.name}");
    }

    /// <summary>
    /// �� ����� ��� ���� ������Ʈ
    /// </summary>
    private void UpdateWaitingForStorage()
    {
        // �ֱ������� üũ (1�ʸ���)
        if (Time.time - lastStorageCheckTime < 1f) return;
        lastStorageCheckTime = Time.time;

        // �� �Ǽ� �۾� ���� ã��
        if (TryPullConstructionTask())
        {
            return;
        }

        // ����� �ٽ� ã��
        targetStorage = FindNearestStorageDirectly();

        if (targetStorage != null)
        {
            // ����� �߰�! �� �����Ϸ� �̵�
            Debug.Log($"[UnitAI] {unit.UnitName}: ����� �߰�! �� {targetStorage.name}");

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
                // �߸��� ���¸� Idle��
                ClearDeliveryState();
                SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
                break;
        }
    }

    private void UpdateMovingToStorage()
    {
        if (targetStorage == null)
        {
            // ������� ��������� ��� ����
            unit.Inventory.DepositToStorage();
            ClearDeliveryState();
            ReturnToPreviousTaskOrIdle();
            return;
        }

        // �� ����� ���� ���� �ִ��� Ȯ�� (�簢��)
        bool isInRange = targetStorage.IsInAccessArea(transform.position);

        // ������� �����ߴ��� Ȯ��
        if (isInRange || unit.HasArrivedAtDestination())
        {
            // ���� �ܰ�� ��ȯ
            deliveryPhase = DeliveryPhase.Depositing;
            depositTimer = 0f;
            unit.StopMoving();
            Debug.Log($"[UnitAI] {unit.UnitName}: ����� ����, ���� ���� ({depositDuration}��)");
            return;
        }

        // ���� �̵� �� - ��ΰ� �������� �ٽ� �õ�
        if (unit.HasArrivedAtDestination() && !isInRange)
        {
            unit.MoveTo(storagePosition);
        }
    }

    private void UpdateDepositing()
    {
        depositTimer += Time.deltaTime;

        // ���� ���� �� (�ִϸ��̼� �� �߰� ����)
        // TODO: ���� �ִϸ��̼� Ʈ����

        if (depositTimer >= depositDuration)
        {
            // ���� �Ϸ�
            PerformDeposit();
            ClearDeliveryState();

            Debug.Log($"[UnitAI] {unit.UnitName}: ���� �Ϸ�!");

            // ���� �۾����� �����ϰų� �� �۾� ã��
            ReturnToPreviousTaskOrIdle();
        }
    }

    /// <summary>
    /// ���� ���� ����
    /// </summary>
    private void PerformDeposit()
    {
        if (targetStorage != null && targetStorage.IsMainStorage)
        {
            // ���� ������� ResourceManager�� ����
            unit.Inventory.DepositToStorage();
        }
        else if (targetStorage != null)
        {
            // �Ϲ� ������� StorageComponent�� ����
            foreach (var slot in unit.Inventory.Slots)
            {
                if (!slot.IsEmpty)
                {
                    targetStorage.AddItem(slot.Resource, slot.Amount);
                }
            }
            // �κ��丮 ���� (��� ����)
            unit.Inventory.Clear();
        }
        else
        {
            // ������� ������ ResourceManager��
            unit.Inventory.DepositToStorage();
        }
    }

    /// <summary>
    /// ����� ��� ���� �ʱ�ȭ
    /// </summary>
    private void ClearDeliveryState()
    {
        deliveryPhase = DeliveryPhase.None;
        targetStorage = null;
        depositTimer = 0f;
    }

    /// <summary>
    /// ���� �۾����� �����ϰų� �� �۾� ã�� �Ǵ� Idle
    /// </summary>
    private void ReturnToPreviousTaskOrIdle()
    {
        // ★ 흡수 대기 아이템이 있으면 새 작업 받지 않음
        pendingMagnetItems.RemoveAll(item => item == null || !item);
        if (pendingMagnetItems.Count > 0)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 흡수 대기 중 ({pendingMagnetItems.Count}개) → 새 작업 안 받음");
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

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
    /// ���� ����� ����� ã��
    /// </summary>
    private StorageComponent FindNearestStorage()
    {
        // BuildingManager�� ���� ã��
        if (BuildingManager.Instance != null)
        {
            var storage = BuildingManager.Instance.GetNearestStorage(transform.position, mustHaveSpace: false);
            if (storage != null)
            {
                return storage.GetComponent<StorageComponent>();
            }
        }

        // BuildingManager�� ������ ���� ã��
        var storages = FindObjectsOfType<StorageComponent>();
        StorageComponent nearest = null;
        float nearestDist = storageSearchRadius;

        foreach (var storage in storages)
        {
            // �ϼ��� �ǹ���
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
    /// �� ����� ���� �˻� (ĳ�� ��� ����)
    /// </summary>
    private StorageComponent FindNearestStorageDirectly()
    {
        var storages = FindObjectsOfType<StorageComponent>();
        StorageComponent nearest = null;
        float nearestDist = storageSearchRadius;

        foreach (var storage in storages)
        {
            // �ϼ��� �ǹ��� (Building ������Ʈ�� ������ �ϼ��� ������ ����)
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

        // ĳ�� ����
        hasStorageBuilding = nearest != null;

        return nearest;
    }

    /// <summary>
    /// �� ������� �����ϴ��� Ȯ�� (ĳ�� ���)
    /// </summary>
    private bool HasAnyStorage()
    {
        // ĳ�õ� ��� ��� (5�ʸ��� ����)
        if (Time.time - lastStorageCheckTime < STORAGE_CHECK_INTERVAL)
        {
            return hasStorageBuilding;
        }

        lastStorageCheckTime = Time.time;

        // BuildingManager�� ���� Ȯ��
        if (BuildingManager.Instance != null)
        {
            var storage = BuildingManager.Instance.GetNearestStorage(transform.position, mustHaveSpace: false);
            hasStorageBuilding = storage != null;
            return hasStorageBuilding;
        }

        // BuildingManager�� ������ ���� Ȯ��
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
    /// �� �κ��丮�� ����� �ϴ��� Ȯ��
    /// ����: ��� ���� ��� �� AND �ش� ������ ���� ����
    /// </summary>
    private bool ShouldDepositInventory(ResourceItemSO resourceToAdd = null)
    {
        // �κ��丮�� ��������� ������ �ʿ� ����
        if (unit.Inventory.IsEmpty) return false;

        // ��� ������ ��� ������ Ȯ��
        bool allSlotsUsed = unit.Inventory.UsedSlots >= unit.Inventory.MaxSlots;

        if (!allSlotsUsed)
        {
            // �� ������ ������ ���� �ʿ� ����
            return false;
        }

        // ��� ������ ��� ���� ��
        if (resourceToAdd != null)
        {
            // �߰��Ϸ��� �������� �� ������ ������ ���� �ʿ�
            return !unit.Inventory.CanAddAny(resourceToAdd);
        }

        // �߰��Ϸ��� �������� ������ (�Ϲ� IsFull üũ)
        return unit.Inventory.IsFull;
    }

    /// <summary>
    /// ★ 작업이 없을 때 인벤토리 정리가 필요한지 확인
    /// </summary>
    private bool ShouldDepositWhenIdle()
    {
        // 인벤토리가 비어있으면 저장할 필요 없음
        if (unit.Inventory.IsEmpty) return false;

        // 인벤이 꽉 차지 않았으면 저장 필요 없음
        if (!unit.Inventory.IsFull) return false;

        // 작업이 있는지 확인
        if (TaskManager.Instance != null)
        {
            var availableTask = TaskManager.Instance.FindNearestTask(unit);
            if (availableTask != null)
            {
                // ★ 채집 작업이면 저장고로 (인벤 꽉 차서 채집 못 함)
                if (availableTask.Data.Type == TaskType.Harvest)
                {
                    return true;
                }
                // 건설 등 다른 작업은 할 수 있으므로 저장 안 함
                return false;
            }
        }

        // 작업이 없고 인벤이 꽉 찼으면 저장
        return true;
    }


    // ==================== ���� ã�� ====================

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

    // �� �ܺο��� ���� ��ġ ����
    public void SetFoodTarget(Vector3 foodPosition)
    {
        if (bb.Hunger > hungerSeekThreshold) return;

        bb.TargetPosition = foodPosition;
        unit.MoveTo(foodPosition);
        SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
    }

    // ==================== �÷��̾� ���� ====================

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

    // ==================== ���� �ൿ ====================

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

    // ==================== ��ƿ��Ƽ ====================

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
        Debug.Log($"[UnitAI] {unit.UnitName}: �����!");
    }

    // ==================== ����� ====================

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

    // ==================== ȣȯ�� ====================

    public void AddPlayerCommand(UnitTask task) { }
    public void AddPlayerCommandImmediate(UnitTask task) { }
    public void OnTaskCompleted(UnitTask task) { }
    public void OnFoodEaten(float nutrition) => bb?.Eat(nutrition);
}