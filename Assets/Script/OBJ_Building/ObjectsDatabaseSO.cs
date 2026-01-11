using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 건물 타입 정의
/// </summary>
public enum BuildingType
{
    Floor,          // 바닥
    Storage,        // 창고 (메인 건물)
    UnitHouse,      // 유닛 고용/거주 건물
    Production,     // 생산 건물
    Decoration,     // 장식

    // ★ 새로 추가
    Farmland,       // 농경지
    Workshop,       // 작업장 (제작)
    Kitchen,        // 주방 (요리)
    Quarry,         // 채석장 (자동 생산)
    LumberMill,     // 벌목장 (자동 생산)
    Mine,           // 광산 (자동 생산)
}

/// <summary>
/// 건물 상태
/// </summary>
public enum BuildingState
{
    Blueprint,          // 건설 예정 (유닛이 와서 지어야 함)
    UnderConstruction,  // 건설 중
    Completed           // 완료
}

// BuildingCategory는 BuildingCategory.cs에 정의되어 있음

[CreateAssetMenu]
public class ObjectsDatabaseSO : ScriptableObject
{
    public List<ObjectData> objectsData;

    public ObjectData GetObjectByID(int id)
    {
        return objectsData.Find(o => o.ID == id);
    }

    /// <summary>
    /// 특정 카테고리의 모든 건물 가져오기
    /// </summary>
    public List<ObjectData> GetObjectsByCategory(BuildingCategory category)
    {
        return objectsData.FindAll(o => o.Category == category);
    }

    /// <summary>
    /// 특정 타입의 모든 건물 가져오기
    /// </summary>
    public List<ObjectData> GetObjectsByType(BuildingType type)
    {
        return objectsData.FindAll(o => o.Type == type);
    }

    /// <summary>
    /// 모든 카테고리 가져오기 (데이터에 존재하는 것만)
    /// </summary>
    public List<BuildingCategory> GetAllCategories()
    {
        HashSet<BuildingCategory> categories = new HashSet<BuildingCategory>();
        foreach (var obj in objectsData)
        {
            categories.Add(obj.Category);
        }

        List<BuildingCategory> result = new List<BuildingCategory>(categories);
        result.Sort();
        return result;
    }
}

[Serializable]
public class ObjectData
{
    [field: SerializeField]
    public string Name { get; private set; }

    [field: SerializeField]
    public int ID { get; private set; }

    [field: SerializeField]
    public Vector2Int Size { get; private set; } = Vector2Int.one;

    [field: SerializeField]
    public BuildingType Type { get; private set; }

    [field: SerializeField]
    public BuildingCategory Category { get; private set; } = BuildingCategory.General;

    [field: SerializeField]
    public Sprite Icon { get; private set; }

    [field: SerializeField, TextArea(2, 4)]
    public string Description { get; private set; }

    [Header("Prefabs")]
    [field: SerializeField]
    public GameObject Prefab { get; private set; }

    [field: SerializeField]
    public GameObject BlueprintPrefab { get; private set; }

    [Header("Construction")]
    [field: SerializeField]
    public float ConstructionWorkRequired { get; private set; } = 10f;

    [field: SerializeField]
    public ResourceCost[] ConstructionCosts { get; private set; }

    [Header("Functionality")]
    [field: SerializeField]
    public int MaxUnitCapacity { get; private set; } = 0;

    [field: SerializeField]
    public bool ShowInBuildMenu { get; private set; } = true;

    // ★ 새로 추가: 워크스테이션용 설정
    [Header("Workstation Settings")]
    [field: SerializeField]
    public WorkTaskType WorkTaskType { get; private set; } = WorkTaskType.None;

    [field: SerializeField]
    public List<RecipeSO> AvailableRecipes { get; private set; }

    [field: SerializeField]
    public ResourceItemSO AutoProducedResource { get; private set; }

    [field: SerializeField]
    public float ProductionInterval { get; private set; } = 10f;
}