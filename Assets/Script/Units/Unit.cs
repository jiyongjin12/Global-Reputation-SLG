using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 유닛 타입
/// </summary>
public enum UnitType
{
    Worker,     // 일반 일꾼
    Fighter     // 전투 유닛
}

/// <summary>
/// 유닛 상태
/// </summary>
public enum UnitState
{
    Idle,
    Moving,
    Working,
    Eating,
    Fighting,
    Fleeing
}

/// <summary>
/// 메인 유닛 클래스
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Unit : MonoBehaviour
{
    [Header("Unit Info")]
    [SerializeField] private string unitName;
    [SerializeField] private UnitType unitType = UnitType.Worker;

    [Header("Stats")]
    [SerializeField] private UnitStats stats = new UnitStats();

    [Header("Inventory")]
    [SerializeField] private UnitInventory inventory = new UnitInventory();

    [Header("Traits")]
    [SerializeField] private List<UnitTraitSO> traits = new List<UnitTraitSO>();

    [Header("Settings")]
    [SerializeField] private float hungerDecreaseRate = 1f; // 초당 배고픔 감소량
    [SerializeField] private float loyaltyDecreaseRate = 0.1f; // 초당 충성심 감소량 (불만족 시)

    // Components
    private NavMeshAgent agent;

    // State
    private UnitState currentState = UnitState.Idle;
    private UnitTask currentTask;
    private Queue<UnitTask> taskQueue = new Queue<UnitTask>();

    // Properties
    public string UnitName => unitName;
    public UnitType Type => unitType;
    public UnitStats Stats => stats;
    public UnitInventory Inventory => inventory;
    public List<UnitTraitSO> Traits => traits;
    public UnitState CurrentState => currentState;
    public UnitTask CurrentTask => currentTask;
    public bool IsIdle => currentState == UnitState.Idle && currentTask == null;
    public bool HasTask => currentTask != null;

    // 이벤트
    public event Action<Unit> OnUnitDeath;
    public event Action<Unit, UnitState> OnStateChanged;
    public event Action<Unit, UnitTask> OnTaskCompleted;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        Initialize();
    }

    /// <summary>
    /// 유닛 초기화
    /// </summary>
    public void Initialize(string name = null, List<UnitTraitSO> initialTraits = null)
    {
        if (!string.IsNullOrEmpty(name))
            unitName = name;
        else if (string.IsNullOrEmpty(unitName))
            unitName = "Unit_" + UnityEngine.Random.Range(1000, 9999);

        stats.Initialize();
        inventory.Initialize(1, 10); // 1슬롯, 10스택

        // 특성 적용
        if (initialTraits != null)
        {
            traits = initialTraits;
        }
        ApplyTraits();

        // 이벤트 연결
        stats.OnDeath += HandleDeath;
        stats.OnHungerCritical += HandleHungerCritical;
        stats.OnLoyaltyCritical += HandleLoyaltyCritical;

        // NavMeshAgent 설정
        agent.speed = stats.MoveSpeed;
    }

    private void Update()
    {
        // 스탯 갱신 (배고픔 감소 등)
        UpdateStats();

        // 작업 실행
        ProcessTask();

        // 상태 업데이트
        UpdateState();
    }

    private void UpdateStats()
    {
        // 배고픔 감소
        stats.DecreaseHunger(hungerDecreaseRate * Time.deltaTime);

        // 불만족 상태면 충성심 감소
        if (stats.IsHungry)
        {
            stats.ModifyLoyalty(-loyaltyDecreaseRate * Time.deltaTime);
        }
    }

    private void ProcessTask()
    {
        // 현재 작업이 없으면 큐에서 가져오기
        if (currentTask == null && taskQueue.Count > 0)
        {
            currentTask = taskQueue.Dequeue();
            Debug.Log($"[Unit] {unitName}: 새 작업 시작 - {currentTask.Type} (남은 큐: {taskQueue.Count})");
            currentTask.Start(this);
        }

        // 현재 작업 실행
        if (currentTask != null)
        {
            if (currentTask.IsComplete(this))
            {
                Debug.Log($"[Unit] {unitName}: 작업 완료 - {currentTask.Type}");
                currentTask.Complete(this);
                OnTaskCompleted?.Invoke(this, currentTask);
                currentTask = null;
            }
            else
            {
                currentTask.Execute(this);
            }
        }
    }

    private void UpdateState()
    {
        UnitState newState = UnitState.Idle;

        if (currentTask != null)
        {
            switch (currentTask.Type)
            {
                case TaskType.MoveTo:
                    newState = UnitState.Moving;
                    break;
                case TaskType.Construct:
                case TaskType.Harvest:
                case TaskType.PickupItem:
                case TaskType.DeliverToStorage:
                    newState = agent.velocity.magnitude > 0.1f ? UnitState.Moving : UnitState.Working;
                    break;
                case TaskType.Eat:
                    newState = UnitState.Eating;
                    break;
                case TaskType.Attack:
                    newState = UnitState.Fighting;
                    break;
                case TaskType.Flee:
                    newState = UnitState.Fleeing;
                    break;
            }
        }

        if (newState != currentState)
        {
            currentState = newState;
            OnStateChanged?.Invoke(this, currentState);
        }
    }

    /// <summary>
    /// 작업 할당
    /// </summary>
    public void AssignTask(UnitTask task)
    {
        taskQueue.Enqueue(task);
    }

    /// <summary>
    /// 작업 즉시 할당 (현재 작업 취소)
    /// </summary>
    public void AssignTaskImmediate(UnitTask task)
    {
        currentTask?.Cancel();
        currentTask = task;
        currentTask.Start(this);
    }

    /// <summary>
    /// 목표 위치로 이동
    /// </summary>
    public void MoveTo(Vector3 destination)
    {
        agent.SetDestination(destination);
    }

    /// <summary>
    /// 이동 정지
    /// </summary>
    public void StopMoving()
    {
        agent.ResetPath();
    }

    /// <summary>
    /// NavMeshAgent 기준 목적지 도착 여부
    /// </summary>
    public bool HasArrivedAtDestination()
    {
        // 경로 계산 중이면 아직 도착 아님
        if (agent.pathPending)
            return false;

        // 남은 거리가 정지 거리보다 작으면 도착
        if (agent.remainingDistance <= agent.stoppingDistance)
        {
            // 경로가 없거나 속도가 거의 0이면 도착
            if (!agent.hasPath || agent.velocity.sqrMagnitude < 0.1f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// NavMeshAgent 접근용
    /// </summary>
    public UnityEngine.AI.NavMeshAgent Agent => agent;

    /// <summary>
    /// 특성 효과 적용
    /// </summary>
    private void ApplyTraits()
    {
        foreach (var trait in traits)
        {
            foreach (var effect in trait.Effects)
            {
                // 조건부 효과는 나중에 작업 시 확인
                if (!effect.HasNodeTypeCondition)
                {
                    stats.ApplyBuff(effect.AffectedStat, effect.Multiplier);
                }
            }
        }

        // 이동 속도 업데이트
        agent.speed = stats.MoveSpeed;
    }

    /// <summary>
    /// 특정 자원 타입에 대한 특성 배율 가져오기
    /// </summary>
    public float GetTraitMultiplier(UnitStatType statType, ResourceNodeType? nodeType = null)
    {
        float multiplier = 1f;

        foreach (var trait in traits)
        {
            foreach (var effect in trait.Effects)
            {
                if (effect.AffectedStat == statType)
                {
                    // 조건부 효과 체크
                    if (!effect.HasNodeTypeCondition || effect.TargetNodeType == nodeType)
                    {
                        multiplier *= effect.Multiplier;
                    }
                }
            }
        }

        return multiplier;
    }

    /// <summary>
    /// 음식 먹기
    /// </summary>
    public void Eat(float nutritionValue)
    {
        stats.Eat(nutritionValue);
    }

    private void HandleDeath()
    {
        Debug.Log($"[Unit] {unitName} has died!");

        // 인벤토리 아이템 드롭
        var droppedItems = inventory.DropAllItems();
        foreach (var (resource, amount) in droppedItems)
        {
            // TODO: 실제로 바닥에 드롭
        }

        OnUnitDeath?.Invoke(this);
        Destroy(gameObject);
    }

    private void HandleHungerCritical()
    {
        Debug.Log($"[Unit] {unitName} is hungry!");
        // TODO: 음식 찾기 우선순위 높이기
    }

    private void HandleLoyaltyCritical()
    {
        Debug.Log($"[Unit] {unitName}'s loyalty is critical!");
        // TODO: 반란 가능성, 도망 등
    }

    private void OnDestroy()
    {
        stats.OnDeath -= HandleDeath;
        stats.OnHungerCritical -= HandleHungerCritical;
        stats.OnLoyaltyCritical -= HandleLoyaltyCritical;
    }
}