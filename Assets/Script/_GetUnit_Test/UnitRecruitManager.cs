using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 유닛 고용 시스템 매니저
/// - 주기 전환 시 고용 가능한 유닛 후보 생성
/// - CycleManager와 연동
/// </summary>
public class UnitRecruitManager : MonoBehaviour
{
    public static UnitRecruitManager Instance { get; private set; }

    [Header("=== 유닛 설정 ===")]
    [Tooltip("유닛 프리팹 (UnitManager에서 사용)")]
    [SerializeField] private GameObject unitPrefab;

    [Tooltip("유닛 스폰 위치")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("유닛 기본 아이콘")]
    [SerializeField] private Sprite defaultWorkerIcon;
    [SerializeField] private Sprite defaultFighterIcon;

    [Header("=== 특성 풀 ===")]
    [Tooltip("사용 가능한 모든 특성들")]
    [SerializeField] private List<UnitTraitSO> allTraits = new List<UnitTraitSO>();

    [Header("=== 비용 설정 ===")]
    [Tooltip("기본 고용 비용 (코인)")]
    [SerializeField] private ResourceItemSO coinResource;
    [SerializeField] private int baseCoinCost = 5;

    [Tooltip("추가 비용 (음식)")]
    [SerializeField] private ResourceItemSO foodResource;
    [SerializeField] private int baseFoodCost = 3;

    [Header("=== 고용 설정 ===")]
    [Tooltip("한 번에 표시할 후보 유닛 수")]
    [SerializeField] private int candidateCount = 3;

    [Tooltip("특성 최소 개수")]
    [SerializeField] private int minTraitCount = 1;

    [Tooltip("특성 최대 개수")]
    [SerializeField] private int maxTraitCount = 3;

    [Header("=== 이름 풀 ===")]
    [SerializeField]
    private List<string> workerNames = new List<string>
    {
        "철수", "영희", "민수", "지영", "준호", "수진", "동현", "미영",
        "성민", "은지", "재현", "소연", "태우", "하나", "승현", "유리"
    };

    [SerializeField]
    private List<string> fighterNames = new List<string>
    {
        "용사", "무사", "검객", "파이터", "전사", "수호자", "돌격병", "척후병"
    };

    // 현재 고용 가능한 유닛들
    private List<RecruitableUnit> currentCandidates = new List<RecruitableUnit>();

    // 이벤트
    public event Action<List<RecruitableUnit>> OnCandidatesGenerated;
    public event Action<Unit> OnUnitRecruited;

    // Properties
    public List<RecruitableUnit> CurrentCandidates => currentCandidates;
    public bool HasCandidates => currentCandidates.Count > 0;

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
        // CycleManager 이벤트 구독
        if (CycleManager.Instance != null)
        {
            CycleManager.Instance.OnCycleComplete += OnCycleComplete;
            Debug.Log("[UnitRecruitManager] CycleManager 이벤트 구독 완료");
        }
        else
        {
            Debug.LogWarning("[UnitRecruitManager] CycleManager를 찾을 수 없습니다!");
        }
    }

    private void OnDestroy()
    {
        if (CycleManager.Instance != null)
        {
            CycleManager.Instance.OnCycleComplete -= OnCycleComplete;
        }
    }

    /// <summary>
    /// 주기 완료 시 호출 (밤 → 낮 전환 시)
    /// </summary>
    private void OnCycleComplete(CycleEventData data)
    {
        Debug.Log($"<color=yellow>[UnitRecruitManager] 주기 {data.CycleCount} 완료! 고용 후보 생성</color>");
        GenerateCandidates();
    }

    /// <summary>
    /// 고용 후보 유닛들 생성
    /// </summary>
    public void GenerateCandidates()
    {
        currentCandidates.Clear();

        for (int i = 0; i < candidateCount; i++)
        {
            RecruitableUnit candidate = GenerateRandomUnit();
            currentCandidates.Add(candidate);
        }

        Debug.Log($"[UnitRecruitManager] {currentCandidates.Count}명의 고용 후보 생성됨");

        OnCandidatesGenerated?.Invoke(currentCandidates);
    }

    /// <summary>
    /// 랜덤 유닛 생성
    /// </summary>
    private RecruitableUnit GenerateRandomUnit()
    {
        RecruitableUnit unit = new RecruitableUnit();

        // 유닛 타입 랜덤 (70% 워커, 30% 파이터)
        unit.Type = UnityEngine.Random.value < 0.7f ? UnitType.Worker : UnitType.Fighter;

        // 이름 랜덤
        if (unit.Type == UnitType.Worker)
        {
            unit.UnitName = workerNames[UnityEngine.Random.Range(0, workerNames.Count)];
            unit.Icon = defaultWorkerIcon;
        }
        else
        {
            unit.UnitName = fighterNames[UnityEngine.Random.Range(0, fighterNames.Count)];
            unit.Icon = defaultFighterIcon;
        }

        // 특성 랜덤 (1~3개)
        int traitCount = UnityEngine.Random.Range(minTraitCount, maxTraitCount + 1);
        List<UnitTraitSO> availableTraits = new List<UnitTraitSO>(allTraits);

        for (int i = 0; i < traitCount && availableTraits.Count > 0; i++)
        {
            int index = UnityEngine.Random.Range(0, availableTraits.Count);
            UnitTraitSO trait = availableTraits[index];

            // 유닛 타입 제한 체크
            if (trait.HasUnitTypeRestriction && trait.RequiredUnitType != unit.Type)
            {
                availableTraits.RemoveAt(index);
                i--;
                continue;
            }

            // 비호환 특성 체크
            bool compatible = true;
            foreach (var existingTrait in unit.Traits)
            {
                if (trait.IncompatibleTraits != null)
                {
                    foreach (var incompatible in trait.IncompatibleTraits)
                    {
                        if (existingTrait.Type == incompatible)
                        {
                            compatible = false;
                            break;
                        }
                    }
                }
                if (!compatible) break;
            }

            if (compatible)
            {
                unit.Traits.Add(trait);
            }

            availableTraits.RemoveAt(index);
        }

        // 비용 계산 (특성 개수에 따라 증가)
        int costMultiplier = 1 + unit.Traits.Count;

        // 긍정 특성 개수에 따라 비용 추가
        int positiveTraitCount = 0;
        foreach (var trait in unit.Traits)
        {
            if (trait.IsPositive) positiveTraitCount++;
        }

        if (coinResource != null)
        {
            unit.Costs.Add(new RecruitCost(coinResource, baseCoinCost + (positiveTraitCount * 2)));
        }

        if (foodResource != null)
        {
            unit.Costs.Add(new RecruitCost(foodResource, baseFoodCost + positiveTraitCount));
        }

        return unit;
    }

    /// <summary>
    /// 유닛 고용
    /// </summary>
    public bool RecruitUnit(RecruitableUnit candidate)
    {
        if (candidate == null)
        {
            Debug.LogWarning("[UnitRecruitManager] 후보가 null입니다!");
            return false;
        }

        // 비용 지불 가능 확인
        if (!candidate.CanAfford())
        {
            Debug.LogWarning($"[UnitRecruitManager] {candidate.UnitName} 고용 비용 부족!");
            return false;
        }

        // 비용 지불
        if (!candidate.PayCost())
        {
            Debug.LogWarning($"[UnitRecruitManager] {candidate.UnitName} 비용 지불 실패!");
            return false;
        }

        // 유닛 생성
        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : Vector3.zero;

        Unit newUnit = null;
        if (UnitManager.Instance != null)
        {
            // 방법 1: UnitManager에 오버로드가 있는 경우
            // newUnit = UnitManager.Instance.CreateUnit(spawnPos, candidate.Type, candidate.UnitName, candidate.Traits);

            // 방법 2: 확장 메서드 사용 (UnitManagerExtension.cs 필요)
            newUnit = UnitManager.Instance.CreateUnitWithTraits(
                spawnPos,
                candidate.Type,
                candidate.UnitName,
                candidate.Traits
            );
        }

        if (newUnit != null)
        {
            Debug.Log($"<color=green>[UnitRecruitManager] {candidate.UnitName} 고용 성공!</color>");

            // 후보 목록에서 제거
            currentCandidates.Remove(candidate);

            OnUnitRecruited?.Invoke(newUnit);
            return true;
        }

        Debug.LogError($"[UnitRecruitManager] {candidate.UnitName} 유닛 생성 실패!");
        return false;
    }

    /// <summary>
    /// 고용 건너뛰기 (모든 후보 제거)
    /// </summary>
    public void SkipRecruiting()
    {
        currentCandidates.Clear();
        Debug.Log("[UnitRecruitManager] 고용 건너뛰기");
    }

    /// <summary>
    /// 테스트용: 수동으로 후보 생성
    /// </summary>
    [ContextMenu("Generate Test Candidates")]
    public void GenerateTestCandidates()
    {
        GenerateCandidates();
    }
}