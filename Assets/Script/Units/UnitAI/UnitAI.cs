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
    Socializing,
    GoingToSleep,   // ★ 추가: 침대로 이동 중
    Sleeping        // ★ 추가: 수면 중
}

public enum TaskPriorityLevel
{
    PlayerCommand = 0,
    Survival = 1,
    Sleep = 2,          // ★ 추가: 수면 (밤에만)
    Construction = 3,
    Workstation = 4,
    ItemPickup = 5,
    Harvest = 6,
    FreeWill = 7
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

// ==================== Main Class (Partial) ====================

/// <summary>
/// 유닛 AI - Core
/// Partial class로 분할됨:
/// - UnitAI.cs (Core)
/// - UnitAI_Work.cs (작업)
/// - UnitAI_Delivery.cs (배달/아이템)
/// - UnitAI_Command.cs (명령/자유행동)
/// - UnitAI_Sleep.cs (수면)
/// </summary>
[RequireComponent(typeof(Unit))]
public partial class UnitAI : MonoBehaviour
{
    // ==================== Nested Class ====================

    protected class TaskContext
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

    [Header("=== 이동 안정성 ===")]
    [SerializeField] private float stuckCheckInterval = 1f;
    [SerializeField] private float stuckThreshold = 0.1f;
    [SerializeField] private int maxStuckCount = 3;

    [Header("=== 디버그 ===")]
    [SerializeField] private AIBehaviorState currentBehavior = AIBehaviorState.Idle;
    [SerializeField] private TaskPriorityLevel currentPriority = TaskPriorityLevel.FreeWill;

    // ==================== Protected Fields ====================

    // Components
    protected Unit unit;
    protected UnitBlackboard bb;
    protected NavMeshAgent agent;
    protected UnitMovement movement;
    protected UnitSocialInteraction socialInteraction;

    // Task Context
    protected TaskContext taskContext = new();
    protected PostedTask previousTask;

    // Timers
    protected float lastDecisionTime;
    protected float pickupTimer;
    protected float magnetAbsorbTimer;
    protected float lastStorageCheckTime = -10f;

    // Item Management
    protected List<DroppedItem> personalItems = new();
    protected List<DroppedItem> pendingMagnetItems = new();
    protected DroppedItem currentPersonalItem;

    // Workstation
    protected IWorkstation currentWorkstation;
    protected bool isWorkstationWorkStarted;

    // Delivery
    protected DeliveryPhase deliveryPhase = DeliveryPhase.None;
    protected Vector3 storagePosition;
    protected StorageComponent targetStorage;
    protected float depositTimer;

    // Social
    protected Unit socialTarget;
    protected bool isApproachingForSocial;

    // ★ 수면 관련
    protected BedComponent targetBed;
    protected bool isSleeping;
    protected bool isGoingToSleep;

    // 명령 대기 플래그
    protected bool isWaitingForCommand = false;

    // 이동 멈춤 감지용
    private Vector3 lastPosition;
    private float lastStuckCheckTime;
    private int stuckCount;
    private bool isRecoveringFromStuck;

    // Cache
    protected bool hasStorageBuilding;
    protected const float STORAGE_CHECK_INTERVAL = 5f;
    protected const float MAGNET_ABSORB_INTERVAL = 0.2f;

    // Debug
    protected Vector3 debugBuildingCenter;
    protected float debugBoxHalfX, debugBoxHalfZ;

    // ==================== Properties ====================

    public AIBehaviorState CurrentBehavior => currentBehavior;
    public TaskPriorityLevel CurrentPriority => currentPriority;
    public bool HasTask => taskContext.HasTask;
    public bool IsIdle => currentBehavior == AIBehaviorState.Idle || currentBehavior == AIBehaviorState.Wandering;
    public bool IsWaitingForCommand => isWaitingForCommand;
    public bool IsSleeping => isSleeping || currentBehavior == AIBehaviorState.Sleeping;
    public bool IsGoingToSleep => isGoingToSleep || currentBehavior == AIBehaviorState.GoingToSleep;

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

        lastPosition = transform.position;
        lastStuckCheckTime = Time.time;
    }

    private void Update()
    {
        if (bb == null || !bb.IsAlive) return;

        // 배고픔 감소 (수면 중에는 느리게)
        float hungerMultiplier = isSleeping ? 0.3f : 1f;
        bb.DecreaseHunger((hungerDecreasePerMinute / 60f) * Time.deltaTime * hungerMultiplier);

        if (bb.IsStarving)
            unit.TakeDamage(Time.deltaTime * 5f);

        // 이동 멈춤 감지 및 복구
        CheckAndRecoverFromStuck();

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

    private void OnEnable()
    {
        StartCoroutine(ReinitializeAgent());
    }

    private System.Collections.IEnumerator ReinitializeAgent()
    {
        yield return null;

        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            stuckCount = 0;
            isRecoveringFromStuck = false;
        }
    }

    // ==================== 이동 멈춤 감지 및 복구 ====================

    private void CheckAndRecoverFromStuck()
    {
        if (isRecoveringFromStuck) return;
        if (!IsMovingState()) return;

        if (Time.time - lastStuckCheckTime < stuckCheckInterval) return;

        float movedDistance = Vector3.Distance(transform.position, lastPosition);

        if (movedDistance < stuckThreshold)
        {
            stuckCount++;

            if (stuckCount >= maxStuckCount)
            {
                Debug.LogWarning($"[UnitAI] {unit.UnitName}: 이동 멈춤 감지! 복구 시도...");
                RecoverFromStuck();
            }
        }
        else
        {
            stuckCount = 0;
        }

        lastPosition = transform.position;
        lastStuckCheckTime = Time.time;
    }

    private bool IsMovingState()
    {
        if (currentBehavior == AIBehaviorState.Idle) return false;
        if (currentBehavior == AIBehaviorState.WaitingForStorage) return false;
        if (currentBehavior == AIBehaviorState.Sleeping) return false;

        if (taskContext.HasTask && taskContext.Phase == TaskPhase.Working) return false;

        if (agent != null && agent.hasPath && agent.remainingDistance > agent.stoppingDistance)
            return true;

        return false;
    }

    private void RecoverFromStuck()
    {
        isRecoveringFromStuck = true;
        stuckCount = 0;

        StartCoroutine(RecoverFromStuckCoroutine());
    }

    private System.Collections.IEnumerator RecoverFromStuckCoroutine()
    {
        if (agent != null)
        {
            Vector3 currentDest = agent.destination;
            bool hadPath = agent.hasPath;

            agent.ResetPath();
            agent.isStopped = false;

            yield return null;

            if (!agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                    Debug.Log($"[UnitAI] {unit.UnitName}: NavMesh 위치로 워프");
                }
            }

            yield return null;

            if (hadPath && currentDest != Vector3.zero)
            {
                Vector3 offsetDest = currentDest + Random.insideUnitSphere * 0.5f;
                offsetDest.y = currentDest.y;

                if (NavMesh.SamplePosition(offsetDest, out NavMeshHit destHit, 2f, NavMesh.AllAreas))
                {
                    agent.SetDestination(destHit.position);
                }
                else
                {
                    agent.SetDestination(currentDest);
                }
            }
            else if (taskContext.HasTask && taskContext.WorkPosition != Vector3.zero)
            {
                agent.SetDestination(taskContext.WorkPosition);
            }
        }

        yield return new WaitForSeconds(0.5f);
        isRecoveringFromStuck = false;

        Debug.Log($"[UnitAI] {unit.UnitName}: 멈춤 복구 완료");
    }

    // ==================== Decision Making ====================

    private void MakeDecision()
    {
        // ★ 명령 대기 중이면 플레이어 명령만 처리
        if (isWaitingForCommand)
        {
            if (bb.HasPlayerCommand && bb.PlayerCommand != null)
            {
                isWaitingForCommand = false;
                // 수면 중이면 강제 기상
                InterruptSleepForCommand();
                ExecutePlayerCommand();
            }
            return;
        }

        // 1. 플레이어 명령 최우선
        if (bb.HasPlayerCommand && bb.PlayerCommand != null)
        {
            InterruptCurrentTask();
            // 수면 중이면 강제 기상
            InterruptSleepForCommand();
            ExecutePlayerCommand();
            return;
        }

        // 2. 생존 - 극심한 배고픔
        if (bb.Hunger <= hungerCriticalThreshold && currentPriority > TaskPriorityLevel.Survival)
        {
            if (TrySeekFood())
            {
                // 수면 취소
                CancelSleep();
                InterruptCurrentTask();
                SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
                return;
            }
        }

        // ★ 3. 수면 - 밤 시간
        if (NeedsSleep() && currentPriority > TaskPriorityLevel.Sleep)
        {
            InterruptCurrentTask();
            if (TryGoToSleep())
            {
                return;
            }
        }

        // 수면 중/이동 중이면 다른 행동 안 함
        if (isSleeping || isGoingToSleep)
        {
            return;
        }

        // 4. 현재 작업 진행 중이면 유지
        if (taskContext.HasTask) return;

        // 5. 저장고 배달 중이면 유지
        if (currentBehavior == AIBehaviorState.DeliveringToStorage) return;

        // 6. 저장고 대기 중이면 건설 작업만 확인
        if (currentBehavior == AIBehaviorState.WaitingForStorage)
        {
            TryPullConstructionTask();
            return;
        }

        // 7. 상호작용 중이면 유지
        if (currentBehavior == AIBehaviorState.Socializing) return;

        // 8. 아이템 정리
        CleanupItemLists();

        // 9. 개인 아이템 줍기
        if (currentPersonalItem != null || personalItems.Count > 0)
        {
            if (currentBehavior != AIBehaviorState.PickingUpItem)
                TryPickupPersonalItems();
            return;
        }

        // 10. 대기 중인 자석 아이템 처리
        if (HandlePendingMagnetItems()) return;

        // 11. 인벤토리 꽉 참 → 저장고로
        if (unit.Inventory.IsFull)
        {
            if (currentBehavior != AIBehaviorState.DeliveringToStorage)
            {
                StartDeliveryToStorage();
                return;
            }
        }

        // 12. 일반 배고픔 시 음식 찾기
        if (bb.Hunger <= hungerSeekThreshold && TrySeekFood())
        {
            SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
            return;
        }

        // 13. Idle 상태에서의 행동
        if (currentBehavior == AIBehaviorState.Idle || currentBehavior == AIBehaviorState.Wandering)
        {
            if (TryPullTask()) return;

            if (ShouldDepositWhenIdle())
            {
                StartDeliveryToStorage();
                return;
            }
        }

        // 14. 자유 행동
        if (currentBehavior == AIBehaviorState.Idle)
        {
            PerformFreeWill();
        }
    }

    private void CleanupItemLists()
    {
        if (currentPersonalItem != null && (!currentPersonalItem || currentPersonalItem.IsBeingMagneted))
            currentPersonalItem = null;

        personalItems.RemoveAll(item => item == null || !item || item.IsBeingMagneted);
        pendingMagnetItems.RemoveAll(item => item == null || !item);
    }

    private bool HandlePendingMagnetItems()
    {
        if (pendingMagnetItems.Count == 0) return false;

        if (unit.Inventory.IsFull)
        {
            StartDeliveryToStorage();
            return true;
        }

        if (HasAbsorbablePendingItems())
        {
            MoveToNearestAbsorbableItem();
            return true;
        }

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
            // ★ 수면 관련
            case AIBehaviorState.GoingToSleep:
                UpdateGoingToSleep();
                break;
            case AIBehaviorState.Sleeping:
                UpdateSleeping();
                break;
        }
    }

    // ==================== Utility ====================

    protected void SetBehaviorAndPriority(AIBehaviorState behavior, TaskPriorityLevel priority)
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
            AIBehaviorState.GoingToSleep => UnitState.Moving,
            AIBehaviorState.Sleeping => UnitState.Sleeping,
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