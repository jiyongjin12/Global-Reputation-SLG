using UnityEngine;
using TMPro;

/// <summary>
/// 카테고리 Box (재화, 음식 등)
/// - TextBox: 카테고리 이름
/// - ItemBox: 아이템 슬롯들 (GridLayoutGroup)
/// </summary>
public class InventoryCategoryBox : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI categoryNameText;   // 카테고리 이름 텍스트
    [SerializeField] private Transform itemContainer;            // ItemBox (GridLayoutGroup)

    private ResourceCategory category;

    public Transform ItemContainer => itemContainer;
    public ResourceCategory Category => category;

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(ResourceCategory cat)
    {
        category = cat;

        // 카테고리 이름 설정
        if (categoryNameText != null)
        {
            categoryNameText.text = GetCategoryDisplayName(cat);
        }

        gameObject.name = $"CategoryBox_{cat}";
    }

    /// <summary>
    /// 카테고리 표시 이름 (한글)
    /// </summary>
    private string GetCategoryDisplayName(ResourceCategory cat)
    {
        switch (cat)
        {
            case ResourceCategory.Currency:
                return "재화";
            case ResourceCategory.Food:
                return "음식";
            case ResourceCategory.Material:
                return "재료";
            case ResourceCategory.Equipment:
                return "장비";
            case ResourceCategory.Seed:
                return "씨앗";
            case ResourceCategory.Special:
                return "특수";
            default:
                return cat.ToString();
        }
    }
}