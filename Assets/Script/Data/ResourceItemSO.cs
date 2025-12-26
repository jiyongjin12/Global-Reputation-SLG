using UnityEngine;

/// <summary>
/// 개별 자원 아이템 정의 (나무, 돌, 음식 등)
/// </summary>
[CreateAssetMenu(fileName = "New Resource", menuName = "Game/Resource Item")]
public class ResourceItemSO : ScriptableObject
{
    [field: SerializeField] public string ResourceName { get; private set; }
    [field: SerializeField] public int ID { get; private set; }
    [field: SerializeField] public Sprite Icon { get; private set; }
    [field: SerializeField] public GameObject DropPrefab { get; private set; } // 바닥에 떨어졌을 때 프리팹

    [field: SerializeField, TextArea]
    public string Description { get; private set; }
}