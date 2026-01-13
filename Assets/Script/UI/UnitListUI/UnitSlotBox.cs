using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 유닛 슬롯 박스 UI
/// - 선택 테두리
/// - 아이콘
/// - 이름 + 레벨
/// - 경험치 바
/// - 배고픔, 충성심 바
/// - 주차 정보 텍스트
/// </summary>
public class UnitSlotBox : MonoBehaviour
{
    [Header("=== UI 참조 ===")]
    [SerializeField] private GameObject selectionBorder;    // 선택 시 테두리
    [SerializeField] private Image iconImage;               // 유닛 아이콘
    [SerializeField] private TextMeshProUGUI nameText;      // "이름 - Lvl 000"
    [SerializeField] private Image expBar;                  // 경험치 바 (Fill Amount)
    [SerializeField] private Image hungerBar;               // 배고픔 바
    [SerializeField] private Image loyaltyBar;              // 충성심 바
    [SerializeField] private TextMeshProUGUI infoText;      // "00주차에 들어옴 / 00일 동안 마을에서 행동"

    [Header("=== 바 색상 ===")]
    [SerializeField] private Color expBarColor = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color hungerBarColor = new Color(0.8f, 0.4f, 0.2f);
    [SerializeField] private Color loyaltyBarColor = new Color(0.3f, 0.5f, 0.8f);

    [Header("=== 버튼 ===")]
    [SerializeField] private Button slotButton;

    // 내부 데이터
    private Unit targetUnit;
    private Action<UnitSlotBox> onClickCallback;
    private bool isSelected = false;

    // 유닛 입장 데이터 (GameManager 또는 TimeManager에서 가져올 값)
    private int joinedWeek = 0;
    private int activeDays = 0;

    // ==================== Properties ====================

    public Unit TargetUnit => targetUnit;
    public bool IsSelected => isSelected;

    // ==================== 초기화 ====================

    private void Awake()
    {
        // 버튼 이벤트 설정
        if (slotButton == null)
            slotButton = GetComponent<Button>();

        if (slotButton != null)
            slotButton.onClick.AddListener(OnClick);

        // 바 색상 설정
        if (expBar != null) expBar.color = expBarColor;
        if (hungerBar != null) hungerBar.color = hungerBarColor;
        if (loyaltyBar != null) loyaltyBar.color = loyaltyBarColor;

        // 선택 테두리 숨기기
        if (selectionBorder != null)
            selectionBorder.SetActive(false);
    }

    /// <summary>
    /// 유닛 데이터로 초기화 (UnitManager 연동)
    /// </summary>
    public void Initialize(Unit unit, int joinedWeekValue, int activeDaysValue, Action<UnitSlotBox> onClick)
    {
        targetUnit = unit;
        onClickCallback = onClick;
        joinedWeek = joinedWeekValue;
        activeDays = activeDaysValue;

        UpdateUI();
    }

    /// <summary>
    /// 유닛 데이터로 초기화 (레거시)
    /// </summary>
    public void Initialize(Unit unit, Action<UnitSlotBox> onClick)
    {
        Initialize(unit, 1, 0, onClick);
    }

    // ==================== UI 업데이트 ====================

    /// <summary>
    /// 전체 UI 업데이트
    /// </summary>
    public void UpdateUI()
    {
        if (targetUnit == null) return;

        UpdateNameText();
        UpdateBars();
        UpdateInfoText();
        UpdateIcon();
    }

    /// <summary>
    /// 이름 + 레벨 텍스트 업데이트
    /// </summary>
    private void UpdateNameText()
    {
        if (nameText != null)
        {
            // "이름 - Lvl 000" 형식
            nameText.text = $"{targetUnit.UnitName} - Lvl {targetUnit.Level:000}";
        }
    }

    /// <summary>
    /// 바 업데이트
    /// </summary>
    private void UpdateBars()
    {
        // 경험치 바
        if (expBar != null)
        {
            float expPercent = targetUnit.ExpToNextLevel > 0
                ? targetUnit.CurrentExp / targetUnit.ExpToNextLevel
                : 0f;
            expBar.fillAmount = Mathf.Clamp01(expPercent);
        }

        // 배고픔 바
        if (hungerBar != null)
        {
            hungerBar.fillAmount = Mathf.Clamp01(targetUnit.Hunger / 100f);
        }

        // 충성심 바
        if (loyaltyBar != null)
        {
            loyaltyBar.fillAmount = Mathf.Clamp01(targetUnit.Loyalty / 100f);
        }
    }

    /// <summary>
    /// 주차 정보 텍스트 업데이트
    /// </summary>
    private void UpdateInfoText()
    {
        if (infoText != null)
        {
            // "00주차에 들어옴 / 00일 동안 마을에서 행동"
            infoText.text = $"{joinedWeek:00}주차에 들어옴 / {activeDays:00}일 동안 마을에서 행동";
        }
    }

    /// <summary>
    /// 아이콘 업데이트 (추후 구현)
    /// </summary>
    private void UpdateIcon()
    {
        // 유닛 타입별 아이콘 설정
        // if (iconImage != null && targetUnit != null)
        // {
        //     iconImage.sprite = UnitIconDatabase.GetIcon(targetUnit.Type);
        // }
    }

    // ==================== 선택 ====================

    /// <summary>
    /// 선택 상태 설정
    /// </summary>
    public void SetSelected(bool selected)
    {
        isSelected = selected;

        if (selectionBorder != null)
            selectionBorder.SetActive(selected);
    }

    // ==================== 이벤트 ====================

    /// <summary>
    /// 클릭 시
    /// </summary>
    private void OnClick()
    {
        onClickCallback?.Invoke(this);
    }

    // ==================== 외부 데이터 설정 ====================

    /// <summary>
    /// 유닛 입장 주차 설정
    /// </summary>
    public void SetJoinedWeek(int week)
    {
        joinedWeek = week;
        UpdateInfoText();
    }

    /// <summary>
    /// 유닛 활동 일수 설정
    /// </summary>
    public void SetActiveDays(int days)
    {
        activeDays = days;
        UpdateInfoText();
    }

    /// <summary>
    /// 유닛 입장 정보 한번에 설정
    /// </summary>
    public void SetUnitInfo(int week, int days)
    {
        joinedWeek = week;
        activeDays = days;
        UpdateInfoText();
    }

    // ==================== 갱신 ====================

    /// <summary>
    /// 유닛 정보 갱신 (매 프레임 또는 주기적으로)
    /// </summary>
    public void Refresh()
    {
        if (targetUnit == null || !targetUnit.IsAlive)
        {
            // 유닛이 죽었으면 제거 요청
            UnitListUI.Instance?.RemoveUnitSlot(targetUnit);
            return;
        }

        // 활동 일수 갱신 (UnitManager에서)
        if (UnitManager.Instance != null)
        {
            activeDays = UnitManager.Instance.GetUnitActiveDays(targetUnit);
        }

        UpdateBars();
        UpdateInfoText();
    }

    private void Update()
    {
        // 패널이 열려있을 때만 갱신
        if (UnitListUI.Instance != null && UnitListUI.Instance.IsOpen)
        {
            Refresh();
        }
    }
}