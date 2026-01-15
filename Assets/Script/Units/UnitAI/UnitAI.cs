using System.Collections;
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
    GoingToSleep,
    Sleeping
}

public enum TaskPriorityLevel
{
    PlayerCommand = 0,
    Survival = 1,
    Sleep = 2,
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
/// 
/// ★ 수정사항:
/// - 배고픔: unit.DecreaseHunger() 사용 (Unit과 Blackboard 동기화)
/// - NavMesh 복구: 더 강력한 예외처리 (agent 재활성화, Warp, GameObject 재활성화)
/// - 의도적 멈춤 구분 (isStopped, isPaused 등)
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
    [Tooltip("분당 배고픔 감소량")]
    [SerializeField] private float hungerDecreasePerMinute = 10f;  // 10분에 100% → 0%
    [Tooltip("음식 찾기 범위")]
    [SerializeField] private float foodSearchRadius = 30f;

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
    [SerializeField] private float stuckCheckInterval = 0.5f;  // ★ 더 자주 체크
    [SerializeField] private float stuckThreshold = 0.05f;     // ★ 더 민감하게
    [SerializeField] private int maxStuckCount = 4;
    [SerializeField] private float pathfindingTimeout = 3f;    // ★ 경로 탐색 타임아웃

    [Header("=== 디버그 ===")]
    [SerializeField] private AIBehaviorState currentBehavior = AIBehaviorState.Idle;
    [SerializeField] private TaskPriorityLevel currentPriority = TaskPriorityLevel.FreeWill;
    [SerializeField] private int debugStuckCount = 0;  // ★ 디버그용

    // ==================== Protected Fields ====================

    protected Unit unit;
    protected UnitBlackboard bb;
    protected NavMeshAgent agent;
    protected UnitMovement movement;
    protected UnitSocialInteraction socialInteraction;

    protected TaskContext taskContext = new();
    protected PostedTask previousTask;

    protected float lastDecisionTime;
    protected float pickupTimer;
    protected float magnetAbsorbTimer;
    protected float lastStorageCheckTime = -10f;

    protected List<DroppedItem> personalItems = new();
    protected List<DroppedItem> pendingMagnetItems = new();
    protected DroppedItem currentPersonalItem;

    protected IWorkstation currentWorkstation;
    protected bool isWorkstationWorkStarted;

    protected DeliveryPhase deliveryPhase = DeliveryPhase.None;
    protected Vector3 storagePosition;
    protected StorageComponent targetStorage;
    protected float depositTimer;

    protected Unit socialTarget;
    protected bool isApproachingForSocial;

    protected BedComponent targetBed;
    protected bool isSleeping;
    protected bool isGoingToSleep;

    protected bool isWaitingForCommand = false;

    // ★ NavMesh 멈춤 감지용 (강화됨)
    private Vector3 lastPosition;
    private float lastStuckCheckTime;
    private int stuckCount;
    private bool isRecoveringFromStuck;
    private float pathfindingStartTime;
    private bool isPathfinding;
    private int recoveryAttempts = 0;
    private const int MAX_RECOVERY_ATTEMPTS = 3;

    protected bool hasStorageBuilding;
    protected const float STORAGE_CHECK_INTERVAL = 5f;
    protected const float MAGNET_ABSORB_INTERVAL = 0.2f;

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

        // ★ 테스트용: H키 누르면 배고픔 즉시 30% 감소
        if (Input.GetKeyDown(KeyCode.H))
        {
            unit.DecreaseHunger(30f);
            Debug.Log($"<color=orange>[UnitAI] {unit.UnitName}: 테스트 - 배고픔 30% 감소! 현재={unit.Hunger:F0}%</color>");
        }

        // ★ 배고픔 감소 - unit.DecreaseHunger() 사용 (Unit과 Blackboard 동기화)
        float hungerMultiplier = isSleeping ? 0.3f : 1f;
        float decreaseAmount = (hungerDecreasePerMinute / 60f) * Time.deltaTime * hungerMultiplier;
        unit.DecreaseHunger(decreaseAmount);

        // 디버그: 10초마다 상태 출력
        if (Time.frameCount % 600 == 0)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 허기={unit.Hunger:F0}%, 행동={currentBehavior}, 배고픔={IsHungryForFood()}, 배부름={IsSatisfied()}");
        }

        if (unit.IsStarving)
            unit.TakeDamage(Time.deltaTime * 5f);

        // ★ NavMesh 멈춤 감지 및 복구 (강화됨)
        CheckAndRecoverFromStuck();

        // 의사결정
        if (Time.time - lastDecisionTime >= decisionInterval)
        {
            MakeDecision();
            lastDecisionTime = Time.time;
        }

        TryAbsorbNearbyMagnetItems();
        ExecuteCurrentBehavior();
    }

    // ==================== ★ NavMesh Stuck Detection (강화됨) ====================

    private void CheckAndRecoverFromStuck()
    {
        if (Time.time - lastStuckCheckTime < stuckCheckInterval) return;
        lastStuckCheckTime = Time.time;

        // ★ agent 기본 유효성 체크
        if (agent == null)
        {
            ResetStuckState();
            return;
        }

        // ★ NavMesh 위에 없으면 즉시 복구 시도
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning($"<color=red>[UnitAI] {unit.UnitName}: NavMesh 밖에 있음! 복구 시도...</color>");
            ForceRecoverNavMesh();
            return;
        }

        // ★ 의도적인 멈춤 상태면 스킵
        if (IsIntentionallyStopped())
        {
            ResetStuckState();
            return;
        }

        // ★ 경로 탐색 중인지 체크
        if (agent.pathPending)
        {
            if (!isPathfinding)
            {
                isPathfinding = true;
                pathfindingStartTime = Time.time;
            }
            else if (Time.time - pathfindingStartTime > pathfindingTimeout)
            {
                Debug.LogWarning($"<color=red>[UnitAI] {unit.UnitName}: 경로 탐색 타임아웃!</color>");
                HandlePathfindingFailure();
            }
            return;
        }
        isPathfinding = false;

        // ★ 경로 상태 체크
        if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 유효하지 않은 경로!");
            HandlePathfindingFailure();
            return;
        }

        // ★ 이동 중인지 확인
        if (!agent.hasPath || agent.remainingDistance < agent.stoppingDistance + 0.1f)
        {
            ResetStuckState();
            return;
        }

        // ★ 이동 거리 확인
        float movedDistance = Vector3.Distance(transform.position, lastPosition);
        debugStuckCount = stuckCount;

        if (movedDistance < stuckThreshold)
        {
            stuckCount++;

            if (stuckCount >= maxStuckCount && !isRecoveringFromStuck)
            {
                Debug.Log($"<color=yellow>[UnitAI] {unit.UnitName}: 멈춤 감지! (이동거리={movedDistance:F3}, 횟수={stuckCount})</color>");
                RecoverFromStuck();
            }
        }
        else
        {
            ResetStuckState();
        }

        lastPosition = transform.position;
    }

    /// <summary>
    /// ★ 의도적으로 멈춘 상태인지 확인
    /// </summary>
    private bool IsIntentionallyStopped()
    {
        // NavMeshAgent가 명시적으로 멈춤
        if (agent.isStopped)
            return true;

        // UnitMovement가 일시정지 상태
        if (movement != null && movement.IsPaused)
            return true;

        // 이동이 필요없는 행동들
        switch (currentBehavior)
        {
            case AIBehaviorState.Idle:
            case AIBehaviorState.Working:
            case AIBehaviorState.WorkingAtStation:
            case AIBehaviorState.Sleeping:
            case AIBehaviorState.Socializing:
                return true;
        }

        // 작업 중 (Working 페이즈)
        if (taskContext.IsWorking)
            return true;

        return false;
    }

    /// <summary>
    /// ★ 멈춤 상태 리셋
    /// </summary>
    private void ResetStuckState()
    {
        stuckCount = 0;
        isRecoveringFromStuck = false;
        recoveryAttempts = 0;
    }

    /// <summary>
    /// ★ 멈춤 상태에서 복구 (단계별 시도)
    /// </summary>
    private void RecoverFromStuck()
    {
        isRecoveringFromStuck = true;
        recoveryAttempts++;

        Vector3 destination = agent.destination;

        Debug.Log($"<color=cyan>[UnitAI] {unit.UnitName}: 복구 시도 #{recoveryAttempts}</color>");

        // ★ 단계 1: 주변 유효 위치로 이동 시도
        if (recoveryAttempts == 1)
        {
            Vector3 randomOffset = Random.insideUnitSphere * 2f;
            randomOffset.y = 0;

            if (NavMesh.SamplePosition(transform.position + randomOffset, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                StartCoroutine(ResumeAfterRecovery(destination, 0.5f));
                stuckCount = 0;
                return;
            }
        }

        // ★ 단계 2: Warp로 강제 위치 재설정
        if (recoveryAttempts == 2)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                Debug.Log($"[UnitAI] {unit.UnitName}: Warp로 복구 시도");
                agent.Warp(hit.position);
                agent.SetDestination(destination);
                stuckCount = 0;
                isRecoveringFromStuck = false;
                return;
            }
        }

        // ★ 단계 3: Agent 재활성화
        if (recoveryAttempts >= MAX_RECOVERY_ATTEMPTS)
        {
            Debug.Log($"<color=orange>[UnitAI] {unit.UnitName}: Agent 재활성화로 복구 시도</color>");
            StartCoroutine(ReactivateAgent(destination));
            return;
        }

        // 복구 실패 → 작업 취소
        Debug.LogWarning($"[UnitAI] {unit.UnitName}: 복구 실패, 작업 취소");
        CompleteCurrentTask();
        ResetStuckState();
    }

    /// <summary>
    /// ★ NavMesh 강제 복구 (NavMesh 밖에 있을 때)
    /// </summary>
    private void ForceRecoverNavMesh()
    {
        // 가장 가까운 NavMesh 위치 찾기
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: NavMesh로 워프 ({hit.position})");

            // Agent 비활성화 후 위치 이동, 다시 활성화
            agent.enabled = false;
            transform.position = hit.position;
            agent.enabled = true;

            if (agent.isOnNavMesh)
            {
                Debug.Log($"<color=green>[UnitAI] {unit.UnitName}: NavMesh 복구 성공!</color>");
            }
        }
        else
        {
            Debug.LogError($"[UnitAI] {unit.UnitName}: NavMesh 찾기 실패! GameObject 재활성화 시도");
            StartCoroutine(ReactivateGameObject());
        }
    }

    /// <summary>
    /// ★ 경로 탐색 실패 처리
    /// </summary>
    private void HandlePathfindingFailure()
    {
        isPathfinding = false;
        agent.ResetPath();

        if (!agent.isOnNavMesh)
        {
            ForceRecoverNavMesh();
        }
        else
        {
            // 현재 위치에서 재시작
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        CompleteCurrentTask();
    }

    /// <summary>
    /// ★ Agent 재활성화 코루틴
    /// </summary>
    private IEnumerator ReactivateAgent(Vector3 destination)
    {
        Vector3 currentPos = transform.position;

        agent.enabled = false;
        yield return null;  // 1프레임 대기

        // NavMesh 위 위치 찾기
        if (NavMesh.SamplePosition(currentPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
        }

        agent.enabled = true;
        yield return null;  // 1프레임 대기

        if (agent.isOnNavMesh)
        {
            agent.SetDestination(destination);
            Debug.Log($"<color=green>[UnitAI] {unit.UnitName}: Agent 재활성화 성공!</color>");
        }
        else
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: Agent 재활성화 후에도 NavMesh 밖!");
            StartCoroutine(ReactivateGameObject());
        }

        ResetStuckState();
    }

    /// <summary>
    /// ★ GameObject 재활성화 코루틴 (최후의 수단)
    /// </summary>
    private IEnumerator ReactivateGameObject()
    {
        Debug.Log($"<color=red>[UnitAI] {unit.UnitName}: GameObject 재활성화 시도 (최후의 수단)</color>");

        Vector3 currentPos = transform.position;
        Quaternion currentRot = transform.rotation;

        // NavMesh 위 위치 찾기
        Vector3 targetPos = currentPos;
        if (NavMesh.SamplePosition(currentPos, out NavMeshHit hit, 15f, NavMesh.AllAreas))
        {
            targetPos = hit.position;
        }

        gameObject.SetActive(false);
        yield return new WaitForSeconds(0.1f);

        transform.position = targetPos;
        transform.rotation = currentRot;

        gameObject.SetActive(true);
        yield return null;

        if (agent != null && agent.isOnNavMesh)
        {
            Debug.Log($"<color=green>[UnitAI] {unit.UnitName}: GameObject 재활성화 성공!</color>");
        }
        else
        {
            Debug.LogError($"[UnitAI] {unit.UnitName}: GameObject 재활성화 후에도 문제 발생!");
        }

        ResetStuckState();
        CompleteCurrentTask();
    }

    private IEnumerator ResumeAfterRecovery(Vector3 originalDestination, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (agent != null && agent.isOnNavMesh)
        {
            agent.SetDestination(originalDestination);
        }

        isRecoveringFromStuck = false;
    }

    // ==================== Decision Making ====================

    private void MakeDecision()
    {
        if (isWaitingForCommand)
        {
            if (bb.HasPlayerCommand && bb.PlayerCommand != null)
            {
                isWaitingForCommand = false;
                InterruptSleepForCommand();
                ExecutePlayerCommand();
            }
            return;
        }

        // 1. 플레이어 명령 최우선
        if (bb.HasPlayerCommand && bb.PlayerCommand != null)
        {
            InterruptCurrentTask();
            InterruptSleepForCommand();
            ExecutePlayerCommand();
            return;
        }

        // 2. 생존 - 배고픔 (40% 이하면 음식 찾기)
        // ★ IsHungryForFood() 사용 (40% 이하)
        if (IsHungryForFood() && currentPriority >= TaskPriorityLevel.Survival)
        {
            Debug.Log($"<color=orange>[UnitAI] {unit.UnitName}: 배고픔! (허기={unit.Hunger:F0}%) → 음식 찾기</color>");
            if (TrySeekFoodNew())
            {
                CancelSleep();
                InterruptCurrentTask();
                SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
                return;
            }
        }

        // 3. 수면
        if (NeedsSleep() && currentPriority > TaskPriorityLevel.Sleep)
        {
            InterruptCurrentTask();
            if (TryGoToSleep()) return;
        }

        if (isSleeping || isGoingToSleep) return;

        // 4. 현재 작업 진행 중
        if (taskContext.HasTask) return;

        // 5. 저장고 배달 중
        if (currentBehavior == AIBehaviorState.DeliveringToStorage) return;

        // 6. 저장고 대기 중
        if (currentBehavior == AIBehaviorState.WaitingForStorage)
        {
            TryPullConstructionTask();
            return;
        }

        // 7. 상호작용 중
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

        // 10. 대기 중인 자석 아이템
        if (HandlePendingMagnetItems()) return;

        // 11. 인벤토리 꽉 참
        if (unit.Inventory.IsFull)
        {
            if (currentBehavior != AIBehaviorState.DeliveringToStorage)
            {
                StartDeliveryToStorage();
                return;
            }
        }

        // 12. 일반 배고픔 체크 (이미 위에서 처리됨, 여기서는 재확인)
        // ★ 40% 이하면 음식 찾기 (이미 SeekingFood 중이 아닐 때만)
        if (IsHungryForFood() && currentBehavior != AIBehaviorState.SeekingFood)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 배고픔 재확인 (허기={unit.Hunger:F0}%)");
            if (TrySeekFoodNew())
            {
                SetBehaviorAndPriority(AIBehaviorState.SeekingFood, TaskPriorityLevel.Survival);
                return;
            }
        }

        // 13. Idle 상태
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
                UpdateSeekingFoodNew();
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
        Debug.Log($"<color=orange>[UnitAI] {unit.UnitName}: 배고파! (Unit.Hunger={unit.Hunger:F1})</color>");
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

    /// <summary>
    /// ★ 음식 먹기 - unit.Eat() 사용
    /// </summary>
    public void OnFoodEaten(float nutrition) => unit.Eat(nutrition);
}