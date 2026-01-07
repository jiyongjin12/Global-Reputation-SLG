using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

/// <summary>
/// 인벤토리 슬롯 UI
/// - 아이콘 + 개수 표시
/// - 선택/호버 시 테두리 효과
/// </summary>
public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Image borderImage;
    [SerializeField] private TextMeshProUGUI amountText;

    [Header("Colors")]
    [SerializeField] private Color normalBorderColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color hoverBorderColor = new Color(0.8f, 0.8f, 0.8f, 1f);
    [SerializeField] private Color selectedBorderColor = new Color(1f, 0.2f, 0.2f, 1f);  // 빨간색 (사진처럼)

    [Header("Animation")]
    [SerializeField] private float scaleOnSelect = 1.05f;
    [SerializeField] private float animDuration = 0.1f;

    // 데이터
    private StoredResource storedResource;
    private int slotIndex;
    private bool isSelected = false;

    // 콜백
    private Action<InventorySlotUI> onClickCallback;
    private Action<InventorySlotUI> onHoverCallback;

    public StoredResource StoredResource => storedResource;
    public ResourceItemSO Item => storedResource?.Item;
    public int Amount => storedResource?.Amount ?? 0;

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(StoredResource stored, int index, Action<InventorySlotUI> onClick, Action<InventorySlotUI> onHover)
    {
        storedResource = stored;
        slotIndex = index;
        onClickCallback = onClick;
        onHoverCallback = onHover;

        // 아이콘 설정
        if (iconImage != null && stored.Item.Icon != null)
        {
            iconImage.sprite = stored.Item.Icon;
            iconImage.enabled = true;
        }

        // 수량 텍스트
        UpdateAmountText();

        // 테두리 초기화
        if (borderImage != null)
        {
            borderImage.color = normalBorderColor;
        }

        gameObject.name = $"Slot_{stored.Item.ResourceName}";
    }

    /// <summary>
    /// 수량 업데이트
    /// </summary>
    public void UpdateAmount(int newAmount)
    {
        if (storedResource != null)
        {
            storedResource.Amount = newAmount;
        }
        UpdateAmountText();

        // 펀치 애니메이션
        transform.DOKill();
        transform.DOPunchScale(Vector3.one * 0.1f, 0.2f, 1, 0.5f).SetUpdate(true);
    }

    private void UpdateAmountText()
    {
        if (amountText == null) return;

        int amount = storedResource?.Amount ?? 0;

        if (amount >= 1000)
        {
            amountText.text = $"{amount / 1000f:0.#}K";
        }
        else
        {
            amountText.text = amount.ToString();
        }
    }

    /// <summary>
    /// 선택 상태 설정
    /// </summary>
    public void SetSelected(bool selected)
    {
        isSelected = selected;

        // 테두리 색상
        if (borderImage != null)
        {
            borderImage.color = selected ? selectedBorderColor : normalBorderColor;
        }

        // 스케일 애니메이션
        transform.DOKill();
        float targetScale = selected ? scaleOnSelect : 1f;
        transform.DOScale(targetScale, animDuration).SetUpdate(true);
    }

    // ==================== 포인터 이벤트 ====================

    public void OnPointerEnter(PointerEventData eventData)
    {
        onHoverCallback?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 선택된 상태가 아니면 원래 색으로
        if (!isSelected && borderImage != null)
        {
            borderImage.color = normalBorderColor;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        onClickCallback?.Invoke(this);
    }

    private void OnDestroy()
    {
        transform.DOKill();
    }
}