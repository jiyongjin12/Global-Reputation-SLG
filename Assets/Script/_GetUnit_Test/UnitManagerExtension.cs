using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UnitManager 확장 - 특성을 포함한 유닛 생성
/// 
/// ★ 사용법:
/// 기존 UnitManager.cs의 CreateUnit 메서드에 아래 오버로드를 추가하거나,
/// 이 partial class를 사용하세요.
/// </summary>
public static class UnitManagerExtension
{
    /// <summary>
    /// 특성을 포함한 유닛 생성 (확장 메서드)
    /// </summary>
    public static Unit CreateUnitWithTraits(
        this UnitManager manager,
        Vector3 position,
        UnitType type,
        string name,
        List<UnitTraitSO> traits)
    {
        // 기본 유닛 생성
        Unit unit = manager.CreateUnit(position, type, name);

        if (unit == null) return null;

        // 특성 적용
        if (traits != null && traits.Count > 0)
        {
            foreach (var trait in traits)
            {
                if (trait != null)
                {
                    unit.AddTrait(trait);
                }
            }
        }

        return unit;
    }
}

/*
=== UnitManager.cs에 직접 추가할 경우 아래 메서드를 복사하세요 ===

/// <summary>
/// 특성을 포함한 유닛 생성
/// </summary>
public Unit CreateUnit(Vector3 position, UnitType type, string name, List<UnitTraitSO> traits)
{
    // 기본 유닛 생성
    Unit unit = CreateUnit(position, type, name);
    
    if (unit == null) return null;
    
    // 특성 적용
    if (traits != null && traits.Count > 0)
    {
        foreach (var trait in traits)
        {
            if (trait != null && unit.CanAddTrait(trait))
            {
                unit.AddTrait(trait);
            }
        }
    }
    
    Debug.Log($"[UnitManager] {name} 생성 완료 (특성 {traits?.Count ?? 0}개)");
    
    return unit;
}

=== Unit.cs에 AddTrait 메서드가 없다면 추가하세요 ===

/// <summary>
/// 특성 추가
/// </summary>
public void AddTrait(UnitTraitSO trait)
{
    if (trait == null) return;
    
    // 중복 체크
    if (traits.Exists(t => t.Type == trait.Type)) return;
    
    traits.Add(trait);
    ApplyTraits();  // 특성 효과 적용
    
    Debug.Log($"[Unit] {unitName}에 특성 추가: {trait.TraitName}");
}

/// <summary>
/// 특성 추가 가능 여부
/// </summary>
public bool CanAddTrait(UnitTraitSO trait)
{
    if (trait == null) return false;
    return trait.CanApplyTo(this);
}

/// <summary>
/// 특정 특성 보유 여부
/// </summary>
public bool HasTrait(TraitType type)
{
    return traits.Exists(t => t.Type == type);
}

*/