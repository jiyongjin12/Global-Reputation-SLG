using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 침대 관리자
/// - 모든 침대 관리
/// - Unit-침대 자동 매칭
/// - 빈 침대 찾기
/// </summary>
public class BedManager : MonoBehaviour
{
    public static BedManager Instance { get; private set; }

    [Header("=== 설정 ===")]
    [SerializeField] private bool autoAssignBeds = true;
    [SerializeField] private bool showDebugLogs = true;

    [Header("=== 노숙 페널티 ===")]
    [SerializeField] private float homelessLoyaltyPenalty = 3f;
    [SerializeField] private float homelessMentalPenalty = 3f;

    // 침대 목록
    private List<BedComponent> allBeds = new();
    private Dictionary<Unit, BedComponent> unitBedMap = new();

    // 이벤트
    public event Action<BedComponent> OnBedRegistered;
    public event Action<BedComponent> OnBedUnregistered;
    public event Action<Unit, BedComponent> OnBedAssigned;

    // Properties
    public int TotalBedCount => allBeds.Count;
    public int OccupiedBedCount => allBeds.Count(b => b.IsOccupied);
    public int AvailableBedCount => allBeds.Count(b => !b.HasOwner);
    public float HomelessLoyaltyPenalty => homelessLoyaltyPenalty;
    public float HomelessMentalPenalty => homelessMentalPenalty;

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // UnitManager 이벤트 구독 (★ 이벤트명: OnUnitAdded / OnUnitRemoved)
        if (UnitManager.Instance != null)
        {
            UnitManager.Instance.OnUnitAdded += HandleUnitAdded;
            UnitManager.Instance.OnUnitRemoved += HandleUnitRemoved;
        }

        // CycleManager 이벤트 구독
        if (CycleManager.Instance != null)
        {
            CycleManager.Instance.OnNightStart += HandleNightStart;
            CycleManager.Instance.OnDayStart += HandleDayStart;
        }
    }

    private void OnDestroy()
    {
        if (UnitManager.Instance != null)
        {
            UnitManager.Instance.OnUnitAdded -= HandleUnitAdded;
            UnitManager.Instance.OnUnitRemoved -= HandleUnitRemoved;
        }

        if (CycleManager.Instance != null)
        {
            CycleManager.Instance.OnNightStart -= HandleNightStart;
            CycleManager.Instance.OnDayStart -= HandleDayStart;
        }
    }

    // ==================== 침대 등록/해제 ====================

    public void RegisterBed(BedComponent bed)
    {
        if (bed == null || allBeds.Contains(bed)) return;

        allBeds.Add(bed);
        OnBedRegistered?.Invoke(bed);

        if (showDebugLogs)
            Debug.Log($"[BedManager] 침대 등록: {bed.name} (총 {allBeds.Count}개)");

        if (autoAssignBeds)
        {
            TryAutoAssignBed(bed);
        }
    }

    public void UnregisterBed(BedComponent bed)
    {
        if (bed == null || !allBeds.Contains(bed)) return;

        if (bed.HasOwner)
        {
            unitBedMap.Remove(bed.Owner);
        }

        allBeds.Remove(bed);
        OnBedUnregistered?.Invoke(bed);

        if (showDebugLogs)
            Debug.Log($"[BedManager] 침대 해제: {bed.name} (총 {allBeds.Count}개)");
    }

    // ==================== Unit 이벤트 ====================

    private void HandleUnitAdded(Unit unit)
    {
        if (!autoAssignBeds) return;

        var availableBed = GetAvailableBed();
        if (availableBed != null)
        {
            AssignBedToUnit(unit, availableBed);
        }
        else if (showDebugLogs)
        {
            Debug.LogWarning($"[BedManager] {unit.UnitName}에게 배정할 침대가 없습니다.");
        }
    }

    private void HandleUnitRemoved(Unit unit)
    {
        if (unitBedMap.TryGetValue(unit, out var bed))
        {
            bed.RemoveOwner();
            unitBedMap.Remove(unit);
        }
    }

    // ==================== 주기 이벤트 ====================

    private void HandleNightStart(CycleEventData data)
    {
        if (showDebugLogs)
            Debug.Log($"[BedManager] 밤 시작 - 유닛들 수면 유도");
    }

    private void HandleDayStart(CycleEventData data)
    {
        if (showDebugLogs)
            Debug.Log($"[BedManager] 낮 시작 - 유닛들 기상");

        foreach (var bed in allBeds)
        {
            if (bed.IsOccupied)
            {
                bed.CompleteSleep();
            }
        }
    }

    // ==================== 침대 배정 ====================

    public bool AssignBedToUnit(Unit unit, BedComponent bed)
    {
        if (unit == null || bed == null) return false;

        if (bed.AssignOwner(unit))
        {
            unitBedMap[unit] = bed;
            OnBedAssigned?.Invoke(unit, bed);

            if (showDebugLogs)
                Debug.Log($"[BedManager] {unit.UnitName}에게 침대 배정: {bed.name}");

            return true;
        }

        return false;
    }

    private void TryAutoAssignBed(BedComponent bed)
    {
        if (bed.HasOwner) return;

        var homelessUnit = GetHomelessUnit();
        if (homelessUnit != null)
        {
            AssignBedToUnit(homelessUnit, bed);
        }
    }

    // ==================== 조회 ====================

    public BedComponent GetBedForUnit(Unit unit)
    {
        if (unit == null) return null;

        unitBedMap.TryGetValue(unit, out var bed);
        return bed;
    }

    public BedComponent GetAvailableBed()
    {
        return allBeds.FirstOrDefault(b => !b.HasOwner && b.IsAvailable);
    }

    public BedComponent GetNearestAvailableBed(Vector3 position)
    {
        BedComponent nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var bed in allBeds)
        {
            if (bed.HasOwner || !bed.IsAvailable) continue;

            float dist = Vector3.Distance(position, bed.SleepPosition);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = bed;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 침대 없는 유닛 찾기 (★ UnitData.Unit으로 접근)
    /// </summary>
    public Unit GetHomelessUnit()
    {
        if (UnitManager.Instance == null) return null;

        foreach (var unitData in UnitManager.Instance.AllUnits)
        {
            if (unitData.Unit != null && unitData.Unit.IsAlive && !unitBedMap.ContainsKey(unitData.Unit))
            {
                return unitData.Unit;
            }
        }

        return null;
    }

    /// <summary>
    /// 침대 없는 유닛 목록 (★ UnitData.Unit으로 접근)
    /// </summary>
    public List<Unit> GetHomelessUnits()
    {
        var homeless = new List<Unit>();

        if (UnitManager.Instance == null) return homeless;

        foreach (var unitData in UnitManager.Instance.AllUnits)
        {
            if (unitData.Unit != null && unitData.Unit.IsAlive && !unitBedMap.ContainsKey(unitData.Unit))
            {
                homeless.Add(unitData.Unit);
            }
        }

        return homeless;
    }

    public bool HasBed(Unit unit)
    {
        return unit != null && unitBedMap.ContainsKey(unit);
    }

    // ==================== 노숙 처리 ====================

    public void ApplyHomelessPenalty(Unit unit)
    {
        if (unit == null) return;

        unit.ModifyLoyalty(-homelessLoyaltyPenalty);
        unit.DecreaseMentalHealth(homelessMentalPenalty);

        if (showDebugLogs)
            Debug.Log($"[BedManager] {unit.UnitName} 노숙 - 충성심 -{homelessLoyaltyPenalty}, 정신력 -{homelessMentalPenalty}");
    }

    // ==================== 디버그 ====================

    [ContextMenu("Print Bed Status")]
    public void DebugPrintStatus()
    {
        Debug.Log($"[BedManager] === 침대 상태 ===");
        Debug.Log($"  총 침대: {TotalBedCount}");
        Debug.Log($"  사용 중: {OccupiedBedCount}");
        Debug.Log($"  빈 침대: {AvailableBedCount}");
        Debug.Log($"  노숙자: {GetHomelessUnits().Count}명");

        foreach (var bed in allBeds)
        {
            Debug.Log($"    - {bed.name}: {bed.GetStatusString()}");
        }
    }

    [ContextMenu("Auto Assign All Beds")]
    public void AutoAssignAllBeds()
    {
        var homeless = GetHomelessUnits();
        var available = allBeds.Where(b => !b.HasOwner).ToList();

        int assigned = 0;
        for (int i = 0; i < Mathf.Min(homeless.Count, available.Count); i++)
        {
            if (AssignBedToUnit(homeless[i], available[i]))
                assigned++;
        }

        Debug.Log($"[BedManager] {assigned}명에게 침대 배정됨");
    }
}