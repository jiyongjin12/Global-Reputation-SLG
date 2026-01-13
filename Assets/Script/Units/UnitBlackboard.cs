using System;
using UnityEngine;

/// <summary>
/// 유닛 공유 데이터 (Unit과 UnitAI가 함께 사용)
/// </summary>
[Serializable]
public class UnitBlackboard
{
    // 기본 상태
    public bool IsAlive = true;
    public UnitState CurrentState = UnitState.Idle;

    // 니즈 (모두 높을수록 좋음)
    [Range(0, 100)] public float Hunger = 100f;
    [Range(0, 100)] public float Loyalty = 100f;
    [Range(0, 100)] public float MentalHealth = 100f;  // 정신력 (높을수록 좋음)

    // 레벨
    public int Level = 1;
    public float CurrentExp = 0f;
    public float ExpToNextLevel = 100f;

    // 현재 목표
    public Vector3? TargetPosition;
    public GameObject TargetObject;
    public PostedTask CurrentTask;

    // 플레이어 명령
    public bool HasPlayerCommand;
    public UnitCommand PlayerCommand;

    // 감지된 정보
    public DroppedItem NearestFood;

    // 사회적 상호작용
    public Unit InteractionTarget;
    public float LastSocialInteractionTime = -100f;
    public float SocialCooldown = 30f;

    // 타이머
    public float LastAteTime;
    public float LastWorkedTime;

    // Events
    public event Action OnHungerCritical;
    public event Action OnLoyaltyCritical;
    public event Action OnMentalHealthCritical;
    public event Action<int> OnLevelUp;
    public event Action<UnitState> OnStateChanged;

    // Properties
    public bool IsHungry => Hunger < 30f;
    public bool IsStarving => Hunger <= 0f;
    public bool IsDisloyal => Loyalty < 50f;
    public bool IsMentallyUnstable => MentalHealth < 20f;   // 심각한 정신 상태
    public bool IsMentallyStressed => MentalHealth < 50f;   // 스트레스 받는 상태
    public bool IsIdle => CurrentState == UnitState.Idle && CurrentTask == null && !HasPlayerCommand;
    public bool CanSocialize => Time.time - LastSocialInteractionTime >= SocialCooldown;

    /// <summary>
    /// 명령 무시 확률 (50 이상: 0%, 49: 10%, 0: 25%)
    /// </summary>
    public float CommandIgnoreChance => Loyalty >= 50f ? 0f : 0.10f + ((49f - Loyalty) / 49f) * 0.15f;

    public bool ShouldIgnoreCommand() => Loyalty < 50f && UnityEngine.Random.value < CommandIgnoreChance;

    public void SetState(UnitState newState)
    {
        if (CurrentState != newState)
        {
            CurrentState = newState;
            OnStateChanged?.Invoke(newState);
        }
    }

    public void DecreaseHunger(float amount)
    {
        float old = Hunger;
        Hunger = Mathf.Max(0, Hunger - amount);
        if (Hunger < 30f && old >= 30f) OnHungerCritical?.Invoke();
    }

    public void Eat(float nutrition)
    {
        Hunger = Mathf.Min(100f, Hunger + nutrition);
        LastAteTime = Time.time;
    }

    public void ModifyLoyalty(float amount)
    {
        float old = Loyalty;
        Loyalty = Mathf.Clamp(Loyalty + amount, 0f, 100f);
        if (Loyalty < 50f && old >= 50f) OnLoyaltyCritical?.Invoke();
    }

    public void ModifyMentalHealth(float amount)
    {
        float old = MentalHealth;
        MentalHealth = Mathf.Clamp(MentalHealth + amount, 0f, 100f);
        if (MentalHealth < 20f && old >= 20f) OnMentalHealthCritical?.Invoke();
    }

    public void IncreaseMentalHealth(float amount) => ModifyMentalHealth(Mathf.Abs(amount));
    public void DecreaseMentalHealth(float amount) => ModifyMentalHealth(-Mathf.Abs(amount));

    // 호환성용
    public void ReduceStress(float amount) => IncreaseMentalHealth(amount);
    public void IncreaseStress(float amount) => DecreaseMentalHealth(amount);

    public void GainExp(float amount)
    {
        if (amount <= 0) return;
        CurrentExp += amount;
        while (CurrentExp >= ExpToNextLevel)
        {
            CurrentExp -= ExpToNextLevel;
            Level++;
            OnLevelUp?.Invoke(Level);
        }
    }

    public void StartSocialInteraction(Unit target)
    {
        InteractionTarget = target;
        LastSocialInteractionTime = Time.time;
    }

    public void EndSocialInteraction() => InteractionTarget = null;

    public void Reset()
    {
        IsAlive = true;
        CurrentState = UnitState.Idle;
        Hunger = Loyalty = MentalHealth = 100f;
        CurrentExp = 0f;
        Level = 1;
        ExpToNextLevel = 100f;
        TargetPosition = null;
        TargetObject = null;
        CurrentTask = null;
        HasPlayerCommand = false;
        PlayerCommand = null;
        InteractionTarget = null;
        LastSocialInteractionTime = -100f;
    }
}

/// <summary>
/// 플레이어 명령
/// </summary>
public class UnitCommand
{
    public UnitCommandType Type;
    public Vector3? TargetPosition;
    public GameObject TargetObject;

    public UnitCommand(UnitCommandType type, Vector3? position = null, GameObject target = null)
    {
        Type = type;
        TargetPosition = position;
        TargetObject = target;
    }
}

public enum UnitCommandType { MoveTo, Attack, Construct, Harvest, Stop }