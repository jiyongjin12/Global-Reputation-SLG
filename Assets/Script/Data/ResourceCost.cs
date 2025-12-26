using UnityEngine;
using System;

/// <summary>
/// 자원 비용 (건물 건설, 유닛 고용 등에 사용)
/// </summary>
[Serializable]
public class ResourceCost
{
    [field: SerializeField] public ResourceItemSO Resource { get; private set; }
    [field: SerializeField] public int Amount { get; private set; }
}

/// <summary>
/// 드롭 보상 정의
/// </summary>
[Serializable]
public class ResourceDrop
{
    [field: SerializeField] public ResourceItemSO Resource { get; private set; }
    [field: SerializeField] public int MinAmount { get; private set; } = 1;
    [field: SerializeField] public int MaxAmount { get; private set; } = 3;

    public int GetRandomAmount()
    {
        return UnityEngine.Random.Range(MinAmount, MaxAmount + 1);
    }
}