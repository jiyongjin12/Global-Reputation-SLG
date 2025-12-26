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
}