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
/// - IEscapableUI 구현
/// - 버튼 3개 (일반, 전투, 용병) - 토글 방식
/// - 각 타입별 스크롤 뷰
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

    [Header("=== Content 부모 (UnitBox 소환 위치) ===")]
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

    // 버튼 이미지 (색상 변경용)
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

    // 선택된 슬롯
    private UnitSlotBox selectedSlot;

    // 이벤트
    public event Action OnPanelOpened;
    public event Action OnPanelClosed;
    public event Action<Unit> OnUnitSelected;

    // ==================== IEscapableUI 구현 ====================
    public bool IsOpen => isOpen;
    public int EscapePriority => escapePriority;

    public static UnitListUI Instance { get; private set; }

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        Instance = this;

        if (mainPanel != null)
            panelRectTransform = mainPanel.GetComponent<RectTransform>();

        // 버튼 이미지 캐시
        if (workerButton != null) workerButtonImage = workerButton.GetComponent<Image>();
        if (fighterButton != null) fighterButtonImage = fighterButton.GetComponent<Image>();
        if (mercenaryButton != null) mercenaryButtonImage = mercenaryButton.GetComponent<Image>();

        InitializeDepthOfField();
        InitializeClosedState();
        SetupButtons();
    }

    private void Start()
    {
        // GameInputManager에 등록
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.RegisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered += HandleGameAction;
            Debug.Log("[UnitListUI] GameInputManager에 등록됨");
        }

        // UnitManager 이벤트 구독 (유닛 추가/제거 시 갱신)
        if (UnitManager.Instance != null)
        {
            UnitManager.Instance.OnUnitAdded += OnUnitAdded;
            UnitManager.Instance.OnUnitRemoved += OnUnitRemoved;
            UnitManager.Instance.OnUnitTypeChanged += OnUnitTypeChanged;
            Debug.Log("[UnitListUI] UnitManager에 등록됨");
        }
    }

    private void OnDestroy()
    {
        KillAllTweens();

        // TimeScale 복구
        Time.timeScale = normalTimeScale;

        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.UnregisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered -= HandleGameAction;
        }

        // UnitManager 이벤트 구독 해제
        if (UnitManager.Instance != null)
        {
            UnitManager.Instance.OnUnitAdded -= OnUnitAdded;
            UnitManager.Instance.OnUnitRemoved -= OnUnitRemoved;
            UnitManager.Instance.OnUnitTypeChanged -= OnUnitTypeChanged;
        }
    }

    // ==================== 입력 처리 ====================

    private void HandleGameAction(GameAction action)
    {
        if (action == GameAction.OpenUnitList)
        {
            // 다른 UI가 열려있으면 무시
            if (BuildingUIManager.Instance != null && BuildingUIManager.Instance.IsOpen)
                return;
            if (ChatPanelController.Instance != null && ChatPanelController.Instance.IsOpen)
                return;
            if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen)
                return;

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
        // 버튼 클릭 이벤트 연결
        if (workerButton != null)
            workerButton.onClick.AddListener(() => ToggleCategory(UnitType.Worker));

        if (fighterButton != null)
            fighterButton.onClick.AddListener(() => ToggleCategory(UnitType.Fighter));

        if (mercenaryButton != null)
            mercenaryButton.onClick.AddListener(ToggleMercenary);

        // 초기 상태 설정
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

        Debug.Log("[UnitListUI] Open() 호출됨");

        KillAllTweens();
        mainPanel.SetActive(true);

        // 유닛 목록 갱신
        RefreshAllUnits();

        // 1. 패널 슬라이드
        panelTween = panelRectTransform
            .DOAnchorPosX(openedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true);

        // 2. Depth of Field 블러
        if (depthOfField != null)
        {
            blurTween = DOTween.To(() => depthOfField.focalLength.value,
                x => depthOfField.focalLength.value = x, blurFocalLengthMax, slideDuration)
                .SetUpdate(true);
        }

        // 3. TimeScale 설정
        Time.timeScale = slowTimeScale;

        OnPanelOpened?.Invoke();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        Debug.Log("[UnitListUI] Close() 호출됨");

        KillAllTweens();
        DeselectAll();

        // 1. 패널 슬라이드
        panelTween = panelRectTransform
            .DOAnchorPosX(closedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true)
            .OnComplete(() => mainPanel.SetActive(false));

        // 2. 블러 해제
        if (depthOfField != null)
        {
            blurTween = DOTween.To(() => depthOfField.focalLength.value,
                x => depthOfField.focalLength.value = x, blurFocalLengthMin, slideDuration)
                .SetUpdate(true);
        }

        // 3. TimeScale 복구
        Time.timeScale = normalTimeScale;

        OnPanelClosed?.Invoke();
    }

    private void KillAllTweens()
    {
        panelTween?.Kill();
        blurTween?.Kill();
    }

    // ==================== 카테고리 토글 ====================

    /// <summary>
    /// 카테고리 버튼 토글 (일반/전투)
    /// </summary>
    private void ToggleCategory(UnitType type)
    {
        switch (type)
        {
            case UnitType.Worker:
                isWorkerActive = !isWorkerActive;
                break;
            case UnitType.Fighter:
                isFighterActive = !isFighterActive;
                break;
        }

        UpdateButtonColors();
        UpdateScrollVisibility();
    }

    /// <summary>
    /// 용병 버튼 토글
    /// </summary>
    private void ToggleMercenary()
    {
        isMercenaryActive = !isMercenaryActive;
        UpdateButtonColors();
        UpdateScrollVisibility();
    }

    /// <summary>
    /// 버튼 색상 업데이트
    /// </summary>
    private void UpdateButtonColors()
    {
        if (workerButtonImage != null)
            workerButtonImage.color = isWorkerActive ? activeColor : inactiveColor;

        if (fighterButtonImage != null)
            fighterButtonImage.color = isFighterActive ? activeColor : inactiveColor;

        if (mercenaryButtonImage != null)
            mercenaryButtonImage.color = isMercenaryActive ? activeColor : inactiveColor;
    }

    /// <summary>
    /// 스크롤 뷰 표시/숨김
    /// </summary>
    private void UpdateScrollVisibility()
    {
        if (workerScroll != null)
            workerScroll.SetActive(isWorkerActive);

        if (fighterScroll != null)
            fighterScroll.SetActive(isFighterActive);

        if (mercenaryScroll != null)
            mercenaryScroll.SetActive(isMercenaryActive);
    }

    // ==================== 유닛 목록 관리 ====================

    /// <summary>
    /// 모든 유닛 목록 갱신
    /// </summary>
    public void RefreshAllUnits()
    {
        ClearAllSlots();

        // UnitManager에서 유닛 가져오기
        if (UnitManager.Instance != null)
        {
            // 타입별로 유닛 추가
            foreach (var data in UnitManager.Instance.GetUnitDataByType(UnitType.Worker))
            {
                AddUnitSlot(data);
            }

            foreach (var data in UnitManager.Instance.GetUnitDataByType(UnitType.Fighter))
            {
                AddUnitSlot(data);
            }

            // 용병은 별도 타입이 있다면 추가
            // foreach (var data in UnitManager.Instance.GetUnitDataByType(UnitType.Mercenary))
            // {
            //     AddUnitSlot(data);
            // }
        }
        else
        {
            // UnitManager가 없으면 FindObjectsOfType 사용 (폴백)
            var allUnits = FindObjectsOfType<Unit>();
            foreach (var unit in allUnits)
            {
                if (unit == null || !unit.IsAlive) continue;
                AddUnitSlotLegacy(unit);
            }
        }

        // 레이아웃 강제 갱신
        Canvas.ForceUpdateCanvases();
        if (workerContent is RectTransform rt1) LayoutRebuilder.ForceRebuildLayoutImmediate(rt1);
        if (fighterContent is RectTransform rt2) LayoutRebuilder.ForceRebuildLayoutImmediate(rt2);
        if (mercenaryContent is RectTransform rt3) LayoutRebuilder.ForceRebuildLayoutImmediate(rt3);
    }

    /// <summary>
    /// 유닛 슬롯 추가 (UnitData 사용)
    /// </summary>
    private void AddUnitSlot(UnitData data)
    {
        if (data == null || data.Unit == null) return;

        Transform parent = GetContentForType(data.Unit.Type);
        if (parent == null || unitSlotBoxPrefab == null) return;

        GameObject slotObj = Instantiate(unitSlotBoxPrefab, parent);
        UnitSlotBox slotBox = slotObj.GetComponent<UnitSlotBox>();

        if (slotBox != null)
        {
            // UnitManager에서 정보 가져와서 초기화
            int currentDay = UnitManager.Instance?.GetCurrentDay() ?? 1;
            int activeDays = data.GetActiveDays(currentDay);

            slotBox.Initialize(data.Unit, data.JoinedWeek, activeDays, OnSlotClicked);
            GetSlotListForType(data.Unit.Type).Add(slotBox);
        }
    }

    /// <summary>
    /// 유닛 슬롯 추가 (레거시 - UnitManager 없을 때)
    /// </summary>
    private void AddUnitSlotLegacy(Unit unit)
    {
        Transform parent = GetContentForType(unit.Type);
        if (parent == null || unitSlotBoxPrefab == null) return;

        GameObject slotObj = Instantiate(unitSlotBoxPrefab, parent);
        UnitSlotBox slotBox = slotObj.GetComponent<UnitSlotBox>();

        if (slotBox != null)
        {
            slotBox.Initialize(unit, 1, 0, OnSlotClicked);
            GetSlotListForType(unit.Type).Add(slotBox);
        }
    }

    /// <summary>
    /// 특정 유닛 슬롯 제거
    /// </summary>
    public void RemoveUnitSlot(Unit unit)
    {
        var list = GetSlotListForType(unit.Type);
        UnitSlotBox toRemove = null;

        foreach (var slot in list)
        {
            if (slot.TargetUnit == unit)
            {
                toRemove = slot;
                break;
            }
        }

        if (toRemove != null)
        {
            list.Remove(toRemove);
            if (toRemove.gameObject != null)
                Destroy(toRemove.gameObject);
        }
    }

    /// <summary>
    /// 모든 슬롯 제거
    /// </summary>
    private void ClearAllSlots()
    {
        ClearSlotList(workerSlots, workerContent);
        ClearSlotList(fighterSlots, fighterContent);
        ClearSlotList(mercenarySlots, mercenaryContent);
    }

    private void ClearSlotList(List<UnitSlotBox> list, Transform content)
    {
        foreach (var slot in list)
        {
            if (slot != null && slot.gameObject != null)
                Destroy(slot.gameObject);
        }
        list.Clear();

        // Content 자식도 정리
        if (content != null)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }
        }
    }

    /// <summary>
    /// 유닛 타입별 Content 반환
    /// </summary>
    private Transform GetContentForType(UnitType type)
    {
        return type switch
        {
            UnitType.Worker => workerContent,
            UnitType.Fighter => fighterContent,
            _ => mercenaryContent  // 용병 등 기타
        };
    }

    /// <summary>
    /// 유닛 타입별 슬롯 리스트 반환
    /// </summary>
    private List<UnitSlotBox> GetSlotListForType(UnitType type)
    {
        return type switch
        {
            UnitType.Worker => workerSlots,
            UnitType.Fighter => fighterSlots,
            _ => mercenarySlots
        };
    }

    // ==================== 선택 ====================

    /// <summary>
    /// 슬롯 클릭 시
    /// </summary>
    private void OnSlotClicked(UnitSlotBox slot)
    {
        SelectSlot(slot);
    }

    /// <summary>
    /// 슬롯 선택
    /// </summary>
    public void SelectSlot(UnitSlotBox slot)
    {
        if (selectedSlot != null)
            selectedSlot.SetSelected(false);

        selectedSlot = slot;

        if (selectedSlot != null)
        {
            selectedSlot.SetSelected(true);
            OnUnitSelected?.Invoke(selectedSlot.TargetUnit);
        }
    }

    /// <summary>
    /// 선택 해제
    /// </summary>
    private void DeselectAll()
    {
        if (selectedSlot != null)
            selectedSlot.SetSelected(false);
        selectedSlot = null;
    }

    // ==================== 외부에서 유닛 추가/제거 시 ====================

    /// <summary>
    /// 유닛 추가 시 호출
    /// </summary>
    public void OnUnitAdded(Unit unit)
    {
        if (!isOpen) return;

        // UnitManager에서 데이터 가져오기
        UnitData data = UnitManager.Instance?.GetUnitData(unit);
        if (data != null)
        {
            AddUnitSlot(data);
        }
        else
        {
            AddUnitSlotLegacy(unit);
        }
    }

    /// <summary>
    /// 유닛 제거 시 호출
    /// </summary>
    public void OnUnitRemoved(Unit unit)
    {
        if (!isOpen) return;
        RemoveUnitSlot(unit);
    }

    /// <summary>
    /// 유닛 타입 변경 시 호출
    /// </summary>
    private void OnUnitTypeChanged(Unit unit, UnitType oldType, UnitType newType)
    {
        if (!isOpen) return;

        // 기존 슬롯 제거
        RemoveUnitSlotFromList(GetSlotListForType(oldType), unit);

        // 새 타입 리스트에 추가
        UnitData data = UnitManager.Instance?.GetUnitData(unit);
        if (data != null)
        {
            AddUnitSlot(data);
        }
    }

    /// <summary>
    /// 특정 리스트에서 유닛 슬롯 제거
    /// </summary>
    private void RemoveUnitSlotFromList(List<UnitSlotBox> list, Unit unit)
    {
        UnitSlotBox toRemove = null;
        foreach (var slot in list)
        {
            if (slot.TargetUnit == unit)
            {
                toRemove = slot;
                break;
            }
        }

        if (toRemove != null)
        {
            list.Remove(toRemove);
            if (toRemove.gameObject != null)
                Destroy(toRemove.gameObject);
        }
    }

    // ==================== 유틸리티 ====================

    /// <summary>
    /// 특정 유닛 타입의 카운트
    /// </summary>
    public int GetUnitCount(UnitType type)
    {
        return GetSlotListForType(type).Count;
    }

    /// <summary>
    /// 전체 유닛 카운트
    /// </summary>
    public int GetTotalUnitCount()
    {
        return workerSlots.Count + fighterSlots.Count + mercenarySlots.Count;
    }
}