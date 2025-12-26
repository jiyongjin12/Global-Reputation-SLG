using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 특성 데이터베이스
/// </summary>
[CreateAssetMenu(fileName = "TraitDatabase", menuName = "Game/Trait Database")]
public class TraitDatabaseSO : ScriptableObject
{
    public List<UnitTraitSO> allTraits;

    public UnitTraitSO GetTraitByType(TraitType type)
    {
        return allTraits.Find(t => t.Type == type);
    }

    public List<UnitTraitSO> GetPositiveTraits()
    {
        return allTraits.FindAll(t => t.IsPositive);
    }

    public List<UnitTraitSO> GetNegativeTraits()
    {
        return allTraits.FindAll(t => !t.IsPositive);
    }

    /// <summary>
    /// 랜덤 특성 부여 (새 유닛 생성 시)
    /// </summary>
    public List<UnitTraitSO> GetRandomTraits(int positiveCount = 1, int negativeCount = 0)
    {
        List<UnitTraitSO> result = new List<UnitTraitSO>();

        var positives = GetPositiveTraits();
        var negatives = GetNegativeTraits();

        // 긍정적 특성 선택
        for (int i = 0; i < positiveCount && positives.Count > 0; i++)
        {
            int index = Random.Range(0, positives.Count);
            result.Add(positives[index]);
            positives.RemoveAt(index);
        }

        // 부정적 특성 선택
        for (int i = 0; i < negativeCount && negatives.Count > 0; i++)
        {
            int index = Random.Range(0, negatives.Count);
            result.Add(negatives[index]);
            negatives.RemoveAt(index);
        }

        return result;
    }
}