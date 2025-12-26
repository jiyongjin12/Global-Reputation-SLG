using System;
using UnityEngine;

/// <summary>
/// 특성 타입
/// </summary>
public enum TraitType
{
    // 긍정적 특성
    Lumberjack,     // 나무꾼 - 나무 채집 효율 2배
    Miner,          // 광부 - 광석 채집 효율 2배
    Builder,        // 건축가 - 건설 속도 2배
    Gatherer,       // 수집가 - 식물 채집 효율 2배
    Sprinter,       // 단거리 주자 - 이동 속도 증가
    Hardworker,     // 일벌레 - 전체 작업 속도 20% 증가

    // 부정적 특성
    Lazy,           // 게으름뱅이 - 작업 속도 30% 감소
    Slow,           // 느림보 - 이동 속도 30% 감소
    Clumsy,         // 서투름 - 가끔 자원 드롭
    Rebellious,     // 반항적 - 충성심 감소 속도 증가

    // 전투 특성
    Warrior,        // 전사 - 공격력 50% 증가
    Hunter,         // 사냥꾼 - 동물에게 데미지 2배
    Defender        // 수호자 - 받는 데미지 30% 감소
}

/// <summary>
/// 특성 효과 정의
/// </summary>
[Serializable]
public class TraitEffect
{
    [field: SerializeField] public UnitStatType AffectedStat { get; private set; }
    [field: SerializeField] public float Multiplier { get; private set; } = 1f;

    [Header("Conditional (Optional)")]
    [field: SerializeField] public bool HasNodeTypeCondition { get; private set; } = false;
    [field: SerializeField] public ResourceNodeType TargetNodeType { get; private set; }
}

/// <summary>
/// 특성 데이터
/// </summary>
[CreateAssetMenu(fileName = "New Trait", menuName = "Game/Unit Trait")]
public class UnitTraitSO : ScriptableObject
{
    [field: SerializeField] public string TraitName { get; private set; }
    [field: SerializeField] public TraitType Type { get; private set; }
    [field: SerializeField, TextArea] public string Description { get; private set; }
    [field: SerializeField] public Sprite Icon { get; private set; }
    [field: SerializeField] public bool IsPositive { get; private set; } = true;

    [Header("Effects")]
    [field: SerializeField] public TraitEffect[] Effects { get; private set; }
}