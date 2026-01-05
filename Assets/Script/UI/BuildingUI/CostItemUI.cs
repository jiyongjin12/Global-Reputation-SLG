using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 가격 아이템 UI
/// - 자원 아이콘 + 수량 표시
/// - 자원 부족 시 빨간색 표시
/// </summary>
public class CostItemUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image resourceIcon;
    [SerializeField] private TextMeshProUGUI amountText;

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color insufficientColor = new Color(1f, 0.3f, 0.3f, 1f);

    private ResourceCost costData;

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(ResourceCost cost)
    {
        costData = cost;

        Debug.Log($"[CostItemUI] Initialize 호출됨");
        Debug.Log($"[CostItemUI] cost null? {cost == null}");

        if (cost == null)
        {
            Debug.LogError("[CostItemUI] cost가 null입니다!");
            return;
        }

        Debug.Log($"[CostItemUI] cost.Resource null? {cost.Resource == null}");

        if (cost.Resource != null)
        {
            Debug.Log($"[CostItemUI] Resource: {cost.Resource.ResourceName}, Amount: {cost.Amount}");
            Debug.Log($"[CostItemUI] Resource.Icon null? {cost.Resource.Icon == null}");
        }

        // 아이콘 설정 (ResourceItemSO에서 가져옴)
        if (resourceIcon != null)
        {
            if (cost.Resource != null && cost.Resource.Icon != null)
            {
                resourceIcon.sprite = cost.Resource.Icon;
                resourceIcon.enabled = true;
                Debug.Log($"[CostItemUI] 아이콘 설정 완료: {cost.Resource.Icon.name}");
            }
            else
            {
                Debug.LogWarning("[CostItemUI] 아이콘이 없습니다! ResourceItemSO에 Icon을 설정하세요.");
            }
        }
        else
        {
            Debug.LogError("[CostItemUI] resourceIcon 참조가 없습니다! Inspector에서 연결하세요.");
        }

        // 수량 텍스트
        if (amountText != null)
        {
            amountText.text = cost.Amount.ToString();
            Debug.Log($"[CostItemUI] 수량 텍스트 설정: {cost.Amount}");
        }
        else
        {
            Debug.LogError("[CostItemUI] amountText 참조가 없습니다! Inspector에서 연결하세요.");
        }

        // 자원 체크
        UpdateAffordability();
    }

    /// <summary>
    /// 자원 보유 여부에 따른 색상 업데이트
    /// </summary>
    public void UpdateAffordability()
    {
        if (costData == null || costData.Resource == null)
            return;

        bool canAfford = true;

        if (ResourceManager.Instance != null)
        {
            int currentAmount = ResourceManager.Instance.GetResourceAmount(costData.Resource);
            canAfford = currentAmount >= costData.Amount;

            // 수량 텍스트 업데이트
            if (amountText != null)
            {
                if (canAfford)
                {
                    amountText.text = costData.Amount.ToString();
                }
                else
                {
                    // 부족하면 "보유량/필요량" 형태로 표시
                    amountText.text = $"{currentAmount}/{costData.Amount}";
                }
            }
        }

        Color targetColor = canAfford ? normalColor : insufficientColor;

        if (resourceIcon != null)
        {
            resourceIcon.color = targetColor;
        }

        if (amountText != null)
        {
            amountText.color = targetColor;
        }
    }
}