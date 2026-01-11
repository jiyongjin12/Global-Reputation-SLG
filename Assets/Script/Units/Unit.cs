using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum UnitType
{
    Worker,
    Fighter
}

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
/// 유닛 (Body) - 물리적 데이터와 기능
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Unit : MonoBehaviour
{
    [Header("Basic Info")]
    [SerializeField] private string unitName;
    [SerializeField] private UnitType unitType = UnitType.Worker;

    [Header("Stats")]
    [SerializeField] private float maxHP = 100f;
    [SerializeField] private float currentHP = 100f;
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float workSpeed = 1f;
    [SerializeField] private float gatherPower = 1f;
    [SerializeField] private float attackPower = 10f;

    [Header("Traits")]
    [SerializeField] private List<UnitTraitSO> traits = new();

    [Header("=== 인벤토리 (인스펙터 확인용) ===")]
    [SerializeField] private UnitInventory inventory = new UnitInventory();

    // Components
    private NavMeshAgent agent;
    private UnitMovement movement;

    // Blackboard
    public UnitBlackboard Blackboard { get; private set; }

    // Properties
    public string UnitName => unitName;
    public UnitType Type => unitType;
    public float MaxHP => maxHP;
    public float CurrentHP => currentHP;
    public float MoveSpeed => moveSpeed;
    public float WorkSpeed => workSpeed;
    public float GatherPower => gatherPower;
    public float AttackPower => attackPower;
    public bool IsAlive => currentHP > 0;
    public UnitInventory Inventory => inventory;
    public List<UnitTraitSO> Traits => traits;
    public NavMeshAgent Agent => agent;

    // 호환성 Properties
    public UnitStats Stats => _legacyStats;
    private UnitStats _legacyStats;
    public bool IsIdle => Blackboard?.IsIdle ?? true;
    public bool HasTask => Blackboard?.CurrentTask != null;
    public UnitState CurrentState => Blackboard?.CurrentState ?? UnitState.Idle;
    public UnitTask CurrentTask => null; // 기존 호환용

    // 이벤트
    public event Action<Unit> OnUnitDeath;
    public event Action<Unit, UnitState> OnStateChanged;
    public event Action<Unit, UnitTask> OnTaskCompleted;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        movement = GetComponent<UnitMovement>();
        Blackboard = new UnitBlackboard();

        // 호환성용 UnitStats 생성
        _legacyStats = new UnitStats();
    }

    private void Start()
    {
        Initialize();
    }

    public void Initialize(string name = null, List<UnitTraitSO> initialTraits = null)
    {
        if (!string.IsNullOrEmpty(name))
            unitName = name;
        else if (string.IsNullOrEmpty(unitName))
            unitName = $"Unit_{UnityEngine.Random.Range(1000, 9999)}";

        currentHP = maxHP;
        inventory.Initialize(5, 5);  // ★ 5칸, 아이템당 최대 5개 스택
        Blackboard.Reset();

        _legacyStats.Initialize(maxHP);

        if (initialTraits != null)
            traits = initialTraits;

        ApplyTraits();
        agent.speed = moveSpeed;

        // Blackboard 이벤트 연결
        Blackboard.OnStateChanged += (state) => OnStateChanged?.Invoke(this, state);
    }

    // ==================== 이동 ====================

    public void MoveTo(Vector3 destination)
    {
        Blackboard.TargetPosition = destination;

        if (movement != null)
            movement.MoveTo(destination, MovementStyle.Natural);
        else
            agent.SetDestination(destination);
    }

    public void StopMoving()
    {
        Blackboard.TargetPosition = null;

        if (movement != null)
            movement.Stop();
        else
            agent.ResetPath();
    }

    public bool HasArrivedAtDestination()
    {
        if (agent.pathPending) return false;
        if (agent.remainingDistance <= agent.stoppingDistance)
        {
            if (!agent.hasPath || agent.velocity.sqrMagnitude < 0.1f)
                return true;
        }
        return false;
    }

    // ==================== 체력 ====================

    public void TakeDamage(float damage)
    {
        float oldHP = currentHP;
        currentHP = Mathf.Max(0, currentHP - damage);
        _legacyStats.TakeDamage(damage);

        if (currentHP <= 0 && oldHP > 0)
            Die();
    }

    public void Heal(float amount)
    {
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        _legacyStats.Heal(amount);
    }

    public void Eat(float nutritionValue)
    {
        Blackboard.Eat(nutritionValue);
        _legacyStats.Eat(nutritionValue);
    }

    private void Die()
    {
        Blackboard.IsAlive = false;
        inventory.DropAllItems();
        OnUnitDeath?.Invoke(this);
        Destroy(gameObject);
    }

    // ==================== 작업 ====================

    public float DoWork()
    {
        Blackboard.LastWorkedTime = Time.time;
        return workSpeed * GetTraitMultiplier(UnitStatType.WorkSpeed);
    }

    public float DoGather(ResourceNodeType? nodeType = null)
    {
        return gatherPower * GetTraitMultiplier(UnitStatType.GatherPower, nodeType);
    }

    // ==================== 호환성 메서드 ====================

    public void AssignTask(UnitTask task) { }
    public void AssignTaskImmediate(UnitTask task) { }

    // ==================== 특성 ====================

    private void ApplyTraits()
    {
        foreach (var trait in traits)
        {
            foreach (var effect in trait.Effects)
            {
                if (!effect.HasNodeTypeCondition)
                    ApplyStatModifier(effect.AffectedStat, effect.Multiplier);
            }
        }
        agent.speed = moveSpeed;
    }

    private void ApplyStatModifier(UnitStatType stat, float multiplier)
    {
        switch (stat)
        {
            case UnitStatType.MoveSpeed:
                moveSpeed *= multiplier;
                break;
            case UnitStatType.WorkSpeed:
                workSpeed *= multiplier;
                break;
            case UnitStatType.GatherPower:
                gatherPower *= multiplier;
                break;
            case UnitStatType.AttackPower:
                attackPower *= multiplier;
                break;
        }
    }

    public float GetTraitMultiplier(UnitStatType statType, ResourceNodeType? nodeType = null)
    {
        float multiplier = 1f;
        foreach (var trait in traits)
        {
            foreach (var effect in trait.Effects)
            {
                if (effect.AffectedStat == statType)
                {
                    if (!effect.HasNodeTypeCondition || effect.TargetNodeType == nodeType)
                        multiplier *= effect.Multiplier;
                }
            }
        }
        return multiplier;
    }

    // ==================== 레벨 시스템 (확장용) ====================

    /// <summary>
    /// 레벨업 시 인벤토리 확장
    /// </summary>
    public void OnLevelUp(int newLevel)
    {
        // 레벨당 1칸씩 증가 (예시)
        inventory.SetSlotsByLevel(newLevel, 1);
    }
}