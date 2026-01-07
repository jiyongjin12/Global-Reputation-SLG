using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 가격 아이템 UI
/// - 자원 아이콘 + 수량 표시
/// - 자원 부족 시 빨간색 표시
/// - ★ 개수에 따른 크기 조절 지원
/// </summary>
public class CostItemUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image resourceIcon;
    [SerializeField] private TextMeshProUGUI amountText;

    [Header("Size Settings (기본값)")]
    [SerializeField] private float defaultIconSize;
    [SerializeField] private float defaultFontSize;

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color insufficientColor = new Color(1f, 0.3f, 0.3f, 1f);

    private ResourceCost costData;
    private RectTransform iconRectTransform;

    private void Awake()
    {
        // 아이콘 RectTransform 가져오기
        if (resourceIcon != null)
        {
            iconRectTransform = resourceIcon.GetComponent<RectTransform>();
        }
    }

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(ResourceCost cost)
    {
        costData = cost;

        if (cost == null)
        {
            Debug.LogError("[CostItemUI] cost가 null입니다!");
            return;
        }

        // 아이콘 설정
        if (resourceIcon != null && cost.Resource != null)
        {
            if (cost.Resource.Icon != null)
            {
                resourceIcon.sprite = cost.Resource.Icon;
                resourceIcon.enabled = true;
            }
            else
            {
                Debug.LogWarning($"[CostItemUI] {cost.Resource.ResourceName}의 아이콘이 없습니다!");
            }
        }

        // 수량 텍스트
        if (amountText != null)
        {
            amountText.text = cost.Amount.ToString();
        }

        // 자원 체크
        UpdateAffordability();
    }

    /// <summary>
    /// ★ 크기 조절 (개수에 따라 호출됨)
    /// </summary>
    /// <param name="scale">0.5 ~ 1.0 (1.0 = 기본 크기)</param>
    public void SetScale(float scale)
    {
        scale = Mathf.Clamp(scale, 0.5f, 1f);

        // 아이콘 크기 조절
        if (iconRectTransform != null)
        {
            float iconSize = defaultIconSize * scale;
            iconRectTransform.sizeDelta = new Vector2(iconSize, iconSize);
        }

        // 폰트 크기 조절
        if (amountText != null)
        {
            amountText.fontSize = defaultFontSize * scale;
        }
    }

    /// <summary>
    /// ★ 직접 크기 지정
    /// </summary>
    public void SetSize(float iconSize, float fontSize)
    {
        // 아이콘 크기
        if (iconRectTransform != null)
        {
            iconRectTransform.sizeDelta = new Vector2(iconSize, iconSize);
        }

        // 폰트 크기
        if (amountText != null)
        {
            amountText.fontSize = fontSize;
        }
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