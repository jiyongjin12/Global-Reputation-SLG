using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

/// <summary>
/// 인벤토리 슬롯 UI
/// - 아이콘 + 개수 표시
/// - 호버 시 툴팁 표시 (선택사항)
/// - 수량 변경 시 애니메이션
/// </summary>
public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private Image backgroundImage;

    [Header("Colors")]
    [SerializeField] private Color normalBgColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);
    [SerializeField] private Color hoverBgColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color emptyColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

    [Header("Animation")]
    [SerializeField] private float punchScale = 0.1f;
    [SerializeField] private float punchDuration = 0.2f;

    // 데이터
    private StoredResource storedResource;
    private int currentAmount;

    public ResourceItemSO Item => storedResource?.Item;
    public int Amount => currentAmount;

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(StoredResource stored)
    {
        storedResource = stored;
        currentAmount = stored.Amount;

        // 아이콘 설정
        if (iconImage != null && stored.Item.Icon != null)
        {
            iconImage.sprite = stored.Item.Icon;
            iconImage.enabled = true;
        }

        // 수량 텍스트
        UpdateAmountText();

        // 배경색
        if (backgroundImage != null)
        {
            backgroundImage.color = normalBgColor;
        }

        // 이름 설정 (디버그용)
        gameObject.name = $"Slot_{stored.Item.ResourceName}";
    }

    /// <summary>
    /// 수량 업데이트
    /// </summary>
    public void UpdateAmount(int newAmount)
    {
        int oldAmount = currentAmount;
        currentAmount = newAmount;

        UpdateAmountText();

        // 수량 변경 애니메이션
        if (oldAmount != newAmount)
        {
            PlayAmountChangeAnimation(newAmount > oldAmount);
        }
    }

    /// <summary>
    /// 수량 텍스트 업데이트
    /// </summary>
    private void UpdateAmountText()
    {
        if (amountText == null)
            return;

        if (currentAmount <= 0)
        {
            amountText.text = "0";
            amountText.color = emptyColor;
            if (iconImage != null)
                iconImage.color = emptyColor;
        }
        else if (currentAmount >= 1000)
        {
            // 1000 이상이면 K 단위로 표시
            amountText.text = $"{currentAmount / 1000f:0.#}K";
            amountText.color = Color.white;
            if (iconImage != null)
                iconImage.color = Color.white;
        }
        else
        {
            amountText.text = currentAmount.ToString();
            amountText.color = Color.white;
            if (iconImage != null)
                iconImage.color = Color.white;
        }
    }

    /// <summary>
    /// 수량 변경 애니메이션
    /// </summary>
    private void PlayAmountChangeAnimation(bool increased)
    {
        // 펀치 스케일 애니메이션
        transform.DOKill();
        transform.localScale = Vector3.one;
        transform.DOPunchScale(Vector3.one * punchScale, punchDuration, 1, 0.5f)
            .SetUpdate(true);

        // 텍스트 색상 깜빡임
        if (amountText != null)
        {
            Color flashColor = increased ? Color.green : Color.red;
            amountText.DOColor(flashColor, punchDuration * 0.5f)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    amountText.DOColor(Color.white, punchDuration * 0.5f)
                        .SetUpdate(true);
                });
        }
    }

    // ==================== 호버 이벤트 ====================

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (backgroundImage != null)
        {
            backgroundImage.DOColor(hoverBgColor, 0.1f).SetUpdate(true);
        }

        // 툴팁 표시 (InventoryTooltip이 있다면)
        if (storedResource != null)
        {
            ShowTooltip();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (backgroundImage != null)
        {
            backgroundImage.DOColor(normalBgColor, 0.1f).SetUpdate(true);
        }

        HideTooltip();
    }

    private void ShowTooltip()
    {
        if (InventoryTooltip.Instance != null && storedResource != null)
        {
            InventoryTooltip.Instance.Show(storedResource.Item, currentAmount);
        }
    }

    private void HideTooltip()
    {
        if (InventoryTooltip.Instance != null)
        {
            InventoryTooltip.Instance.Hide();
        }
    }

    private void OnDestroy()
    {
        transform.DOKill();
        if (amountText != null)
            amountText.DOKill();
        if (backgroundImage != null)
            backgroundImage.DOKill();
    }
}