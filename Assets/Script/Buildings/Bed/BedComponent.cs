using System;
using UnityEngine;

/// <summary>
/// 침대 컴포넌트
/// - Unit 소유자 시스템
/// - 수면 기능
/// - 주기 시스템 연동
/// </summary>
public class BedComponent : MonoBehaviour
{
    [Header("=== 침대 설정 ===")]
    [SerializeField] private float sleepDuration = 180f;  // 밤 시간과 맞추기 (3분)
    [SerializeField] private float mentalHealthRecovery = 10f;  // 수면 시 정신력 회복
    [SerializeField] private float hungerDecreaseWhileSleep = 5f;  // 수면 중 배고픔 감소량

    [Header("=== 위치 설정 ===")]
    [SerializeField] private Transform sleepPoint;  // 수면 위치
    [SerializeField] private float interactionRadius = 1.5f;

    [Header("=== 현재 상태 ===")]
    [SerializeField] private Unit owner;
    [SerializeField] private Unit currentSleeper;
    [SerializeField] private bool isOccupied;
    [SerializeField] private float sleepTimer;

    // 컴포넌트
    private Building building;

    // 이벤트
    public event Action<BedComponent, Unit> OnOwnerAssigned;
    public event Action<BedComponent> OnOwnerRemoved;
    public event Action<BedComponent, Unit> OnSleepStarted;
    public event Action<BedComponent, Unit> OnSleepEnded;
    public event Action<BedComponent, Unit> OnSleepInterrupted;

    // Properties
    public Unit Owner => owner;
    public Unit CurrentSleeper => currentSleeper;
    public bool HasOwner => owner != null;
    public bool IsOccupied => isOccupied;
    public bool IsAvailable => !HasOwner || (HasOwner && owner.IsAlive);
    public Transform SleepPoint => sleepPoint != null ? sleepPoint : transform;
    public Vector3 SleepPosition => SleepPoint.position;
    public float InteractionRadius => interactionRadius;
    public Building Building => building;

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        building = GetComponent<Building>();

        if (sleepPoint == null)
            sleepPoint = transform;
    }

    private void Start()
    {
        // BedManager에 등록
        BedManager.Instance?.RegisterBed(this);
    }

    private void OnDestroy()
    {
        // 소유자 해제
        if (owner != null)
        {
            RemoveOwner();
        }

        // BedManager에서 제거
        BedManager.Instance?.UnregisterBed(this);
    }

    private void Update()
    {
        if (isOccupied && currentSleeper != null)
        {
            UpdateSleep();
        }
    }

    // ==================== 소유자 시스템 ====================

    /// <summary>
    /// 소유자 지정
    /// </summary>
    public bool AssignOwner(Unit unit)
    {
        if (unit == null) return false;

        // 이미 소유자가 있으면 실패
        if (HasOwner && owner != unit)
        {
            Debug.LogWarning($"[Bed] 이미 {owner.UnitName}의 침대입니다.");
            return false;
        }

        // 해당 유닛이 이미 다른 침대를 가지고 있으면 해제
        var existingBed = BedManager.Instance?.GetBedForUnit(unit);
        if (existingBed != null && existingBed != this)
        {
            existingBed.RemoveOwner();
        }

        owner = unit;
        unit.AssignBed(this);

        // Unit 사망 시 소유권 해제
        unit.OnUnitDeath += HandleOwnerDeath;

        OnOwnerAssigned?.Invoke(this, unit);
        Debug.Log($"[Bed] {unit.UnitName}에게 침대 배정됨");

        return true;
    }

    /// <summary>
    /// 소유자 해제
    /// </summary>
    public void RemoveOwner()
    {
        if (owner == null) return;

        var previousOwner = owner;

        // 수면 중이면 깨우기
        if (isOccupied && currentSleeper == owner)
        {
            InterruptSleep();
        }

        previousOwner.OnUnitDeath -= HandleOwnerDeath;
        previousOwner.RemoveBed();

        owner = null;

        OnOwnerRemoved?.Invoke(this);
        Debug.Log($"[Bed] {previousOwner.UnitName}의 침대 배정 해제됨");
    }

    private void HandleOwnerDeath(Unit unit)
    {
        if (unit == owner)
        {
            RemoveOwner();
        }
    }

    // ==================== 수면 시스템 ====================

    /// <summary>
    /// 수면 시작 가능 여부
    /// </summary>
    public bool CanStartSleep(Unit unit)
    {
        if (unit == null) return false;
        if (isOccupied) return false;

        // 소유자가 있으면 소유자만 사용 가능
        if (HasOwner && owner != unit) return false;

        // 건물 완공 체크
        if (building != null && building.CurrentState != BuildingState.Completed)
            return false;

        return true;
    }

    /// <summary>
    /// 수면 범위 내인지 체크
    /// </summary>
    public bool IsInSleepRange(Vector3 position)
    {
        return Vector3.Distance(position, SleepPosition) <= interactionRadius;
    }

    /// <summary>
    /// 수면 시작
    /// </summary>
    public bool StartSleep(Unit unit)
    {
        if (!CanStartSleep(unit)) return false;

        currentSleeper = unit;
        isOccupied = true;
        sleepTimer = 0f;

        OnSleepStarted?.Invoke(this, unit);
        Debug.Log($"[Bed] {unit.UnitName} 수면 시작");

        return true;
    }

    /// <summary>
    /// 수면 업데이트 (매 프레임)
    /// </summary>
    private void UpdateSleep()
    {
        sleepTimer += Time.deltaTime;

        // 정신력 회복 (초당)
        float recoveryPerSecond = mentalHealthRecovery / sleepDuration;
        currentSleeper.IncreaseMentalHealth(recoveryPerSecond * Time.deltaTime);

        // 배고픔 감소 (수면 중에도 약간 감소)
        float hungerPerSecond = hungerDecreaseWhileSleep / sleepDuration;
        currentSleeper.DecreaseHunger(hungerPerSecond * Time.deltaTime);
    }

    /// <summary>
    /// 수면 완료
    /// </summary>
    public void CompleteSleep()
    {
        if (!isOccupied || currentSleeper == null) return;

        var sleeper = currentSleeper;

        isOccupied = false;
        currentSleeper = null;
        sleepTimer = 0f;

        OnSleepEnded?.Invoke(this, sleeper);
        Debug.Log($"[Bed] {sleeper.UnitName} 수면 완료");
    }

    /// <summary>
    /// 수면 중단 (깨우기)
    /// </summary>
    /// <param name="applyPenalty">페널티 적용 여부 (플레이어가 깨웠을 때)</param>
    public void InterruptSleep(bool applyPenalty = false)
    {
        if (!isOccupied || currentSleeper == null) return;

        var sleeper = currentSleeper;

        if (applyPenalty)
        {
            // 수면 중 깨우면 정신력 -5
            sleeper.DecreaseMentalHealth(5f);
            Debug.Log($"[Bed] {sleeper.UnitName} 강제 기상 - 정신력 -5");
        }

        isOccupied = false;
        currentSleeper = null;
        sleepTimer = 0f;

        OnSleepInterrupted?.Invoke(this, sleeper);
        Debug.Log($"[Bed] {sleeper.UnitName} 수면 중단");
    }

    // ==================== 유틸리티 ====================

    /// <summary>
    /// 침대 상태 문자열
    /// </summary>
    public string GetStatusString()
    {
        if (isOccupied)
            return $"수면 중: {currentSleeper?.UnitName}";

        if (HasOwner)
            return $"소유자: {owner.UnitName}";

        return "빈 침대";
    }

    // ==================== Gizmos ====================

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 pos = sleepPoint != null ? sleepPoint.position : transform.position;

        // 수면 범위
        Gizmos.color = isOccupied ? Color.blue : (HasOwner ? Color.cyan : Color.gray);
        Gizmos.DrawWireSphere(pos, interactionRadius);

        // 수면 위치
        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(pos, 0.2f);
    }
#endif
}