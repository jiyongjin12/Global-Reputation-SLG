using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 유닛 관리자
/// 유닛 생성, 추적, 관리
/// </summary>
public class UnitManager : MonoBehaviour
{
    public static UnitManager Instance { get; private set; }

    [Header("Unit Prefabs")]
    [SerializeField] private GameObject workerPrefab;
    [SerializeField] private GameObject fighterPrefab;

    [Header("Trait Database")]
    [SerializeField] private TraitDatabaseSO traitDatabase;

    [Header("Settings")]
    [SerializeField] private int maxUnits = 20;
    [SerializeField] private Transform unitContainer; // 정리용 부모 오브젝트

    // 생성된 유닛 목록
    private List<Unit> allUnits = new List<Unit>();
    private List<Unit> workers = new List<Unit>();
    private List<Unit> fighters = new List<Unit>();

    // Properties
    public int TotalUnitCount => allUnits.Count;
    public int WorkerCount => workers.Count;
    public int FighterCount => fighters.Count;
    public bool CanSpawnMoreUnits => allUnits.Count < maxUnits;
    public List<Unit> AllUnits => allUnits;

    // 이벤트
    public event Action<Unit> OnUnitSpawned;
    public event Action<Unit> OnUnitDied;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 일꾼 유닛 생성
    /// </summary>
    public Unit SpawnWorker(Vector3 position, string name = null)
    {
        return SpawnUnit(UnitType.Worker, position, name);
    }

    /// <summary>
    /// 전투 유닛 생성
    /// </summary>
    public Unit SpawnFighter(Vector3 position, string name = null)
    {
        return SpawnUnit(UnitType.Fighter, position, name);
    }

    /// <summary>
    /// 유닛 생성
    /// </summary>
    public Unit SpawnUnit(UnitType type, Vector3 position, string name = null)
    {
        if (!CanSpawnMoreUnits)
        {
            Debug.LogWarning("[UnitManager] Maximum unit capacity reached!");
            return null;
        }

        GameObject prefab = type == UnitType.Worker ? workerPrefab : fighterPrefab;
        if (prefab == null)
        {
            Debug.LogError($"[UnitManager] No prefab assigned for unit type {type}!");
            return null;
        }

        // 유닛 생성
        GameObject unitObj = Instantiate(prefab, position, Quaternion.identity);
        if (unitContainer != null)
        {
            unitObj.transform.parent = unitContainer;
        }

        Unit unit = unitObj.GetComponent<Unit>();
        if (unit == null)
        {
            unit = unitObj.AddComponent<Unit>();
        }

        // 랜덤 특성 부여
        List<UnitTraitSO> traits = null;
        if (traitDatabase != null)
        {
            int positiveCount = UnityEngine.Random.Range(0, 3); // 0~2개
            int negativeCount = UnityEngine.Random.Range(0, 2); // 0~1개
            traits = traitDatabase.GetRandomTraits(positiveCount, negativeCount);
        }

        // 초기화
        unit.Initialize(name, traits);

        // 목록에 추가
        allUnits.Add(unit);
        if (type == UnitType.Worker)
            workers.Add(unit);
        else
            fighters.Add(unit);

        // TaskManager에 등록
        TaskManager.Instance?.RegisterUnit(unit);

        // 이벤트 연결
        unit.OnUnitDeath += HandleUnitDeath;

        OnUnitSpawned?.Invoke(unit);

        Debug.Log($"[UnitManager] Spawned {type} unit: {unit.UnitName}");
        return unit;
    }

    /// <summary>
    /// 유닛 제거
    /// </summary>
    public void DespawnUnit(Unit unit)
    {
        if (unit == null) return;

        allUnits.Remove(unit);
        workers.Remove(unit);
        fighters.Remove(unit);

        TaskManager.Instance?.UnregisterUnit(unit);

        Destroy(unit.gameObject);
    }

    /// <summary>
    /// 특정 위치에서 가장 가까운 유닛 찾기
    /// </summary>
    public Unit FindNearestUnit(Vector3 position, UnitType? type = null)
    {
        List<Unit> searchList = type switch
        {
            UnitType.Worker => workers,
            UnitType.Fighter => fighters,
            _ => allUnits
        };

        Unit nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var unit in searchList)
        {
            float dist = Vector3.Distance(unit.transform.position, position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = unit;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 유휴 유닛 찾기
    /// </summary>
    public List<Unit> GetIdleUnits(UnitType? type = null)
    {
        List<Unit> searchList = type switch
        {
            UnitType.Worker => workers,
            UnitType.Fighter => fighters,
            _ => allUnits
        };

        return searchList.FindAll(u => u.IsIdle);
    }

    /// <summary>
    /// 모든 유닛에게 위치로 이동 명령
    /// </summary>
    public void CommandAllUnitsMoveTo(Vector3 position)
    {
        foreach (var unit in allUnits)
        {
            var moveTask = new MoveToTask(position);
            unit.AssignTaskImmediate(moveTask);
        }
    }

    private void HandleUnitDeath(Unit unit)
    {
        allUnits.Remove(unit);
        workers.Remove(unit);
        fighters.Remove(unit);

        OnUnitDied?.Invoke(unit);

        Debug.Log($"[UnitManager] Unit died: {unit.UnitName}");
    }

    /// <summary>
    /// 디버그: 유닛 상태 출력
    /// </summary>
    [ContextMenu("Print Unit Status")]
    public void DebugPrintStatus()
    {
        Debug.Log($"[UnitManager] Total: {TotalUnitCount}/{maxUnits}, " +
                  $"Workers: {WorkerCount}, Fighters: {FighterCount}");

        foreach (var unit in allUnits)
        {
            Debug.Log($"  - {unit.UnitName} ({unit.Type}): " +
                      $"HP={unit.Stats.CurrentHP:F0}, " +
                      $"Hunger={unit.Stats.Hunger:F0}, " +
                      $"State={unit.CurrentState}");
        }
    }
}