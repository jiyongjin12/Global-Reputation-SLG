using System;
using UnityEngine;

/// <summary>
/// 특성 타입
/// </summary>
public enum TraitType
{
    // ========== 채집 특성 ==========
    Lumberjack,     // 나무꾼 - 나무 채집 효율 2배
    Miner,          // 광부 - 광석 채집 효율 2배
    Gatherer,       // 수집가 - 식물 채집 효율 2배
    Fisher,         // 어부 - 물고기 채집 효율 2배

    // ========== 작업 특성 ==========
    Builder,        // 건축가 - 건설 속도 2배
    Hardworker,     // 일벌레 - 전체 작업 속도 20% 증가
    Craftsman,      // 장인 - 제작 속도 50% 증가

    // ========== 이동 특성 ==========
    Sprinter,       // 단거리 주자 - 이동 속도 30% 증가
    Slow,           // 느림보 - 이동 속도 30% 감소

    // ========== 전투 특성 ==========
    Warrior,        // 전사 - 공격력 50% 증가
    Hunter,         // 사냥꾼 - 동물에게 데미지 2배
    Defender,       // 수호자 - 받는 데미지 30% 감소
    Berserker,      // 광전사 - 공격력 100% 증가, 받는 데미지 50% 증가

    // ========== 부정적 특성 ==========
    Lazy,           // 게으름뱅이 - 작업 속도 30% 감소
    Clumsy,         // 서투름 - 가끔 자원 드롭
    Rebellious,     // 반항적 - 충성심 감소 속도 증가
    Coward,         // 겁쟁이 - 전투 시 도망 확률 증가

    // ========== 특수 특성 ==========
    Lucky,          // 행운 - 드롭률 증가
    Tough,          // 강인함 - 최대 HP 30% 증가
    Hungry,         // 대식가 - 배고픔 감소 50% 빠름
    Efficient       // 효율적 - 자원 소모 20% 감소
}

/// <summary>
/// 특성 효과 정의
/// </summary>
[Serializable]
public class TraitEffect
{
    [Tooltip("영향받는 스탯")]
    [field: SerializeField] public UnitStatType AffectedStat { get; private set; }

    [Tooltip("배율 (1.0 = 100%, 1.5 = 150%, 0.7 = 70%)")]
    [field: SerializeField] public float Multiplier { get; private set; } = 1f;

    [Header("=== 조건부 효과 (선택) ===")]
    [Tooltip("특정 자원 노드에서만 적용")]
    [field: SerializeField] public bool HasNodeTypeCondition { get; private set; } = false;

    [Tooltip("대상 자원 노드 타입")]
    [field: SerializeField] public ResourceNodeType TargetNodeType { get; private set; }
}

/// <summary>
/// 유닛 특성 ScriptableObject
/// 
/// === Unity Inspector 설정 예시 ===
/// 
/// [나무꾼 특성]
/// - TraitName: "나무꾼"
/// - Type: Lumberjack
/// - Description: "나무 채집 효율이 2배가 됩니다."
/// - IsPositive: true
/// - Effects:
///   [0] AffectedStat: GatherPower, Multiplier: 2.0
///       HasNodeTypeCondition: true, TargetNodeType: Tree
/// 
/// [광부 특성]
/// - TraitName: "광부"
/// - Type: Miner
/// - Effects:
///   [0] AffectedStat: GatherPower, Multiplier: 2.0
///       HasNodeTypeCondition: true, TargetNodeType: Rock
/// 
/// [전사 특성]
/// - TraitName: "전사"
/// - Type: Warrior
/// - Effects:
///   [0] AffectedStat: AttackPower, Multiplier: 1.5
///       HasNodeTypeCondition: false
/// 
/// [일벌레 특성]
/// - TraitName: "일벌레"
/// - Type: Hardworker
/// - Effects:
///   [0] AffectedStat: WorkSpeed, Multiplier: 1.2
///       HasNodeTypeCondition: false
/// 
/// [게으름뱅이 특성]
/// - TraitName: "게으름뱅이"
/// - Type: Lazy
/// - IsPositive: false
/// - Effects:
///   [0] AffectedStat: WorkSpeed, Multiplier: 0.7
///       HasNodeTypeCondition: false
/// </summary>
[CreateAssetMenu(fileName = "New Trait", menuName = "Game/Unit Trait")]
public class UnitTraitSO : ScriptableObject
{
    [Header("=== 기본 정보 ===")]
    [field: SerializeField] public string TraitName { get; private set; }
    [field: SerializeField] public TraitType Type { get; private set; }
    [field: SerializeField, TextArea] public string Description { get; private set; }
    [field: SerializeField] public Sprite Icon { get; private set; }
    [field: SerializeField] public bool IsPositive { get; private set; } = true;

    [Header("=== 희귀도 ===")]
    [field: SerializeField] public TraitRarity Rarity { get; private set; } = TraitRarity.Common;

    [Header("=== 효과 ===")]
    [field: SerializeField] public TraitEffect[] Effects { get; private set; }

    [Header("=== 제한 (선택) ===")]
    [Tooltip("이 특성과 함께 가질 수 없는 특성들")]
    [field: SerializeField] public TraitType[] IncompatibleTraits { get; private set; }

    [Tooltip("특정 유닛 타입에서만 획득 가능")]
    [field: SerializeField] public bool HasUnitTypeRestriction { get; private set; } = false;
    [field: SerializeField] public UnitType RequiredUnitType { get; private set; }

    /// <summary>
    /// 유닛이 이 특성을 가질 수 있는지 확인
    /// </summary>
    public bool CanApplyTo(Unit unit)
    {
        if (unit == null) return false;

        // 유닛 타입 제한 확인
        if (HasUnitTypeRestriction && unit.Type != RequiredUnitType)
            return false;

        // 비호환 특성 확인
        if (IncompatibleTraits != null)
        {
            foreach (var incompatible in IncompatibleTraits)
            {
                if (unit.HasTrait(incompatible))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 특성 효과 요약 문자열
    /// </summary>
    public string GetEffectSummary()
    {
        if (Effects == null || Effects.Length == 0)
            return "효과 없음";

        var summary = new System.Text.StringBuilder();

        foreach (var effect in Effects)
        {
            float percent = (effect.Multiplier - 1f) * 100f;
            string sign = percent >= 0 ? "+" : "";
            string statName = effect.AffectedStat.ToString();

            if (effect.HasNodeTypeCondition)
            {
                summary.AppendLine($"{effect.TargetNodeType} {statName} {sign}{percent:F0}%");
            }
            else
            {
                summary.AppendLine($"{statName} {sign}{percent:F0}%");
            }
        }

        return summary.ToString().TrimEnd();
    }
}

/// <summary>
/// 특성 희귀도
/// </summary>
public enum TraitRarity
{
    Common,     // 흔함 (60%)
    Uncommon,   // 드묾 (25%)
    Rare,       // 희귀 (12%)
    Epic,       // 에픽 (3%)
    Legendary   // 전설 (0.5%)
}