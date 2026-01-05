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
    Decoration      // 장식
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
        result.Sort(); // enum 순서대로 정렬
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

    // ★ 새로 추가: 카테고리
    [field: SerializeField]
    public BuildingCategory Category { get; private set; } = BuildingCategory.General;

    // ★ 새로 추가: UI용 아이콘
    [field: SerializeField]
    public Sprite Icon { get; private set; }

    // ★ 새로 추가: 설명
    [field: SerializeField, TextArea(2, 4)]
    public string Description { get; private set; }

    [Header("Prefabs")]
    [field: SerializeField]
    public GameObject Prefab { get; private set; }  // 완성된 건물 (기존 호환)

    [field: SerializeField]
    public GameObject BlueprintPrefab { get; private set; }  // 건설 예정 상태 (반투명)

    [Header("Construction")]
    [field: SerializeField]
    public float ConstructionWorkRequired { get; private set; } = 10f;

    [field: SerializeField]
    public ResourceCost[] ConstructionCosts { get; private set; }

    [Header("Functionality")]
    [field: SerializeField]
    public int MaxUnitCapacity { get; private set; } = 0;

    // ★ 새로 추가: UI에 표시할지 여부 (Floor 등 숨길 건물용)
    [field: SerializeField]
    public bool ShowInBuildMenu { get; private set; } = true;
}