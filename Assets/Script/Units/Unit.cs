using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 유닛 타입
/// </summary>
public enum UnitType
{
    Worker,     // 일반 유닛: 채집 1.5배, 전투 0.75배
    Fighter     // 전투 유닛: 전투 1.5배, 채집 0.75배
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
    Fleeing,
    Socializing,
    Sleeping    // ★ 추가
}

/// <summary>
/// 유닛 (Body) - 물리적 데이터와 기능
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Unit : MonoBehaviour
{
    // ==================== Type Efficiency Constants ====================

    private const float TYPE_BONUS = 1.5f;      // 전문 분야 보너스
    private const float TYPE_PENALTY = 0.75f;   // 비전문 분야 페널티

    // ==================== Serialized Fields ====================

    [Header("=== 기본 정보 ===")]
    [SerializeField] private string unitName;
    [SerializeField] private UnitType unitType = UnitType.Worker;

    [Header("=== 기본 스탯 ===")]
    [SerializeField] private float maxHP = 100f;
    [SerializeField] private float currentHP = 100f;
    [SerializeField] private float baseAttackPower = 10f;
    [SerializeField] private float baseGatherPower = 1f;
    [SerializeField] private float baseWorkSpeed = 1f;

    [Header("=== 이동 ===")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float runSpeed = 4f;

    [Header("=== 레벨 ===")]
    [SerializeField] private int level = 1;
    [SerializeField] private float currentExp = 0f;
    [SerializeField] private float expToNextLevel = 100f;

    [Header("=== 니즈 ===")]
    [SerializeField, Range(0, 100)] private float hunger = 100f;
    [SerializeField, Range(0, 100)] private float loyalty = 100f;
    [SerializeField, Range(0, 100)] private float mentalHealth = 100f;

    [Header("=== 전투 스탯 ===")]
    [SerializeField] private float baseAttackSpeed = 1f;

    [Header("=== 특성 ===")]
    [SerializeField] private List<UnitTraitSO> traits = new();

    [Header("=== 인벤토리 ===")]
    [SerializeField] private UnitInventory inventory = new();

    // ==================== Components ====================

    private NavMeshAgent agent;
    private UnitMovement movement;

    // ★ 침대 참조
    private BedComponent assignedBed;

    // ==================== Blackboard ====================

    public UnitBlackboard Blackboard { get; private set; }

    // 호환성용 레가시 스탯
    public UnitStats Stats => _legacyStats;
    private UnitStats _legacyStats;

    // ==================== Properties ====================

    public string UnitName => unitName;
    public UnitType Type => unitType;
    public float MaxHP => maxHP;
    public float CurrentHP => currentHP;
    public bool IsAlive => currentHP > 0;
    public float MoveSpeed => moveSpeed;
    public float RunSpeed => runSpeed;

    // 타입별 효율이 적용된 스탯
    public float AttackPower => CalculateAttackPower();
    public float GatherPower => CalculateGatherPower();
    public float WorkSpeed => baseWorkSpeed * GetTraitMultiplier(UnitStatType.WorkSpeed);

    // 기본 스탯 (효율 미적용)
    public float BaseAttackPower => baseAttackPower;
    public float BaseGatherPower => baseGatherPower;
    public float BaseWorkSpeed => baseWorkSpeed;

    public int Level => level;
    public float CurrentExp => currentExp;
    public float ExpToNextLevel => expToNextLevel;
    public float Hunger => hunger;
    public float Loyalty => loyalty;
    public float MentalHealth => mentalHealth;
    public float AttackSpeed => baseAttackSpeed * GetTraitMultiplier(UnitStatType.AttackSpeed);
    public UnitInventory Inventory => inventory;
    public List<UnitTraitSO> Traits => traits;
    public NavMeshAgent Agent => agent;
    public bool IsIdle => Blackboard?.IsIdle ?? true;
    public bool HasTask => Blackboard?.CurrentTask != null;
    public UnitState CurrentState => Blackboard?.CurrentState ?? UnitState.Idle;
    public bool IsHungry => hunger < 30f;
    public bool IsStarving => hunger <= 0f;
    public bool IsDisloyal => loyalty < 50f;
    public bool IsMentallyUnstable => mentalHealth < 20f;
    public bool IsMentallyStressed => mentalHealth < 50f;
    public UnitTask CurrentTask => null;

    // ★ 침대 관련 Properties
    public BedComponent AssignedBed => assignedBed;
    public bool HasBed => assignedBed != null;

    // ==================== Events ====================

    public event Action<Unit> OnUnitDeath;
    public event Action<Unit, UnitState> OnStateChanged;
    public event Action<Unit, UnitTask> OnTaskCompleted;
    public event Action<Unit, int> OnLevelUp;

    // ==================== Unity Methods ====================

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        movement = GetComponent<UnitMovement>();
        Blackboard = new UnitBlackboard();
        _legacyStats = new UnitStats();
    }

    private void Start() => Initialize();

    // ==================== Initialize ====================

    public void Initialize(string name = null, List<UnitTraitSO> initialTraits = null, UnitType? type = null)
    {
        unitName = !string.IsNullOrEmpty(name) ? name :
                   string.IsNullOrEmpty(unitName) ? $"Unit_{UnityEngine.Random.Range(1000, 9999)}" : unitName;

        if (type.HasValue)
            unitType = type.Value;

        currentHP = maxHP;
        hunger = loyalty = mentalHealth = 100f;
        currentExp = 0f;
        level = 1;
        expToNextLevel = 100f;

        inventory.Initialize(5, 5);
        Blackboard.Reset();
        SyncToBlackboard();

        _legacyStats.Initialize(maxHP);

        if (initialTraits != null)
            traits = initialTraits;

        ApplyTraits();
        agent.speed = moveSpeed;

        Blackboard.OnStateChanged += state => OnStateChanged?.Invoke(this, state);
        Blackboard.OnLevelUp += lvl => { level = lvl; OnLevelUp?.Invoke(this, lvl); OnLevelUpInternal(lvl); };

        Debug.Log($"[Unit] {unitName} 초기화 완료 - 타입: {unitType}, 채집력: {GatherPower:F2}, 공격력: {AttackPower:F2}");
    }

    private void SyncToBlackboard()
    {
        Blackboard.Hunger = hunger;
        Blackboard.Loyalty = loyalty;
        Blackboard.MentalHealth = mentalHealth;
        Blackboard.Level = level;
        Blackboard.CurrentExp = currentExp;
        Blackboard.ExpToNextLevel = expToNextLevel;
    }

    // ==================== Type Efficiency ====================

    /// <summary>
    /// 타입별 채집 효율 계산
    /// Worker: 1.5배, Fighter: 0.75배
    /// </summary>
    private float GetGatherEfficiency()
    {
        return unitType switch
        {
            UnitType.Worker => TYPE_BONUS,
            UnitType.Fighter => TYPE_PENALTY,
            _ => 1f
        };
    }

    /// <summary>
    /// 타입별 전투 효율 계산
    /// Fighter: 1.5배, Worker: 0.75배
    /// </summary>
    private float GetCombatEfficiency()
    {
        return unitType switch
        {
            UnitType.Fighter => TYPE_BONUS,
            UnitType.Worker => TYPE_PENALTY,
            _ => 1f
        };
    }

    /// <summary>
    /// 최종 공격력 계산 (타입 효율 + 특성)
    /// </summary>
    private float CalculateAttackPower()
    {
        return baseAttackPower * GetCombatEfficiency() * GetTraitMultiplier(UnitStatType.AttackPower);
    }

    /// <summary>
    /// 최종 채집력 계산 (타입 효율 + 특성, 노드 타입 무관)
    /// </summary>
    private float CalculateGatherPower()
    {
        return baseGatherPower * GetGatherEfficiency() * GetTraitMultiplier(UnitStatType.GatherPower);
    }

    // ==================== Movement ====================

    public void MoveTo(Vector3 destination)
    {
        Blackboard.TargetPosition = destination;
        agent.speed = moveSpeed;
        if (movement != null) movement.MoveTo(destination, MovementStyle.Natural);
        else agent.SetDestination(destination);
    }

    public void RunTo(Vector3 destination)
    {
        Blackboard.TargetPosition = destination;
        agent.speed = runSpeed;
        if (movement != null) movement.MoveTo(destination, MovementStyle.Urgent);
        else agent.SetDestination(destination);
    }

    public void StopMoving()
    {
        Blackboard.TargetPosition = null;
        if (movement != null) movement.Stop();
        else agent.ResetPath();
        agent.speed = moveSpeed;
    }

    /// <summary>
    /// 모든 행동 중지 (명령 대기 상태)
    /// </summary>
    public void StopAllActions()
    {
        StopMoving();

        if (Blackboard != null)
        {
            Blackboard.TargetPosition = null;
            Blackboard.TargetObject = null;
            Blackboard.CurrentTask = null;
            Blackboard.HasPlayerCommand = false;
            Blackboard.PlayerCommand = null;
            Blackboard.SetState(UnitState.Idle);
        }

        var unitAI = GetComponent<UnitAI>();
        if (unitAI != null)
        {
            unitAI.CancelCurrentTask();
        }

        Debug.Log($"[Unit] {unitName}: 모든 행동 중지");
    }

    public bool HasArrivedAtDestination()
    {
        if (agent.pathPending) return false;
        return agent.remainingDistance <= agent.stoppingDistance &&
               (!agent.hasPath || agent.velocity.sqrMagnitude < 0.1f);
    }

    public bool IsInDanger() => IsStarving || Blackboard.CurrentState == UnitState.Fleeing;

    // ==================== HP ====================

    public void TakeDamage(float damage)
    {
        float old = currentHP;
        currentHP = Mathf.Max(0, currentHP - damage);
        _legacyStats.TakeDamage(damage);
        if (currentHP <= 0 && old > 0) Die();
    }

    public void Heal(float amount)
    {
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        _legacyStats.Heal(amount);
    }

    public void Eat(float nutritionValue)
    {
        hunger = Mathf.Min(100f, hunger + nutritionValue);
        Blackboard.Eat(nutritionValue);
        _legacyStats.Eat(nutritionValue);
    }

    private void Die()
    {
        Blackboard.IsAlive = false;
        inventory.DropAllItems();

        // ★ 침대 해제
        if (assignedBed != null)
        {
            assignedBed.RemoveOwner();
            assignedBed = null;
        }

        OnUnitDeath?.Invoke(this);
        Destroy(gameObject);
    }

    // ==================== Needs ====================

    public void DecreaseHunger(float amount)
    {
        hunger = Mathf.Max(0, hunger - amount);
        Blackboard.DecreaseHunger(amount);
        if (hunger <= 0) TakeDamage(amount * 0.5f);
    }

    public void ModifyLoyalty(float amount)
    {
        loyalty = Mathf.Clamp(loyalty + amount, 0f, 100f);
        Blackboard.ModifyLoyalty(amount);
    }

    public void ModifyMentalHealth(float amount)
    {
        mentalHealth = Mathf.Clamp(mentalHealth + amount, 0f, 100f);
        Blackboard.ModifyMentalHealth(amount);
    }

    public void IncreaseMentalHealth(float amount) => ModifyMentalHealth(Mathf.Abs(amount));
    public void DecreaseMentalHealth(float amount) => ModifyMentalHealth(-Mathf.Abs(amount));

    // 호환성용 (기존 코드와의 연동)
    public void ReduceStress(float amount) => IncreaseMentalHealth(amount);
    public void IncreaseStress(float amount) => DecreaseMentalHealth(amount);

    // ==================== Experience ====================

    public void GainExp(float amount)
    {
        if (amount <= 0) return;
        currentExp += amount;
        Blackboard.CurrentExp = currentExp;
        while (currentExp >= expToNextLevel)
        {
            currentExp -= expToNextLevel;
            LevelUp();
        }
    }

    public void GainExpFromAction(ExpGainAction action, float multiplier = 1f)
    {
        float baseExp = action switch { ExpGainAction.Combat => 2f, ExpGainAction.Social => 0.5f, _ => 1f };
        GainExp(baseExp * multiplier);
    }

    private void LevelUp()
    {
        level++;
        Blackboard.Level = level;
        Blackboard.CurrentExp = currentExp;
        OnLevelUp?.Invoke(this, level);
        OnLevelUpInternal(level);
    }

    private void OnLevelUpInternal(int newLevel) => inventory.SetSlotsByLevel(newLevel, 1);

    // ==================== Work & Combat ====================

    /// <summary>
    /// 건설 작업 수행 (타입 효율 적용 안함, 특성만 적용)
    /// </summary>
    public float DoWork()
    {
        Blackboard.LastWorkedTime = Time.time;
        GainExpFromAction(ExpGainAction.Construct);
        return baseWorkSpeed * GetTraitMultiplier(UnitStatType.WorkSpeed);
    }

    /// <summary>
    /// 자원 채집 (타입 효율 + 특성 + 노드 타입 보너스)
    /// Worker: 기본 1.5배
    /// 특성 예: 나무꾼이 나무 채집 시 추가 보너스
    /// </summary>
    public float DoGather(ResourceNodeType? nodeType = null)
    {
        GainExpFromAction(ExpGainAction.Harvest);

        float result = baseGatherPower;
        result *= GetGatherEfficiency();
        result *= GetTraitMultiplier(UnitStatType.GatherPower);

        if (nodeType.HasValue)
            result *= GetTraitMultiplier(UnitStatType.GatherPower, nodeType);

        return result;
    }

    /// <summary>
    /// 전투 데미지 계산 (타입 효율 + 특성)
    /// Fighter: 기본 1.5배
    /// </summary>
    public float DoAttack()
    {
        GainExpFromAction(ExpGainAction.Combat);
        return baseAttackPower * GetCombatEfficiency() * GetTraitMultiplier(UnitStatType.AttackPower);
    }

    /// <summary>
    /// 특정 대상에게 공격 (향후 전투 시스템용)
    /// </summary>
    public float DoAttack(GameObject target)
    {
        float damage = DoAttack();
        Debug.Log($"[Unit] {unitName} 공격! 데미지: {damage:F1} (타입: {unitType})");
        return damage;
    }

    /// <summary>
    /// 받는 데미지 계산 (방어력 특성 적용)
    /// </summary>
    public float CalculateIncomingDamage(float rawDamage)
    {
        float defenseMultiplier = GetTraitMultiplier(UnitStatType.MaxHP);
        return rawDamage * defenseMultiplier;
    }

    public void OnItemPickedUp() => GainExpFromAction(ExpGainAction.PickupItem);
    public void OnDeliveryComplete() => GainExpFromAction(ExpGainAction.Deliver);

    // ==================== Command ====================

    public bool ShouldIgnoreCommand() => Blackboard.ShouldIgnoreCommand();
    public float GetCommandIgnoreChance() => Blackboard.CommandIgnoreChance;

    // ==================== ★ 침대 시스템 ====================

    /// <summary>
    /// 침대 배정 (BedComponent에서 호출)
    /// </summary>
    public void AssignBed(BedComponent bed)
    {
        assignedBed = bed;
        Debug.Log($"[Unit] {unitName}: 침대 배정됨");
    }

    /// <summary>
    /// 침대 해제 (BedComponent에서 호출)
    /// </summary>
    public void RemoveBed()
    {
        assignedBed = null;
        Debug.Log($"[Unit] {unitName}: 침대 해제됨");
    }

    // ==================== Traits ====================

    private void ApplyTraits()
    {
        foreach (var trait in traits)
        {
            foreach (var effect in trait.Effects)
            {
                if (!effect.HasNodeTypeCondition)
                {
                    ApplyStatModifier(effect.AffectedStat, effect.Multiplier);
                }
            }
        }
        agent.speed = moveSpeed;
    }

    private void ApplyStatModifier(UnitStatType stat, float multiplier)
    {
        switch (stat)
        {
            case UnitStatType.MoveSpeed: moveSpeed *= multiplier; break;
        }
    }

    /// <summary>
    /// 특성 배율 계산 (노드 타입 조건 포함)
    /// </summary>
    public float GetTraitMultiplier(UnitStatType statType, ResourceNodeType? nodeType = null)
    {
        float multiplier = 1f;

        foreach (var trait in traits)
        {
            foreach (var effect in trait.Effects)
            {
                if (effect.AffectedStat != statType) continue;

                if (!effect.HasNodeTypeCondition)
                {
                    multiplier *= effect.Multiplier;
                }
                else if (nodeType.HasValue && effect.TargetNodeType == nodeType.Value)
                {
                    multiplier *= effect.Multiplier;
                }
            }
        }

        return multiplier;
    }

    /// <summary>
    /// 특성 추가
    /// </summary>
    public void AddTrait(UnitTraitSO trait)
    {
        if (trait == null || traits.Contains(trait)) return;

        traits.Add(trait);

        foreach (var effect in trait.Effects)
        {
            if (!effect.HasNodeTypeCondition)
                ApplyStatModifier(effect.AffectedStat, effect.Multiplier);
        }

        Debug.Log($"[Unit] {unitName}: 특성 '{trait.TraitName}' 추가됨");
    }

    /// <summary>
    /// 특성 제거
    /// </summary>
    public void RemoveTrait(UnitTraitSO trait)
    {
        if (trait == null || !traits.Contains(trait)) return;

        foreach (var effect in trait.Effects)
        {
            if (!effect.HasNodeTypeCondition && effect.Multiplier != 0)
                ApplyStatModifier(effect.AffectedStat, 1f / effect.Multiplier);
        }

        traits.Remove(trait);
        Debug.Log($"[Unit] {unitName}: 특성 '{trait.TraitName}' 제거됨");
    }

    /// <summary>
    /// 특정 특성 보유 여부
    /// </summary>
    public bool HasTrait(TraitType traitType)
    {
        foreach (var trait in traits)
        {
            if (trait.Type == traitType)
                return true;
        }
        return false;
    }

    // ==================== Type Change ====================

    /// <summary>
    /// 유닛 타입 변경 (직업 전환)
    /// </summary>
    public void SetUnitType(UnitType newType)
    {
        if (unitType == newType) return;

        UnitType oldType = unitType;
        unitType = newType;

        Debug.Log($"[Unit] {unitName}: 타입 변경 {oldType} → {newType}");
        Debug.Log($"[Unit] 새 스탯 - 채집력: {GatherPower:F2}, 공격력: {AttackPower:F2}");
    }

    // ==================== Utility ====================

    public string GetStatusString() =>
        $"[{unitType}] Lv.{level} HP:{currentHP:F0}/{maxHP:F0} 채집:{GatherPower:F1} 공격:{AttackPower:F1}";

    public string GetDetailedStatus() =>
        $"=== {unitName} ({unitType}) ===\n" +
        $"레벨: {level} (EXP: {currentExp:F0}/{expToNextLevel:F0})\n" +
        $"HP: {currentHP:F0}/{maxHP:F0}\n" +
        $"배고픔: {hunger:F0} | 충성도: {loyalty:F0} | 정신력: {mentalHealth:F0}\n" +
        $"채집력: {GatherPower:F2} (기본 {baseGatherPower} × {GetGatherEfficiency():F2})\n" +
        $"공격력: {AttackPower:F2} (기본 {baseAttackPower} × {GetCombatEfficiency():F2})\n" +
        $"침대: {(HasBed ? assignedBed.name : "없음")}\n" +
        $"특성: {traits.Count}개";

    // 호환용
    public void AssignTask(UnitTask task) { }
    public void AssignTaskImmediate(UnitTask task) { }
}