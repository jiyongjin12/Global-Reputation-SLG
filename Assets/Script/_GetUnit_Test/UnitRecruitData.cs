using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 고용 가능한 유닛 후보 데이터
/// </summary>
[System.Serializable]
public class RecruitableUnit
{
    public string UnitName;
    public UnitType Type;
    public Sprite Icon;
    public List<UnitTraitSO> Traits = new List<UnitTraitSO>();

    // 고용 비용 (재료 ID, 수량)
    public List<RecruitCost> Costs = new List<RecruitCost>();

    /// <summary>
    /// 고용 비용을 지불할 수 있는지 확인
    /// </summary>
    public bool CanAfford()
    {
        if (ResourceManager.Instance == null) return false;

        foreach (var cost in Costs)
        {
            if (!ResourceManager.Instance.HasEnoughResource(cost.Resource.ID, cost.Amount))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 고용 비용 지불
    /// </summary>
    public bool PayCost()
    {
        if (!CanAfford()) return false;

        foreach (var cost in Costs)
        {
            ResourceManager.Instance.UseResource(cost.Resource.ID, cost.Amount);
        }
        return true;
    }
}

/// <summary>
/// 고용 비용
/// </summary>
[System.Serializable]
public class RecruitCost
{
    public ResourceItemSO Resource;
    public int Amount;

    public RecruitCost(ResourceItemSO resource, int amount)
    {
        Resource = resource;
        Amount = amount;
    }
}