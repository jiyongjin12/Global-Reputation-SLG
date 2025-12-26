using System;
using UnityEngine;

/// <summary>
/// 유닛 스탯 타입
/// </summary>
public enum UnitStatType
{
    HP,
    MaxHP,
    Hunger,         // 배고픔 (0~100, 0이면 굶주림)
    Loyalty,        // 충성심 (0~100)
    MoveSpeed,
    WorkSpeed,      // 작업 속도 배율
    AttackPower,
    GatherPower     // 채집 효율 배율
}

/// <summary>
/// 유닛 스탯 관리 클래스
/// </summary>
[Serializable]
public class UnitStats
{
    [Header("Base Stats")]
    [SerializeField] private float maxHP = 100f;
    [SerializeField] private float currentHP = 100f;
    [SerializeField] private float hunger = 100f;      // 100 = 배부름, 0 = 굶주림
    [SerializeField] private float loyalty = 100f;     // 100 = 충성, 0 = 반란 가능

    [Header("Work Stats")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float workSpeed = 1f;     // 작업 속도 배율
    [SerializeField] private float gatherPower = 1f;   // 채집 효율

    [Header("Combat Stats")]
    [SerializeField] private float attackPower = 10f;

    // 이벤트
    public event Action OnDeath;
    public event Action<UnitStatType, float, float> OnStatChanged; // (type, oldVal, newVal)
    public event Action OnHungerCritical;  // 배고픔이 위험 수준
    public event Action OnLoyaltyCritical; // 충성심이 위험 수준

    // Properties
    public float MaxHP => maxHP;
    public float CurrentHP => currentHP;
    public float HPPercent => maxHP > 0 ? currentHP / maxHP : 0f;
    public float Hunger => hunger;
    public float Loyalty => loyalty;
    public float MoveSpeed => moveSpeed;
    public float WorkSpeed => workSpeed;
    public float GatherPower => gatherPower;
    public float AttackPower => attackPower;

    public bool IsAlive => currentHP > 0;
    public bool IsHungry => hunger < 30f;
    public bool IsStarving => hunger <= 0f;
    public bool IsDisloyal => loyalty < 20f;

    /// <summary>
    /// 스탯 초기화
    /// </summary>
    public void Initialize(float baseMaxHP = 100f)
    {
        maxHP = baseMaxHP;
        currentHP = maxHP;
        hunger = 100f;
        loyalty = 100f;
    }

    /// <summary>
    /// 데미지 받기
    /// </summary>
    public void TakeDamage(float damage)
    {
        float oldHP = currentHP;
        currentHP = Mathf.Max(0, currentHP - damage);
        OnStatChanged?.Invoke(UnitStatType.HP, oldHP, currentHP);

        if (currentHP <= 0)
        {
            OnDeath?.Invoke();
        }
    }

    /// <summary>
    /// 체력 회복
    /// </summary>
    public void Heal(float amount)
    {
        float oldHP = currentHP;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        OnStatChanged?.Invoke(UnitStatType.HP, oldHP, currentHP);
    }

    /// <summary>
    /// 배고픔 감소 (시간에 따라 호출)
    /// </summary>
    public void DecreaseHunger(float amount)
    {
        float oldHunger = hunger;
        hunger = Mathf.Max(0, hunger - amount);
        OnStatChanged?.Invoke(UnitStatType.Hunger, oldHunger, hunger);

        if (hunger < 30f && oldHunger >= 30f)
        {
            OnHungerCritical?.Invoke();
        }

        // 굶주리면 체력도 감소
        if (hunger <= 0)
        {
            TakeDamage(amount * 0.5f);
        }
    }

    /// <summary>
    /// 음식 먹기
    /// </summary>
    public void Eat(float nutritionValue)
    {
        float oldHunger = hunger;
        hunger = Mathf.Min(100f, hunger + nutritionValue);
        OnStatChanged?.Invoke(UnitStatType.Hunger, oldHunger, hunger);
    }

    /// <summary>
    /// 충성심 변화
    /// </summary>
    public void ModifyLoyalty(float amount)
    {
        float oldLoyalty = loyalty;
        loyalty = Mathf.Clamp(loyalty + amount, 0f, 100f);
        OnStatChanged?.Invoke(UnitStatType.Loyalty, oldLoyalty, loyalty);

        if (loyalty < 20f && oldLoyalty >= 20f)
        {
            OnLoyaltyCritical?.Invoke();
        }
    }

    /// <summary>
    /// 특성에 의한 스탯 버프
    /// </summary>
    public void ApplyBuff(UnitStatType statType, float multiplier)
    {
        switch (statType)
        {
            case UnitStatType.WorkSpeed:
                workSpeed *= multiplier;
                break;
            case UnitStatType.GatherPower:
                gatherPower *= multiplier;
                break;
            case UnitStatType.MoveSpeed:
                moveSpeed *= multiplier;
                break;
            case UnitStatType.AttackPower:
                attackPower *= multiplier;
                break;
        }
    }
}