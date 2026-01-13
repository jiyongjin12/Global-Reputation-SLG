using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 유닛 데이터 (관리용)
/// </summary>
[Serializable]
public class UnitData
{
    public Unit Unit;
    public int JoinedWeek;      // 입장 주차
    public int JoinedDay;       // 입장 일차
    public float JoinedTime;    // 입장 시간 (Time.time)

    public UnitData(Unit unit, int week, int day)
    {
        Unit = unit;
        JoinedWeek = week;
        JoinedDay = day;
        JoinedTime = Time.time;
    }

    /// <summary>
    /// 활동 일수 계산
    /// </summary>
    public int GetActiveDays(int currentDay)
    {
        return Mathf.Max(0, currentDay - JoinedDay);
    }
}

/// <summary>
/// 유닛 중앙 관리자
/// - 모든 유닛 리스트 관리
/// - 생성/제거 이벤트
/// - 타입별 필터링
/// - UnitListUI와 연동
/// </summary>
public class UnitManager : MonoBehaviour
{
    public static UnitManager Instance { get; private set; }

    [Header("=== 유닛 프리팹 ===")]
    [SerializeField] private GameObject unitPrefab;

    [Header("=== 스폰 설정 ===")]
    [SerializeField] private Transform defaultSpawnPoint;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // ==================== 유닛 리스트 ====================

    // 전체 유닛 데이터 리스트
    private List<UnitData> allUnits = new();

    // 타입별 캐시 (빠른 조회용)
    private Dictionary<UnitType, List<UnitData>> unitsByType = new();

    // ==================== 이벤트 ====================

    public event Action<Unit> OnUnitAdded;
    public event Action<Unit> OnUnitRemoved;
    public event Action<Unit, UnitType, UnitType> OnUnitTypeChanged;  // unit, oldType, newType
    public event Action OnUnitsChanged;  // 유닛 목록 변경 시

    // ==================== Properties ====================

    public int TotalUnitCount => allUnits.Count;
    public IReadOnlyList<UnitData> AllUnits => allUnits;

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            InitializeTypeDictionary();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // 씬에 이미 있는 유닛들 등록
        RegisterExistingUnits();
    }

    private void InitializeTypeDictionary()
    {
        // 모든 UnitType에 대해 빈 리스트 생성
        foreach (UnitType type in Enum.GetValues(typeof(UnitType)))
        {
            unitsByType[type] = new List<UnitData>();
        }
    }

    /// <summary>
    /// 씬에 이미 존재하는 유닛들 등록
    /// </summary>
    private void RegisterExistingUnits()
    {
        var existingUnits = FindObjectsOfType<Unit>();

        foreach (var unit in existingUnits)
        {
            if (unit != null && unit.IsAlive)
            {
                RegisterUnit(unit, GetCurrentWeek(), GetCurrentDay());
            }
        }

        if (showDebugLogs)
            Debug.Log($"[UnitManager] 기존 유닛 {existingUnits.Length}개 등록됨");
    }

    // ==================== 유닛 생성 ====================

    /// <summary>
    /// 새 유닛 생성 (기본)
    /// </summary>
    public Unit CreateUnit(Vector3 position, UnitType type = UnitType.Worker, string name = null)
    {
        if (unitPrefab == null)
        {
            Debug.LogError("[UnitManager] Unit Prefab이 설정되지 않았습니다!");
            return null;
        }

        GameObject unitObj = Instantiate(unitPrefab, position, Quaternion.identity);
        Unit unit = unitObj.GetComponent<Unit>();

        if (unit != null)
        {
            unit.Initialize(name, null, type);
            RegisterUnit(unit, GetCurrentWeek(), GetCurrentDay());

            if (showDebugLogs)
                Debug.Log($"[UnitManager] 유닛 생성: {unit.UnitName} ({type})");
        }

        return unit;
    }

    /// <summary>
    /// 유닛 생성 (상세 설정)
    /// </summary>
    public Unit CreateUnit(UnitCreationData creationData)
    {
        if (unitPrefab == null)
        {
            Debug.LogError("[UnitManager] Unit Prefab이 설정되지 않았습니다!");
            return null;
        }

        Vector3 spawnPos = creationData.SpawnPosition ??
                          (defaultSpawnPoint != null ? defaultSpawnPoint.position : Vector3.zero);

        GameObject unitObj = Instantiate(unitPrefab, spawnPos, Quaternion.identity);
        Unit unit = unitObj.GetComponent<Unit>();

        if (unit != null)
        {
            unit.Initialize(creationData.Name, creationData.Traits, creationData.Type);
            RegisterUnit(unit, GetCurrentWeek(), GetCurrentDay());

            if (showDebugLogs)
                Debug.Log($"[UnitManager] 유닛 생성: {unit.UnitName} ({creationData.Type})");
        }

        return unit;
    }

    /// <summary>
    /// 스폰 포인트에서 유닛 생성
    /// </summary>
    public Unit CreateUnitAtSpawnPoint(UnitType type = UnitType.Worker, string name = null)
    {
        Vector3 spawnPos = defaultSpawnPoint != null ? defaultSpawnPoint.position : Vector3.zero;
        return CreateUnit(spawnPos, type, name);
    }

    // ==================== 유닛 등록/해제 ====================

    /// <summary>
    /// 유닛 등록 (내부용)
    /// </summary>
    private void RegisterUnit(Unit unit, int week, int day)
    {
        // 이미 등록되어 있는지 확인
        if (GetUnitData(unit) != null)
        {
            if (showDebugLogs)
                Debug.LogWarning($"[UnitManager] 유닛 {unit.UnitName}은 이미 등록되어 있습니다.");
            return;
        }

        UnitData data = new UnitData(unit, week, day);
        allUnits.Add(data);
        unitsByType[unit.Type].Add(data);

        // 유닛 사망 이벤트 구독
        unit.OnUnitDeath += OnUnitDeath;

        // 이벤트 발생
        OnUnitAdded?.Invoke(unit);
        OnUnitsChanged?.Invoke();

        // UnitListUI에 알림
        UnitListUI.Instance?.OnUnitAdded(unit);
    }

    /// <summary>
    /// 유닛 등록 해제
    /// </summary>
    public void UnregisterUnit(Unit unit)
    {
        UnitData data = GetUnitData(unit);
        if (data == null) return;

        allUnits.Remove(data);
        unitsByType[unit.Type].Remove(data);

        // 이벤트 구독 해제
        unit.OnUnitDeath -= OnUnitDeath;

        // 이벤트 발생
        OnUnitRemoved?.Invoke(unit);
        OnUnitsChanged?.Invoke();

        // UnitListUI에 알림
        UnitListUI.Instance?.OnUnitRemoved(unit);

        if (showDebugLogs)
            Debug.Log($"[UnitManager] 유닛 등록 해제: {unit.UnitName}");
    }

    /// <summary>
    /// 유닛 사망 시 처리
    /// </summary>
    private void OnUnitDeath(Unit unit)
    {
        UnregisterUnit(unit);
    }

    // ==================== 유닛 조회 ====================

    /// <summary>
    /// 유닛 데이터 가져오기
    /// </summary>
    public UnitData GetUnitData(Unit unit)
    {
        return allUnits.FirstOrDefault(d => d.Unit == unit);
    }

    /// <summary>
    /// 모든 유닛 가져오기
    /// </summary>
    public List<Unit> GetAllUnits()
    {
        return allUnits.Where(d => d.Unit != null && d.Unit.IsAlive)
                       .Select(d => d.Unit)
                       .ToList();
    }

    /// <summary>
    /// 타입별 유닛 가져오기
    /// </summary>
    public List<Unit> GetUnitsByType(UnitType type)
    {
        if (!unitsByType.ContainsKey(type))
            return new List<Unit>();

        return unitsByType[type]
            .Where(d => d.Unit != null && d.Unit.IsAlive)
            .Select(d => d.Unit)
            .ToList();
    }

    /// <summary>
    /// 타입별 유닛 데이터 가져오기
    /// </summary>
    public List<UnitData> GetUnitDataByType(UnitType type)
    {
        if (!unitsByType.ContainsKey(type))
            return new List<UnitData>();

        return unitsByType[type]
            .Where(d => d.Unit != null && d.Unit.IsAlive)
            .ToList();
    }

    /// <summary>
    /// 타입별 유닛 수
    /// </summary>
    public int GetUnitCountByType(UnitType type)
    {
        if (!unitsByType.ContainsKey(type))
            return 0;

        return unitsByType[type].Count(d => d.Unit != null && d.Unit.IsAlive);
    }

    /// <summary>
    /// Idle 상태인 유닛들 가져오기
    /// </summary>
    public List<Unit> GetIdleUnits()
    {
        return allUnits
            .Where(d => d.Unit != null && d.Unit.IsAlive && d.Unit.IsIdle)
            .Select(d => d.Unit)
            .ToList();
    }

    /// <summary>
    /// 특정 위치에서 가장 가까운 유닛 찾기
    /// </summary>
    public Unit GetNearestUnit(Vector3 position, UnitType? typeFilter = null)
    {
        IEnumerable<UnitData> searchList = typeFilter.HasValue
            ? unitsByType[typeFilter.Value]
            : allUnits;

        Unit nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var data in searchList)
        {
            if (data.Unit == null || !data.Unit.IsAlive) continue;

            float dist = Vector3.Distance(position, data.Unit.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = data.Unit;
            }
        }

        return nearest;
    }

    // ==================== 유닛 타입 변경 ====================

    /// <summary>
    /// 유닛 타입 변경
    /// </summary>
    public void ChangeUnitType(Unit unit, UnitType newType)
    {
        UnitData data = GetUnitData(unit);
        if (data == null) return;

        UnitType oldType = unit.Type;
        if (oldType == newType) return;

        // 타입별 리스트에서 이동
        unitsByType[oldType].Remove(data);
        unitsByType[newType].Add(data);

        // 유닛 타입 변경
        unit.SetUnitType(newType);

        // 이벤트 발생
        OnUnitTypeChanged?.Invoke(unit, oldType, newType);
        OnUnitsChanged?.Invoke();

        if (showDebugLogs)
            Debug.Log($"[UnitManager] 유닛 타입 변경: {unit.UnitName} ({oldType} → {newType})");
    }

    // ==================== 주기 시스템 연동 ====================

    /// <summary>
    /// 현재 주차 가져오기 (TimeManager 연동)
    /// </summary>
    public int GetCurrentWeek()
    {
        // TODO: TimeManager와 연동
        // return TimeManager.Instance?.CurrentWeek ?? 1;
        return 1;  // 임시
    }

    /// <summary>
    /// 현재 일차 가져오기 (TimeManager 연동)
    /// </summary>
    public int GetCurrentDay()
    {
        // TODO: TimeManager와 연동
        // return TimeManager.Instance?.CurrentDay ?? 1;
        return 1;  // 임시
    }

    /// <summary>
    /// 유닛의 입장 주차 가져오기
    /// </summary>
    public int GetUnitJoinedWeek(Unit unit)
    {
        UnitData data = GetUnitData(unit);
        return data?.JoinedWeek ?? 0;
    }

    /// <summary>
    /// 유닛의 활동 일수 가져오기
    /// </summary>
    public int GetUnitActiveDays(Unit unit)
    {
        UnitData data = GetUnitData(unit);
        return data?.GetActiveDays(GetCurrentDay()) ?? 0;
    }

    // ==================== 디버그 / 테스트 ====================

    /// <summary>
    /// 테스트용 유닛 생성
    /// </summary>
    [ContextMenu("Create Test Worker")]
    public void CreateTestWorker()
    {
        CreateUnit(GetRandomSpawnPosition(), UnitType.Worker);
    }

    [ContextMenu("Create Test Fighter")]
    public void CreateTestFighter()
    {
        CreateUnit(GetRandomSpawnPosition(), UnitType.Fighter);
    }

    private Vector3 GetRandomSpawnPosition()
    {
        Vector3 basePos = defaultSpawnPoint != null ? defaultSpawnPoint.position : Vector3.zero;
        return basePos + new Vector3(
            UnityEngine.Random.Range(-5f, 5f),
            0,
            UnityEngine.Random.Range(-5f, 5f)
        );
    }

    /// <summary>
    /// 유닛 목록 출력
    /// </summary>
    [ContextMenu("Print Unit List")]
    public void PrintUnitList()
    {
        Debug.Log($"=== 유닛 목록 (총 {TotalUnitCount}명) ===");

        foreach (UnitType type in Enum.GetValues(typeof(UnitType)))
        {
            int count = GetUnitCountByType(type);
            Debug.Log($"  [{type}]: {count}명");

            foreach (var data in unitsByType[type])
            {
                if (data.Unit != null)
                {
                    Debug.Log($"    - {data.Unit.UnitName} (Lv.{data.Unit.Level}, 입장: {data.JoinedWeek}주차)");
                }
            }
        }
    }
}

/// <summary>
/// 유닛 생성 데이터
/// </summary>
[Serializable]
public class UnitCreationData
{
    public string Name;
    public UnitType Type = UnitType.Worker;
    public Vector3? SpawnPosition;
    public List<UnitTraitSO> Traits;

    public UnitCreationData() { }

    public UnitCreationData(string name, UnitType type)
    {
        Name = name;
        Type = type;
    }
}