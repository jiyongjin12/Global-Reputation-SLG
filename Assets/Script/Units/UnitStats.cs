using System;
using UnityEngine;

/// <summary>
/// 유닛 스탯 타입
/// </summary>
public enum UnitStatType
{
    HP, MaxHP, Hunger, Loyalty, Stress,
    MoveSpeed, WorkSpeed, AttackPower, GatherPower
}

/// <summary>
/// 경험치 획득 행동 타입
/// </summary>
public enum ExpGainAction
{
    Harvest, Construct, PickupItem, Deliver, Combat, Craft, Social
}

/// <summary>
/// 유닛 스탯 관리 클래스
/// </summary>
[Serializable]
public class UnitStats
{
    [Header("=== 기본 스탯 ===")]
    [SerializeField] private float maxHP = 100f;
    [SerializeField] private float currentHP = 100f;
    [SerializeField] private float hunger = 100f;
    [SerializeField] private float loyalty = 100f;

    [Header("=== 레벨 시스템 ===")]
    [SerializeField] private int level = 1;
    [SerializeField] private float currentExp = 0f;
    [SerializeField] private float expToNextLevel = 100f;

    [Header("=== 스트레스 ===")]
    [SerializeField] private float stress = 0f;

    [Header("=== 능력치 ===")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float workSpeed = 1f;
    [SerializeField] private float gatherPower = 1f;
    [SerializeField] private float attackPower = 10f;

    // Events
    public event Action OnDeath;
    public event Action<UnitStatType, float, float> OnStatChanged;
    public event Action OnHungerCritical;
    public event Action OnLoyaltyCritical;
    public event Action<int> OnLevelUp;
    public event Action OnStressCritical;

    // Properties
    public float MaxHP => maxHP;
    public float CurrentHP => currentHP;
    public float HPPercent => maxHP > 0 ? currentHP / maxHP : 0f;
    public float Hunger => hunger;
    public float Loyalty => loyalty;
    public float Stress => stress;
    public int Level => level;
    public float CurrentExp => currentExp;
    public float ExpToNextLevel => expToNextLevel;
    public float ExpPercent => expToNextLevel > 0 ? currentExp / expToNextLevel : 0f;
    public float MoveSpeed => moveSpeed;
    public float WorkSpeed => workSpeed;
    public float GatherPower => gatherPower;
    public float AttackPower => attackPower;

    public bool IsAlive => currentHP > 0;
    public bool IsHungry => hunger < 30f;
    public bool IsStarving => hunger <= 0f;
    public bool IsDisloyal => loyalty < 50f;
    public bool IsStressed => stress >= 80f;

    /// <summary>
    /// 명령 무시 확률 (50 이상: 0%, 49: 10%, 0: 25%)
    /// </summary>
    public float CommandIgnoreChance => loyalty >= 50f ? 0f : 0.10f + ((49f - loyalty) / 49f) * 0.15f;

    public bool ShouldIgnoreCommand() => loyalty < 50f && UnityEngine.Random.value < CommandIgnoreChance;

    public void Initialize(float baseMaxHP = 100f)
    {
        maxHP = baseMaxHP;
        currentHP = maxHP;
        hunger = loyalty = 100f;
        stress = currentExp = 0f;
        level = 1;
        expToNextLevel = 100f;
    }

    public void TakeDamage(float damage)
    {
        float old = currentHP;
        currentHP = Mathf.Max(0, currentHP - damage);
        OnStatChanged?.Invoke(UnitStatType.HP, old, currentHP);
        if (currentHP <= 0 && old > 0) OnDeath?.Invoke();
    }

    public void Heal(float amount)
    {
        float old = currentHP;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        OnStatChanged?.Invoke(UnitStatType.HP, old, currentHP);
    }

    public void DecreaseHunger(float amount)
    {
        float old = hunger;
        hunger = Mathf.Max(0, hunger - amount);
        OnStatChanged?.Invoke(UnitStatType.Hunger, old, hunger);
        if (hunger < 30f && old >= 30f) OnHungerCritical?.Invoke();
        if (hunger <= 0) TakeDamage(amount * 0.5f);
    }

    public void Eat(float nutritionValue)
    {
        float old = hunger;
        hunger = Mathf.Min(100f, hunger + nutritionValue);
        OnStatChanged?.Invoke(UnitStatType.Hunger, old, hunger);
    }

    public void ModifyLoyalty(float amount)
    {
        float old = loyalty;
        loyalty = Mathf.Clamp(loyalty + amount, 0f, 100f);
        OnStatChanged?.Invoke(UnitStatType.Loyalty, old, loyalty);
        if (loyalty < 50f && old >= 50f) OnLoyaltyCritical?.Invoke();
    }

    public void ModifyStress(float amount)
    {
        float old = stress;
        stress = Mathf.Clamp(stress + amount, 0f, 100f);
        OnStatChanged?.Invoke(UnitStatType.Stress, old, stress);
        if (stress >= 80f && old < 80f) OnStressCritical?.Invoke();
    }

    public void ReduceStress(float amount) => ModifyStress(-Mathf.Abs(amount));
    public void IncreaseStress(float amount) => ModifyStress(Mathf.Abs(amount));

    public void GainExp(float amount)
    {
        if (amount <= 0) return;
        currentExp += amount;
        while (currentExp >= expToNextLevel)
        {
            currentExp -= expToNextLevel;
            level++;
            OnLevelUp?.Invoke(level);
        }
    }

    public void GainExpFromAction(ExpGainAction action, float multiplier = 1f)
    {
        float baseExp = action switch { ExpGainAction.Combat => 2f, ExpGainAction.Social => 0.5f, _ => 1f };
        GainExp(baseExp * multiplier);
    }

    public void ApplyBuff(UnitStatType stat, float multiplier)
    {
        switch (stat)
        {
            case UnitStatType.WorkSpeed: workSpeed *= multiplier; break;
            case UnitStatType.GatherPower: gatherPower *= multiplier; break;
            case UnitStatType.MoveSpeed: moveSpeed *= multiplier; break;
            case UnitStatType.AttackPower: attackPower *= multiplier; break;
        }
    }
}