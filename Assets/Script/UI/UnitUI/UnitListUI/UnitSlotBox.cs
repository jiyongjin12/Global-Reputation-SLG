using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 유닛 슬롯 박스 UI
/// - 호버/선택 상태 표시
/// - 더블클릭 감지
/// - UnitListUI에서 상태 제어
/// </summary>
public class UnitSlotBox : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("=== UI 참조 ===")]
    [SerializeField] private GameObject selectionBorder;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Image expBar;
    [SerializeField] private Image hungerBar;
    [SerializeField] private Image loyaltyBar;
    [SerializeField] private Image mentalHealthBar;
    [SerializeField] private TextMeshProUGUI infoText;

    [Header("=== 경험치 바 색상 ===")]
    [SerializeField] private Color expBarColor = new Color(0.2f, 0.8f, 0.2f);

    [Header("=== 상태 바 색상 ===")]
    [SerializeField] private Color highColor = new Color(0.3f, 0.8f, 1f);
    [SerializeField] private Color midColor = new Color(1f, 0.6f, 0.2f);
    [SerializeField] private Color lowColor = new Color(1f, 0.2f, 0.2f);

    [Header("=== 테두리 색상 ===")]
    [SerializeField] private Color hoverBorderColor = new Color(1f, 1f, 1f, 0.5f);
    [SerializeField] private Color selectedBorderColor = new Color(1f, 0.8f, 0.2f, 1f);

    [Header("=== 더블클릭 설정 ===")]
    [SerializeField] private float doubleClickTime = 0.3f;

    // 내부 데이터
    private Unit targetUnit;
    private Image borderImage;
    private int slotIndex;

    // 상태
    private bool isSelected = false;
    private bool isHovered = false;

    // 더블클릭
    private float lastClickTime = 0f;

    // 유닛 정보
    private int joinedWeek = 0;
    private int activeDays = 0;

    // 콜백
    private Action<UnitSlotBox> onClickCallback;
    private Action<UnitSlotBox> onDoubleClickCallback;
    private Action<UnitSlotBox> onHoverEnterCallback;
    private Action<UnitSlotBox> onHoverExitCallback;

    // ==================== Properties ====================

    public Unit TargetUnit => targetUnit;
    public bool IsSelected => isSelected;
    public bool IsHovered => isHovered;
    public int SlotIndex => slotIndex;

    // ==================== 초기화 ====================

    private void Awake()
    {
        if (expBar != null) expBar.color = expBarColor;

        if (selectionBorder != null)
        {
            borderImage = selectionBorder.GetComponent<Image>();
            selectionBorder.SetActive(false);
        }
    }

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(Unit unit, int index, int week, int days,
        Action<UnitSlotBox> onClick,
        Action<UnitSlotBox> onDoubleClick,
        Action<UnitSlotBox> onHoverEnter,
        Action<UnitSlotBox> onHoverExit)
    {
        targetUnit = unit;
        slotIndex = index;
        joinedWeek = week;
        activeDays = days;
        onClickCallback = onClick;
        onDoubleClickCallback = onDoubleClick;
        onHoverEnterCallback = onHoverEnter;
        onHoverExitCallback = onHoverExit;

        UpdateUI();
    }

    // ==================== 포인터 이벤트 ====================

    public void OnPointerEnter(PointerEventData eventData)
    {
        onHoverEnterCallback?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        onHoverExitCallback?.Invoke(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        float currentTime = Time.unscaledTime;

        if (currentTime - lastClickTime <= doubleClickTime)
        {
            // 더블클릭
            onDoubleClickCallback?.Invoke(this);
            lastClickTime = 0f;
        }
        else
        {
            // 싱글클릭
            onClickCallback?.Invoke(this);
            lastClickTime = currentTime;
        }
    }

    // ==================== 상태 설정 ====================

    /// <summary>
    /// 선택 상태 설정 (UnitListUI에서 호출)
    /// </summary>
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateBorderDisplay();
    }

    /// <summary>
    /// 호버 상태 설정 (UnitListUI에서 호출)
    /// </summary>
    public void SetHovered(bool hovered)
    {
        isHovered = hovered;
        UpdateBorderDisplay();
    }

    /// <summary>
    /// 테두리 표시 업데이트
    /// </summary>
    private void UpdateBorderDisplay()
    {
        if (selectionBorder == null) return;

        // 호버 또는 선택 시 테두리 표시
        bool showBorder = isHovered || isSelected;
        selectionBorder.SetActive(showBorder);

        if (borderImage != null && showBorder)
        {
            // 호버 우선 (호버 중이면 호버색, 아니면 선택색)
            borderImage.color = isHovered ? hoverBorderColor : selectedBorderColor;
        }
    }

    // ==================== UI 업데이트 ====================

    public void UpdateUI()
    {
        if (targetUnit == null) return;

        UpdateNameText();
        UpdateBars();
        UpdateInfoText();
    }

    private void UpdateNameText()
    {
        if (nameText != null)
            nameText.text = $"{targetUnit.UnitName} - Lvl {targetUnit.Level:000}";
    }

    private void UpdateBars()
    {
        if (expBar != null)
        {
            float expPercent = targetUnit.ExpToNextLevel > 0
                ? targetUnit.CurrentExp / targetUnit.ExpToNextLevel : 0f;
            expBar.fillAmount = Mathf.Clamp01(expPercent);
        }

        if (hungerBar != null)
        {
            float percent = targetUnit.Hunger / 100f;
            hungerBar.fillAmount = Mathf.Clamp01(percent);
            hungerBar.color = GetStatusBarColor(percent);
        }

        if (loyaltyBar != null)
        {
            float percent = targetUnit.Loyalty / 100f;
            loyaltyBar.fillAmount = Mathf.Clamp01(percent);
            loyaltyBar.color = GetStatusBarColor(percent);
        }

        if (mentalHealthBar != null)
        {
            float percent = targetUnit.MentalHealth / 100f;
            mentalHealthBar.fillAmount = Mathf.Clamp01(percent);
            mentalHealthBar.color = GetStatusBarColor(percent);
        }
    }

    private Color GetStatusBarColor(float percent)
    {
        if (percent >= 0.5f)
            return Color.Lerp(midColor, highColor, (percent - 0.5f) / 0.5f);
        else if (percent >= 0.2f)
            return Color.Lerp(lowColor, midColor, (percent - 0.2f) / 0.3f);
        else
            return lowColor;
    }

    private void UpdateInfoText()
    {
        if (infoText != null)
            infoText.text = $"{joinedWeek:00}주차에 들어옴 / {activeDays:00}일 동안 마을에서 행동";
    }

    // ==================== 갱신 ====================

    public void Refresh()
    {
        if (targetUnit == null || !targetUnit.IsAlive)
        {
            UnitListUI.Instance?.RemoveUnitSlot(targetUnit);
            return;
        }

        if (UnitManager.Instance != null)
            activeDays = UnitManager.Instance.GetUnitActiveDays(targetUnit);

        UpdateBars();
        UpdateInfoText();
    }

    private void Update()
    {
        if (UnitListUI.Instance != null && UnitListUI.Instance.IsOpen)
            Refresh();
    }

    // ==================== 외부 설정 ====================

    public void SetUnitInfo(int week, int days)
    {
        joinedWeek = week;
        activeDays = days;
        UpdateInfoText();
    }
}