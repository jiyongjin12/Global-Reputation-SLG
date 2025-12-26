using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자원 노드 타입
/// </summary>
public enum ResourceNodeType
{
    Tree,       // 나무
    Rock,       // 돌
    Bush,       // 덤불/풀
    Ore,        // 광석
    Plant       // 식물 (채집)
}

/// <summary>
/// 자원 노드 데이터베이스
/// </summary>
[CreateAssetMenu(fileName = "ResourceNodeDatabase", menuName = "Game/Resource Node Database")]
public class ResourceNodeDatabaseSO : ScriptableObject
{
    public List<ResourceNodeData> resourceNodes;

    public ResourceNodeData GetNodeByID(int id)
    {
        return resourceNodes.Find(n => n.ID == id);
    }

    public List<ResourceNodeData> GetNodesByType(ResourceNodeType type)
    {
        return resourceNodes.FindAll(n => n.NodeType == type);
    }
}

/// <summary>
/// 개별 자원 노드 데이터
/// </summary>
[Serializable]
public class ResourceNodeData
{
    [field: SerializeField] public string Name { get; private set; }
    [field: SerializeField] public int ID { get; private set; }
    [field: SerializeField] public ResourceNodeType NodeType { get; private set; }
    [field: SerializeField] public GameObject Prefab { get; private set; }
    [field: SerializeField] public Vector2Int Size { get; private set; } = Vector2Int.one;

    [Header("Stats")]
    [field: SerializeField] public float MaxHP { get; private set; } = 100f;
    [field: SerializeField] public float HarvestWorkPerHit { get; private set; } = 10f; // 한 번 칠 때 깎이는 양

    [Header("Drops")]
    [field: SerializeField] public ResourceDrop[] Drops { get; private set; }

    [Header("Respawn")]
    [field: SerializeField] public bool CanRespawn { get; private set; } = false;
    [field: SerializeField] public float RespawnTime { get; private set; } = 60f;
}