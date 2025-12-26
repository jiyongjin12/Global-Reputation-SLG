using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 전역 자원 관리 시스템
/// 메인 건물(창고)에 저장된 자원을 관리
/// </summary>
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    // 자원 저장소 (ResourceID -> 수량)
    private Dictionary<int, int> resources = new();

    // 자원 변경 이벤트 (UI 업데이트용)
    public event Action<int, int> OnResourceChanged; // (resourceID, newAmount)

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
    /// 자원 추가
    /// </summary>
    public void AddResource(ResourceItemSO resource, int amount)
    {
        AddResource(resource.ID, amount);
    }

    public void AddResource(int resourceID, int amount)
    {
        if (!resources.ContainsKey(resourceID))
            resources[resourceID] = 0;

        resources[resourceID] += amount;
        OnResourceChanged?.Invoke(resourceID, resources[resourceID]);

        Debug.Log($"[ResourceManager] Added {amount} of resource {resourceID}. Total: {resources[resourceID]}");
    }

    /// <summary>
    /// 자원 사용 (성공 시 true)
    /// </summary>
    public bool UseResource(ResourceItemSO resource, int amount)
    {
        return UseResource(resource.ID, amount);
    }

    public bool UseResource(int resourceID, int amount)
    {
        if (!HasEnoughResource(resourceID, amount))
            return false;

        resources[resourceID] -= amount;
        OnResourceChanged?.Invoke(resourceID, resources[resourceID]);
        return true;
    }

    /// <summary>
    /// 자원 보유량 확인
    /// </summary>
    public int GetResourceAmount(int resourceID)
    {
        return resources.TryGetValue(resourceID, out int amount) ? amount : 0;
    }

    public int GetResourceAmount(ResourceItemSO resource)
    {
        return GetResourceAmount(resource.ID);
    }

    /// <summary>
    /// 충분한 자원이 있는지 확인
    /// </summary>
    public bool HasEnoughResource(int resourceID, int amount)
    {
        return GetResourceAmount(resourceID) >= amount;
    }

    /// <summary>
    /// 여러 자원 비용을 지불할 수 있는지 확인
    /// </summary>
    public bool CanAfford(ResourceCost[] costs)
    {
        foreach (var cost in costs)
        {
            if (!HasEnoughResource(cost.Resource.ID, cost.Amount))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 여러 자원 비용 지불
    /// </summary>
    public bool PayCosts(ResourceCost[] costs)
    {
        if (!CanAfford(costs))
            return false;

        foreach (var cost in costs)
        {
            UseResource(cost.Resource.ID, cost.Amount);
        }
        return true;
    }
}