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

    // 니즈
    [Range(0, 100)] public float Hunger = 100f;
    [Range(0, 100)] public float Loyalty = 100f;

    // 현재 목표
    public Vector3? TargetPosition;
    public GameObject TargetObject;
    public PostedTask CurrentTask;

    // 플레이어 명령
    public bool HasPlayerCommand;
    public UnitCommand PlayerCommand;

    // 감지된 정보
    public DroppedItem NearestFood;

    // 타이머
    public float LastAteTime;
    public float LastWorkedTime;

    // 이벤트
    public event Action OnHungerCritical;
    public event Action OnLoyaltyCritical;
    public event Action<UnitState> OnStateChanged;

    // 상태 체크
    public bool IsHungry => Hunger < 30f;
    public bool IsStarving => Hunger <= 0f;
    public bool IsDisloyal => Loyalty < 20f;
    public bool IsIdle => CurrentState == UnitState.Idle && CurrentTask == null && !HasPlayerCommand;

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
        float oldHunger = Hunger;
        Hunger = Mathf.Max(0, Hunger - amount);

        if (Hunger < 30f && oldHunger >= 30f)
            OnHungerCritical?.Invoke();
    }

    public void Eat(float nutrition)
    {
        Hunger = Mathf.Min(100f, Hunger + nutrition);
        LastAteTime = Time.time;
    }

    public void ModifyLoyalty(float amount)
    {
        float oldLoyalty = Loyalty;
        Loyalty = Mathf.Clamp(Loyalty + amount, 0f, 100f);

        if (Loyalty < 20f && oldLoyalty >= 20f)
            OnLoyaltyCritical?.Invoke();
    }

    public void Reset()
    {
        IsAlive = true;
        CurrentState = UnitState.Idle;
        Hunger = 100f;
        Loyalty = 100f;
        TargetPosition = null;
        TargetObject = null;
        CurrentTask = null;
        HasPlayerCommand = false;
        PlayerCommand = null;
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

public enum UnitCommandType
{
    MoveTo,
    Attack,
    Construct,
    Harvest,
    Stop
}