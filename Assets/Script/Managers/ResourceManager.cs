using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 저장된 자원 데이터 (UI 표시용)
/// </summary>
[Serializable]
public class StoredResource
{
    public ResourceItemSO Item;
    public int Amount;

    public StoredResource(ResourceItemSO item, int amount)
    {
        Item = item;
        Amount = amount;
    }
}

/// <summary>
/// 전역 자원 관리 시스템
/// 플레이어(창고)가 보유한 자원을 관리
/// </summary>
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("Database")]
    [SerializeField] private ResourceItemDatabaseSO itemDatabase;

    [Header("Initial Resources (Optional)")]
    [SerializeField] private List<ResourceCost> startingResources = new List<ResourceCost>();

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // 자원 저장소 (ResourceID -> StoredResource)
    private Dictionary<int, StoredResource> resources = new Dictionary<int, StoredResource>();

    // ===== 이벤트 =====
    /// <summary>특정 자원 변경 (resourceID, newAmount)</summary>
    public event Action<int, int> OnResourceChanged;

    /// <summary>자원이 새로 추가됨 (처음 획득)</summary>
    public event Action<ResourceItemSO> OnNewResourceDiscovered;

    /// <summary>전체 인벤토리 변경 (UI 전체 새로고침용)</summary>
    public event Action OnInventoryChanged;

    // ===== Properties =====
    public ResourceItemDatabaseSO ItemDatabase => itemDatabase;
    public int UniqueResourceCount => resources.Count;
    public int TotalItemCount => resources.Values.Sum(r => r.Amount);

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
        // 초기 자원 지급
        foreach (var startRes in startingResources)
        {
            if (startRes.Resource != null)
            {
                AddResource(startRes.Resource, startRes.Amount);
            }
        }
    }

    // ===== 자원 추가 =====

    /// <summary>
    /// 자원 추가 (ResourceItemSO 사용)
    /// </summary>
    public void AddResource(ResourceItemSO resource, int amount)
    {
        if (resource == null || amount <= 0) return;

        bool isNew = !resources.ContainsKey(resource.ID);

        if (isNew)
        {
            resources[resource.ID] = new StoredResource(resource, amount);
            OnNewResourceDiscovered?.Invoke(resource);
        }
        else
        {
            resources[resource.ID].Amount += amount;
        }

        // 이벤트 발생
        OnResourceChanged?.Invoke(resource.ID, resources[resource.ID].Amount);
        OnInventoryChanged?.Invoke();

        if (showDebugLogs)
        {
            Debug.Log($"[ResourceManager] +{amount} {resource.ResourceName} (총: {resources[resource.ID].Amount})");
        }
    }

    /// <summary>
    /// 자원 추가 (ID 사용 - 데이터베이스에서 찾음)
    /// </summary>
    public void AddResource(int resourceID, int amount)
    {
        // 이미 저장되어 있으면 그 정보 사용
        if (resources.TryGetValue(resourceID, out var stored))
        {
            AddResource(stored.Item, amount);
            return;
        }

        // 데이터베이스에서 찾기
        if (itemDatabase != null)
        {
            var item = itemDatabase.GetItemByID(resourceID);
            if (item != null)
            {
                AddResource(item, amount);
                return;
            }
        }

        Debug.LogWarning($"[ResourceManager] ID {resourceID}에 해당하는 아이템을 찾을 수 없습니다!");
    }

    // ===== 자원 사용 =====

    /// <summary>
    /// 자원 사용 (성공 시 true)
    /// </summary>
    public bool UseResource(ResourceItemSO resource, int amount)
    {
        if (resource == null) return false;
        return UseResource(resource.ID, amount);
    }

    /// <summary>
    /// 자원 사용 (ID 사용)
    /// </summary>
    public bool UseResource(int resourceID, int amount)
    {
        if (!HasEnoughResource(resourceID, amount))
            return false;

        resources[resourceID].Amount -= amount;

        // 0개가 되어도 목록에서 제거하지 않음 (발견 기록 유지)
        // 제거하고 싶으면 아래 주석 해제
        // if (resources[resourceID].Amount <= 0)
        //     resources.Remove(resourceID);

        OnResourceChanged?.Invoke(resourceID, resources[resourceID].Amount);
        OnInventoryChanged?.Invoke();

        if (showDebugLogs)
        {
            var item = resources[resourceID].Item;
            Debug.Log($"[ResourceManager] -{amount} {item.ResourceName} (남은: {resources[resourceID].Amount})");
        }

        return true;
    }

    // ===== 조회 =====

    /// <summary>
    /// 자원 보유량 확인
    /// </summary>
    public int GetResourceAmount(int resourceID)
    {
        return resources.TryGetValue(resourceID, out var stored) ? stored.Amount : 0;
    }

    public int GetResourceAmount(ResourceItemSO resource)
    {
        return resource != null ? GetResourceAmount(resource.ID) : 0;
    }

    /// <summary>
    /// 충분한 자원이 있는지 확인
    /// </summary>
    public bool HasEnoughResource(int resourceID, int amount)
    {
        return GetResourceAmount(resourceID) >= amount;
    }

    public bool HasEnoughResource(ResourceItemSO resource, int amount)
    {
        return resource != null && HasEnoughResource(resource.ID, amount);
    }

    /// <summary>
    /// 자원 보유 여부 (1개 이상)
    /// </summary>
    public bool HasResource(int resourceID)
    {
        return GetResourceAmount(resourceID) > 0;
    }

    /// <summary>
    /// StoredResource 정보 가져오기 (UI용)
    /// </summary>
    public StoredResource GetStoredResource(int resourceID)
    {
        return resources.TryGetValue(resourceID, out var stored) ? stored : null;
    }

    // ===== 인벤토리 UI용 =====

    /// <summary>
    /// 모든 보유 자원 목록 (0개 포함)
    /// </summary>
    public List<StoredResource> GetAllResources()
    {
        return resources.Values.ToList();
    }

    /// <summary>
    /// 1개 이상 보유한 자원만
    /// </summary>
    public List<StoredResource> GetOwnedResources()
    {
        return resources.Values.Where(r => r.Amount > 0).ToList();
    }

    /// <summary>
    /// 카테고리별 자원 목록
    /// </summary>
    public List<StoredResource> GetResourcesByCategory(ResourceCategory category)
    {
        return resources.Values
            .Where(r => r.Item.Category == category && r.Amount > 0)
            .ToList();
    }

    /// <summary>
    /// 음식 목록
    /// </summary>
    public List<StoredResource> GetFoodResources()
    {
        return resources.Values
            .Where(r => r.Item.IsFood && r.Amount > 0)
            .ToList();
    }

    /// <summary>
    /// 이름순 정렬
    /// </summary>
    public List<StoredResource> GetResourcesSortedByName()
    {
        return resources.Values
            .Where(r => r.Amount > 0)
            .OrderBy(r => r.Item.ResourceName)
            .ToList();
    }

    /// <summary>
    /// 수량순 정렬 (많은 순)
    /// </summary>
    public List<StoredResource> GetResourcesSortedByAmount()
    {
        return resources.Values
            .Where(r => r.Amount > 0)
            .OrderByDescending(r => r.Amount)
            .ToList();
    }

    /// <summary>
    /// 카테고리 → 이름순 정렬
    /// </summary>
    public List<StoredResource> GetResourcesSortedByCategoryThenName()
    {
        return resources.Values
            .Where(r => r.Amount > 0)
            .OrderBy(r => r.Item.Category)
            .ThenBy(r => r.Item.ResourceName)
            .ToList();
    }

    // ===== 비용 관련 =====

    /// <summary>
    /// 여러 자원 비용을 지불할 수 있는지 확인
    /// </summary>
    public bool CanAfford(ResourceCost[] costs)
    {
        if (costs == null) return true;

        foreach (var cost in costs)
        {
            if (cost.Resource == null) continue;
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
            if (cost.Resource != null)
            {
                UseResource(cost.Resource.ID, cost.Amount);
            }
        }
        return true;
    }

    /// <summary>
    /// 부족한 자원 목록 반환 (UI에서 빨간색 표시용)
    /// </summary>
    public List<(ResourceItemSO item, int required, int has)> GetMissingResources(ResourceCost[] costs)
    {
        var missing = new List<(ResourceItemSO, int, int)>();

        if (costs == null) return missing;

        foreach (var cost in costs)
        {
            if (cost.Resource == null) continue;

            int has = GetResourceAmount(cost.Resource);
            if (has < cost.Amount)
            {
                missing.Add((cost.Resource, cost.Amount, has));
            }
        }

        return missing;
    }

    // ===== 유틸리티 =====

    /// <summary>
    /// 모든 자원 클리어 (테스트/리셋용)
    /// </summary>
    public void ClearAllResources()
    {
        resources.Clear();
        OnInventoryChanged?.Invoke();

        if (showDebugLogs)
        {
            Debug.Log("[ResourceManager] 모든 자원 초기화됨");
        }
    }

    /// <summary>
    /// 자원 직접 설정 (세이브 로드용)
    /// </summary>
    public void SetResource(ResourceItemSO resource, int amount)
    {
        if (resource == null) return;

        if (!resources.ContainsKey(resource.ID))
        {
            resources[resource.ID] = new StoredResource(resource, amount);
        }
        else
        {
            resources[resource.ID].Amount = amount;
        }

        OnResourceChanged?.Invoke(resource.ID, amount);
        OnInventoryChanged?.Invoke();
    }

    // ===== 디버그 =====

    /// <summary>
    /// 콘솔에 현재 보유 자원 출력
    /// </summary>
    [ContextMenu("Print All Resources")]
    public void DebugPrintResources()
    {
        Debug.Log("===== [ResourceManager] 보유 자원 =====");
        foreach (var stored in GetResourcesSortedByCategoryThenName())
        {
            Debug.Log($"  [{stored.Item.Category}] {stored.Item.ResourceName}: {stored.Amount}개");
        }
        Debug.Log($"총 {UniqueResourceCount}종류, {TotalItemCount}개");
    }

    /// <summary>
    /// 테스트용: 각 자원 10개씩 추가
    /// </summary>
    [ContextMenu("Add 10 of Each Resource (Debug)")]
    public void DebugAdd10OfEach()
    {
        if (itemDatabase == null)
        {
            Debug.LogWarning("ItemDatabase가 설정되지 않았습니다!");
            return;
        }

        foreach (var item in itemDatabase.AllItems)
        {
            AddResource(item, 10);
        }
    }
}