using UnityEngine;

/// <summary>
/// 자원 카테고리
/// </summary>
public enum ResourceCategory
{
    RawMaterial,    // 원자재 (나무, 돌)
    Processed,      // 가공품 (판자, 벽돌)
    Food,           // 음식
    Tool,           // 도구
    Special         // 특수 아이템
}

/// <summary>
/// 개별 자원 아이템 정의 (나무, 돌, 음식 등)
/// </summary>
[CreateAssetMenu(fileName = "New Resource", menuName = "Game/Resource Item")]
public class ResourceItemSO : ScriptableObject
{
    [Header("Basic Info")]
    [field: SerializeField] public string ResourceName { get; private set; }
    [field: SerializeField] public int ID { get; private set; }
    [field: SerializeField] public ResourceCategory Category { get; private set; } = ResourceCategory.RawMaterial;

    [field: SerializeField, TextArea]
    public string Description { get; private set; }

    [Header("Visuals")]
    [field: SerializeField] public Sprite Icon { get; private set; }
    [field: SerializeField] public GameObject DropPrefab { get; private set; }

    [Header("Stacking")]
    [field: SerializeField] public int MaxStackSize { get; private set; } = 99;

    [Header("Value (Optional)")]
    [field: SerializeField] public int BaseValue { get; private set; } = 1;  // 기본 가치 (거래용)

    [Header("Food Properties (if Category == Food)")]
    [field: SerializeField] public float NutritionValue { get; private set; } = 0f;  // 배고픔 회복량
    [field: SerializeField] public float HealthRestore { get; private set; } = 0f;   // 체력 회복량

    /// <summary>
    /// 음식인지 확인
    /// </summary>
    public bool IsFood => Category == ResourceCategory.Food && NutritionValue > 0;
}