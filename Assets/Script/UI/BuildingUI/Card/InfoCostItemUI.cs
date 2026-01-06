using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 정보 패널용 비용 아이템 UI
/// - 필요량 + (보유량) 표시
/// - 예: [아이콘] 10 (72) ← 필요 10개, 보유 72개
/// </summary>
public class InfoCostItemUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image resourceIcon;
    [SerializeField] private TextMeshProUGUI requiredText;    // 필요량
    [SerializeField] private TextMeshProUGUI ownedText;       // (보유량)

    private ResourceCost costData;

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(ResourceCost cost, Color normalColor, Color ownedColor, Color insufficientColor)
    {
        costData = cost;

        if (cost == null || cost.Resource == null)
            return;

        // 아이콘 설정
        if (resourceIcon != null && cost.Resource.Icon != null)
        {
            resourceIcon.sprite = cost.Resource.Icon;
            resourceIcon.enabled = true;
        }

        // 보유량 확인
        int ownedAmount = 0;
        if (ResourceManager.Instance != null)
        {
            ownedAmount = ResourceManager.Instance.GetResourceAmount(cost.Resource);
        }

        bool canAfford = ownedAmount >= cost.Amount;

        // 필요량 텍스트
        if (requiredText != null)
        {
            requiredText.text = cost.Amount.ToString();
            requiredText.color = canAfford ? normalColor : insufficientColor;
        }

        // 보유량 텍스트 (흐린색)
        if (ownedText != null)
        {
            ownedText.text = $"({ownedAmount})";
            ownedText.color = ownedColor;
        }

        // 아이콘 색상도 변경 (부족 시 빨간색)
        if (resourceIcon != null)
        {
            resourceIcon.color = canAfford ? Color.white : insufficientColor;
        }
    }

    /// <summary>
    /// 보유량 업데이트
    /// </summary>
    public void UpdateOwned()
    {
        if (costData == null || costData.Resource == null)
            return;

        int ownedAmount = 0;
        if (ResourceManager.Instance != null)
        {
            ownedAmount = ResourceManager.Instance.GetResourceAmount(costData.Resource);
        }

        if (ownedText != null)
        {
            ownedText.text = $"({ownedAmount})";
        }
    }
}