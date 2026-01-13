using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DG.Tweening;

/// <summary>
/// 유닛 목록 UI
/// - Tab 키로 열기/닫기
/// - W/S 키로 선택 이동
/// - 마우스 호버 우선 표시, 벗어나면 선택으로 복귀
/// - 더블클릭 시 해당 유닛 명령 UI 열기
/// </summary>
public class UnitListUI : MonoBehaviour, IEscapableUI
{
    [Header("=== 메인 패널 ===")]
    [SerializeField] private GameObject mainPanel;
    private RectTransform panelRectTransform;

    [Header("=== 타입 버튼 (3개) ===")]
    [SerializeField] private Button workerButton;
    [SerializeField] private Button fighterButton;
    [SerializeField] private Button mercenaryButton;

    [Header("=== 스크롤 뷰 (3개) ===")]
    [SerializeField] private GameObject workerScroll;
    [SerializeField] private GameObject fighterScroll;
    [SerializeField] private GameObject mercenaryScroll;

    [Header("=== Content 부모 ===")]
    [SerializeField] private Transform workerContent;
    [SerializeField] private Transform fighterContent;
    [SerializeField] private Transform mercenaryContent;

    [Header("=== 프리팹 ===")]
    [SerializeField] private GameObject unitSlotBoxPrefab;

    [Header("=== 버튼 색상 ===")]
    [SerializeField] private Color activeColor = Color.white;
    [SerializeField] private Color inactiveColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("=== Post Processing ===")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private float blurFocalLengthMin = 0f;
    [SerializeField] private float blurFocalLengthMax = 50f;

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float closedPosX = 1400f;
    [SerializeField] private float openedPosX = 0f;
    [SerializeField] private float slideDuration = 0.4f;
    [SerializeField] private Ease slideEase = Ease.OutQuart;

    [Header("=== 게임 속도 설정 ===")]
    [SerializeField] private float slowTimeScale = 0.05f;
    [SerializeField] private float normalTimeScale = 1f;

    [Header("=== ESC 우선순위 ===")]
    [SerializeField] private int escapePriority = 80;

    [Header("=== 상태 ===")]
    [SerializeField] private bool isOpen = false;

    // Depth of Field
    private DepthOfField depthOfField;

    // 트윈
    private Tweener panelTween;
    private Tweener blurTween;

    // 버튼 이미지
    private Image workerButtonImage;
    private Image fighterButtonImage;
    private Image mercenaryButtonImage;

    // 토글 상태
    private bool isWorkerActive = true;
    private bool isFighterActive = false;
    private bool isMercenaryActive = false;

    // 슬롯 관리
    private List<UnitSlotBox> workerSlots = new();
    private List<UnitSlotBox> fighterSlots = new();
    private List<UnitSlotBox> mercenarySlots = new();
    private List<UnitSlotBox> allVisibleSlots = new();  // 현재 보이는 슬롯들

    // 선택/호버 상태 (분리)
    private int selectedIndex = -1;      // 클릭으로 선택된 인덱스
    private int hoveredIndex = -1;       // 마우스 호버 중인 인덱스
    private UnitSlotBox selectedSlot;
    private UnitSlotBox hoveredSlot;

    // 이벤트
    public event Action OnPanelOpened;
    public event Action OnPanelClosed;
    public event Action<Unit> OnUnitSelected;

    // IEscapableUI
    public bool IsOpen => isOpen;
    public int EscapePriority => escapePriority;

    public static UnitListUI Instance { get; private set; }

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        Instance = this;

        if (mainPanel != null)
            panelRectTransform = mainPanel.GetComponent<RectTransform>();

        if (workerButton != null) workerButtonImage = workerButton.GetComponent<Image>();
        if (fighterButton != null) fighterButtonImage = fighterButton.GetComponent<Image>();
        if (mercenaryButton != null) mercenaryButtonImage = mercenaryButton.GetComponent<Image>();

        InitializeDepthOfField();
        InitializeClosedState();
        SetupButtons();
    }

    private void Start()
    {
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.RegisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered += HandleGameAction;
        }

        if (UnitManager.Instance != null)
        {
            UnitManager.Instance.OnUnitAdded += OnUnitAdded;
            UnitManager.Instance.OnUnitRemoved += OnUnitRemoved;
            UnitManager.Instance.OnUnitTypeChanged += OnUnitTypeChanged;
        }
    }

    private void OnDestroy()
    {
        KillAllTweens();
        Time.timeScale = normalTimeScale;

        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.UnregisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered -= HandleGameAction;
        }

        if (UnitManager.Instance != null)
        {
            UnitManager.Instance.OnUnitAdded -= OnUnitAdded;
            UnitManager.Instance.OnUnitRemoved -= OnUnitRemoved;
            UnitManager.Instance.OnUnitTypeChanged -= OnUnitTypeChanged;
        }
    }

    private void Update()
    {
        if (!isOpen) return;
        HandleKeyboardNavigation();
    }

    // ==================== 키보드 네비게이션 ====================

    private void HandleKeyboardNavigation()
    {
        if (allVisibleSlots.Count == 0) return;

        int newIndex = selectedIndex;

        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
        {
            newIndex = selectedIndex <= 0 ? allVisibleSlots.Count - 1 : selectedIndex - 1;
        }
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
        {
            newIndex = selectedIndex >= allVisibleSlots.Count - 1 ? 0 : selectedIndex + 1;
        }
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            // Enter/Space로 더블클릭과 동일한 효과
            if (selectedIndex >= 0 && selectedIndex < allVisibleSlots.Count)
            {
                OnSlotDoubleClick(allVisibleSlots[selectedIndex]);
            }
            return;
        }

        if (newIndex != selectedIndex)
        {
            SelectSlotByIndex(newIndex);
        }
    }

    // ==================== 슬롯 이벤트 핸들러 ====================

    private void OnSlotClick(UnitSlotBox slot)
    {
        SelectSlot(slot);
    }

    private void OnSlotDoubleClick(UnitSlotBox slot)
    {
        if (slot?.TargetUnit == null) return;

        Debug.Log($"[UnitListUI] 더블클릭: {slot.TargetUnit.UnitName}");

        Unit targetUnit = slot.TargetUnit;
        Close();

        // 명령 UI 열기
        if (UnitSelectionManager.Instance != null)
        {
            UnitSelectionManager.Instance.SelectAndOpenCommand(targetUnit);
        }
        else if (UnitCommandUI.Instance != null)
        {
            UnitCommandUI.Instance.Open(targetUnit);
        }
    }

    private void OnSlotHoverEnter(UnitSlotBox slot)
    {
        SetHoveredSlot(slot);
    }

    private void OnSlotHoverExit(UnitSlotBox slot)
    {
        // 호버 해제
        if (hoveredSlot == slot)
        {
            SetHoveredSlot(null);
        }
    }

    // ==================== 선택/호버 관리 ====================

    /// <summary>
    /// 슬롯 선택 (클릭 시)
    /// </summary>
    private void SelectSlot(UnitSlotBox slot)
    {
        if (slot == null) return;

        int index = allVisibleSlots.IndexOf(slot);
        if (index < 0) return;

        SelectSlotByIndex(index);
    }

    /// <summary>
    /// 인덱스로 슬롯 선택
    /// </summary>
    private void SelectSlotByIndex(int index)
    {
        if (index < 0 || index >= allVisibleSlots.Count) return;

        // 이전 선택 해제
        if (selectedSlot != null)
            selectedSlot.SetSelected(false);

        selectedIndex = index;
        selectedSlot = allVisibleSlots[index];
        selectedSlot.SetSelected(true);

        OnUnitSelected?.Invoke(selectedSlot.TargetUnit);

        // 호버 중이 아니면 선택 표시 유지
        UpdateAllSlotDisplays();
    }

    /// <summary>
    /// 호버 슬롯 설정
    /// </summary>
    private void SetHoveredSlot(UnitSlotBox slot)
    {
        // 이전 호버 해제
        if (hoveredSlot != null)
            hoveredSlot.SetHovered(false);

        hoveredSlot = slot;
        hoveredIndex = slot != null ? allVisibleSlots.IndexOf(slot) : -1;

        // 새 호버 설정
        if (hoveredSlot != null)
            hoveredSlot.SetHovered(true);

        UpdateAllSlotDisplays();
    }

    /// <summary>
    /// 모든 슬롯 표시 업데이트
    /// </summary>
    private void UpdateAllSlotDisplays()
    {
        for (int i = 0; i < allVisibleSlots.Count; i++)
        {
            var slot = allVisibleSlots[i];
            bool isThisSelected = (i == selectedIndex);
            bool isThisHovered = (i == hoveredIndex);

            slot.SetSelected(isThisSelected && !isThisHovered);
            slot.SetHovered(isThisHovered);
        }
    }

    /// <summary>
    /// 선택 해제
    /// </summary>
    private void DeselectAll()
    {
        if (selectedSlot != null)
            selectedSlot.SetSelected(false);
        if (hoveredSlot != null)
            hoveredSlot.SetHovered(false);

        selectedSlot = null;
        hoveredSlot = null;
        selectedIndex = -1;
        hoveredIndex = -1;
    }

    // ==================== 입력 처리 ====================

    private void HandleGameAction(GameAction action)
    {
        if (action == GameAction.OpenUnitList)
        {
            // 다른 UI가 열려있으면 무시
            if (UnitCommandUI.Instance != null && UnitCommandUI.Instance.IsOpen) return;
            if (BuildingUIManager.Instance != null && BuildingUIManager.Instance.IsOpen) return;
            if (ChatPanelController.Instance != null && ChatPanelController.Instance.IsOpen) return;
            if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) return;

            Toggle();
        }
    }

    // ==================== 초기화 ====================

    private void InitializeDepthOfField()
    {
        if (globalVolume != null && globalVolume.profile != null)
        {
            if (!globalVolume.profile.TryGet(out depthOfField))
                Debug.LogWarning("[UnitListUI] Depth of Field가 Volume에 없습니다.");
        }
    }

    private void InitializeClosedState()
    {
        if (panelRectTransform != null)
        {
            Vector2 pos = panelRectTransform.anchoredPosition;
            pos.x = closedPosX;
            panelRectTransform.anchoredPosition = pos;
        }
        mainPanel.SetActive(false);
        isOpen = false;
    }

    private void SetupButtons()
    {
        if (workerButton != null)
            workerButton.onClick.AddListener(() => ToggleCategory(UnitType.Worker));
        if (fighterButton != null)
            fighterButton.onClick.AddListener(() => ToggleCategory(UnitType.Fighter));
        if (mercenaryButton != null)
            mercenaryButton.onClick.AddListener(ToggleMercenary);

        UpdateButtonColors();
        UpdateScrollVisibility();
    }

    // ==================== 열기/닫기 ====================

    public void Toggle()
    {
        if (isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;

        KillAllTweens();
        mainPanel.SetActive(true);
        RefreshAllUnits();

        // 첫 번째 슬롯 선택
        if (allVisibleSlots.Count > 0)
            SelectSlotByIndex(0);

        panelTween = panelRectTransform
            .DOAnchorPosX(openedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true);

        if (depthOfField != null)
        {
            blurTween = DOTween.To(() => depthOfField.focalLength.value,
                x => depthOfField.focalLength.value = x, blurFocalLengthMax, slideDuration)
                .SetUpdate(true);
        }

        Time.timeScale = slowTimeScale;
        OnPanelOpened?.Invoke();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        KillAllTweens();
        DeselectAll();

        panelTween = panelRectTransform
            .DOAnchorPosX(closedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true)
            .OnComplete(() => mainPanel.SetActive(false));

        if (depthOfField != null)
        {
            blurTween = DOTween.To(() => depthOfField.focalLength.value,
                x => depthOfField.focalLength.value = x, blurFocalLengthMin, slideDuration)
                .SetUpdate(true);
        }

        Time.timeScale = normalTimeScale;
        OnPanelClosed?.Invoke();
    }

    private void KillAllTweens()
    {
        panelTween?.Kill();
        blurTween?.Kill();
    }

    // ==================== 카테고리 토글 ====================

    private void ToggleCategory(UnitType type)
    {
        switch (type)
        {
            case UnitType.Worker: isWorkerActive = !isWorkerActive; break;
            case UnitType.Fighter: isFighterActive = !isFighterActive; break;
        }
        UpdateButtonColors();
        UpdateScrollVisibility();
        RebuildVisibleSlots();
    }

    private void ToggleMercenary()
    {
        isMercenaryActive = !isMercenaryActive;
        UpdateButtonColors();
        UpdateScrollVisibility();
        RebuildVisibleSlots();
    }

    private void UpdateButtonColors()
    {
        if (workerButtonImage != null)
            workerButtonImage.color = isWorkerActive ? activeColor : inactiveColor;
        if (fighterButtonImage != null)
            fighterButtonImage.color = isFighterActive ? activeColor : inactiveColor;
        if (mercenaryButtonImage != null)
            mercenaryButtonImage.color = isMercenaryActive ? activeColor : inactiveColor;
    }

    private void UpdateScrollVisibility()
    {
        if (workerScroll != null) workerScroll.SetActive(isWorkerActive);
        if (fighterScroll != null) fighterScroll.SetActive(isFighterActive);
        if (mercenaryScroll != null) mercenaryScroll.SetActive(isMercenaryActive);
    }

    // ==================== 유닛 목록 관리 ====================

    public void RefreshAllUnits()
    {
        ClearAllSlots();

        if (UnitManager.Instance != null)
        {
            int globalIndex = 0;

            foreach (var data in UnitManager.Instance.GetUnitDataByType(UnitType.Worker))
            {
                AddUnitSlot(data, UnitType.Worker, globalIndex++);
            }

            foreach (var data in UnitManager.Instance.GetUnitDataByType(UnitType.Fighter))
            {
                AddUnitSlot(data, UnitType.Fighter, globalIndex++);
            }
        }

        RebuildVisibleSlots();

        Canvas.ForceUpdateCanvases();
        if (workerContent is RectTransform rt1) LayoutRebuilder.ForceRebuildLayoutImmediate(rt1);
        if (fighterContent is RectTransform rt2) LayoutRebuilder.ForceRebuildLayoutImmediate(rt2);
        if (mercenaryContent is RectTransform rt3) LayoutRebuilder.ForceRebuildLayoutImmediate(rt3);
    }

    private void AddUnitSlot(UnitData data, UnitType type, int index)
    {
        if (data?.Unit == null) return;

        Transform parent = GetContentForType(type);
        if (parent == null || unitSlotBoxPrefab == null) return;

        GameObject slotObj = Instantiate(unitSlotBoxPrefab, parent);
        UnitSlotBox slot = slotObj.GetComponent<UnitSlotBox>();

        if (slot != null)
        {
            int currentDay = UnitManager.Instance?.GetCurrentDay() ?? 1;
            int activeDays = data.GetActiveDays(currentDay);

            slot.Initialize(data.Unit, index, data.JoinedWeek, activeDays,
                OnSlotClick, OnSlotDoubleClick, OnSlotHoverEnter, OnSlotHoverExit);

            GetSlotListForType(type).Add(slot);
        }
    }

    /// <summary>
    /// 현재 보이는 슬롯 리스트 재구성
    /// </summary>
    private void RebuildVisibleSlots()
    {
        allVisibleSlots.Clear();

        if (isWorkerActive)
            allVisibleSlots.AddRange(workerSlots);
        if (isFighterActive)
            allVisibleSlots.AddRange(fighterSlots);
        if (isMercenaryActive)
            allVisibleSlots.AddRange(mercenarySlots);

        // 선택 인덱스 조정
        if (selectedIndex >= allVisibleSlots.Count)
            selectedIndex = allVisibleSlots.Count - 1;
        if (selectedIndex < 0 && allVisibleSlots.Count > 0)
            selectedIndex = 0;

        // 선택 슬롯 업데이트
        selectedSlot = selectedIndex >= 0 ? allVisibleSlots[selectedIndex] : null;
        UpdateAllSlotDisplays();
    }

    public void RemoveUnitSlot(Unit unit)
    {
        RemoveFromList(workerSlots, unit);
        RemoveFromList(fighterSlots, unit);
        RemoveFromList(mercenarySlots, unit);
        RebuildVisibleSlots();
    }

    private void RemoveFromList(List<UnitSlotBox> list, Unit unit)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].TargetUnit == unit)
            {
                Destroy(list[i].gameObject);
                list.RemoveAt(i);
            }
        }
    }

    private void ClearAllSlots()
    {
        ClearSlotList(workerSlots, workerContent);
        ClearSlotList(fighterSlots, fighterContent);
        ClearSlotList(mercenarySlots, mercenaryContent);
        allVisibleSlots.Clear();
        selectedSlot = null;
        hoveredSlot = null;
        selectedIndex = -1;
        hoveredIndex = -1;
    }

    private void ClearSlotList(List<UnitSlotBox> list, Transform content)
    {
        foreach (var slot in list)
            if (slot != null) Destroy(slot.gameObject);
        list.Clear();

        if (content != null)
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);
    }

    private Transform GetContentForType(UnitType type)
    {
        return type switch
        {
            UnitType.Worker => workerContent,
            UnitType.Fighter => fighterContent,
            _ => mercenaryContent
        };
    }

    private List<UnitSlotBox> GetSlotListForType(UnitType type)
    {
        return type switch
        {
            UnitType.Worker => workerSlots,
            UnitType.Fighter => fighterSlots,
            _ => mercenarySlots
        };
    }

    // ==================== 이벤트 핸들러 ====================

    public void OnUnitAdded(Unit unit)
    {
        if (!isOpen) return;
        RefreshAllUnits();
    }

    public void OnUnitRemoved(Unit unit)
    {
        if (!isOpen) return;
        RemoveUnitSlot(unit);
    }

    private void OnUnitTypeChanged(Unit unit, UnitType oldType, UnitType newType)
    {
        if (!isOpen) return;
        RefreshAllUnits();
    }

    // ==================== 유틸리티 ====================

    public int GetUnitCount(UnitType type) => GetSlotListForType(type).Count;
    public int GetTotalUnitCount() => workerSlots.Count + fighterSlots.Count + mercenarySlots.Count;
}