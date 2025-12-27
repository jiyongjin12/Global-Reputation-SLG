using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 자원 아이템 데이터베이스
/// ResourceManager에서 ID로 ResourceItemSO를 찾을 때 사용
/// </summary>
[CreateAssetMenu(fileName = "ResourceItemDatabase", menuName = "Game/Resource Item Database")]
public class ResourceItemDatabaseSO : ScriptableObject
{
    [SerializeField] private List<ResourceItemSO> allItems = new List<ResourceItemSO>();

    // 빠른 검색을 위한 캐시
    private Dictionary<int, ResourceItemSO> itemCache;

    /// <summary>
    /// 모든 아이템 목록
    /// </summary>
    public List<ResourceItemSO> AllItems => allItems;

    /// <summary>
    /// ID로 아이템 찾기
    /// </summary>
    public ResourceItemSO GetItemByID(int id)
    {
        // 캐시 초기화
        if (itemCache == null)
        {
            BuildCache();
        }

        return itemCache.TryGetValue(id, out var item) ? item : null;
    }

    /// <summary>
    /// 이름으로 아이템 찾기
    /// </summary>
    public ResourceItemSO GetItemByName(string name)
    {
        return allItems.Find(item => item.ResourceName == name);
    }

    /// <summary>
    /// 카테고리별 아이템 목록
    /// </summary>
    public List<ResourceItemSO> GetItemsByCategory(ResourceCategory category)
    {
        return allItems.FindAll(item => item.Category == category);
    }

    /// <summary>
    /// 음식 아이템만 가져오기
    /// </summary>
    public List<ResourceItemSO> GetFoodItems()
    {
        return allItems.FindAll(item => item.IsFood);
    }

    /// <summary>
    /// 캐시 구축
    /// </summary>
    private void BuildCache()
    {
        itemCache = new Dictionary<int, ResourceItemSO>();
        foreach (var item in allItems)
        {
            if (item != null && !itemCache.ContainsKey(item.ID))
            {
                itemCache[item.ID] = item;
            }
        }
    }

    /// <summary>
    /// 에디터에서 데이터 변경 시 캐시 무효화
    /// </summary>
    private void OnValidate()
    {
        itemCache = null;
    }
}