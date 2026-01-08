using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DG.Tweening;

/// <summary>
/// 인벤토리 UI (BuildingUIManager 스타일)
/// - IEscapableUI 구현: GameInputManager 연동
/// - DOTween 슬라이드 애니메이션 + 시간 느려짐 + Depth of Field 블러
/// </summary>
public class InventoryUI : MonoBehaviour, IEscapableUI
{
    [Header("=== UI References ===")]
    [SerializeField] private GameObject inventoryPanel;          // 전체 패널
    [SerializeField] private Transform contentParent;            // ScrollView의 Content
    [SerializeField] private InventoryInfoPanel infoPanel;       // 아이템 정보 패널 (우측)
    private RectTransform panelRectTransform;

    [Header("=== Prefabs ===")]
    [SerializeField] private GameObject categoryBoxPrefab;       // CategoryBox 프리팹
    [SerializeField] private GameObject itemSlotPrefab;          // ItemSlot 프리팹

    [Header("=== Post Processing ===")]
    [SerializeField] private Volume globalVolume;                // Global Volume 참조
    [SerializeField] private float blurFocalLengthMin = 0f;      // 닫힌 상태 (블러 없음)
    [SerializeField] private float blurFocalLengthMax = 50f;     // 열린 상태 (블러 최대)

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float closedPosX = -1400f;          // 닫힌 상태 X 위치 (왼쪽 밖)
    [SerializeField] private float openedPosX = 0f;              // 열린 상태 X 위치
    [SerializeField] private float slideDuration = 0.4f;         // 슬라이드 애니메이션 시간
    [SerializeField] private Ease slideEase = Ease.OutQuart;     // 슬라이드 이징

    [Header("=== 게임 속도 설정 ===")]
    [SerializeField] private float slowTimeScale = 0.05f;        // 열렸을 때 게임 속도
    [SerializeField] private float normalTimeScale = 1f;         // 닫혔을 때 게임 속도
    [SerializeField] private float timeScaleDuration = 0.3f;     // 속도 변경 시간

    [Header("=== ESC 우선순위 ===")]
    [Tooltip("높을수록 ESC 시 먼저 닫힘 (건물UI: 90, 인벤토리: 80)")]
    [SerializeField] private int escapePriority = 80;            // 인벤토리는 건물창보다 낮은 우선순위

    [Header("=== 상태 ===")]
    [SerializeField] private bool isOpen = false;

    // Depth of Field 참조
    private DepthOfField depthOfField;

    // 현재 진행 중인 트윈
    private Tweener panelTween;
    private Tweener blurTween;
    private Tweener timeTween;

    // 데이터 관리 (기본 로직 유지)
    private Dictionary<ResourceCategory, InventoryCategoryBox> categoryBoxes = new Dictionary<ResourceCategory, InventoryCategoryBox>();
    private Dictionary<int, InventorySlotUI> slotDict = new Dictionary<int, InventorySlotUI>();
    private List<InventorySlotUI> allSlots = new List<InventorySlotUI>();
    private InventorySlotUI selectedSlot;
    private int selectedIndex = -1;

    // 이벤트
    public event Action OnPanelOpened;
    public event Action OnPanelClosed;

    // ==================== IEscapableUI 구현 ====================
    public bool IsOpen => isOpen;
    public int EscapePriority => escapePriority;

    public static InventoryUI Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        if (inventoryPanel != null)
            panelRectTransform = inventoryPanel.GetComponent<RectTransform>();

        InitializeDepthOfField();
        InitializeClosedState();
    }

    private void Start()
    {
        // ResourceManager 이벤트 구독
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnInventoryChanged += RefreshUI;
            ResourceManager.Instance.OnResourceChanged += OnResourceChanged;
        }

        // GameInputManager에 등록
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.RegisterEscapableUI(this);
        }
    }

    private void OnDestroy()
    {
        KillAllTweens();
        Time.timeScale = normalTimeScale;

        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnInventoryChanged -= RefreshUI;
            ResourceManager.Instance.OnResourceChanged -= OnResourceChanged;
        }

        if (GameInputManager.Instance != null)
            GameInputManager.Instance.UnregisterEscapableUI(this);
    }

    private void Update()
    {
        HandleInputFallback();

        if (!isOpen) return;
        HandleNavigation();
    }

    // ==================== 초기화 및 입력 처리 ====================

    private void InitializeDepthOfField()
    {
        if (globalVolume != null && globalVolume.profile != null)
        {
            if (!globalVolume.profile.TryGet(out depthOfField))
                Debug.LogWarning("[InventoryUI] Depth of Field가 Volume에 없습니다.");
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
        inventoryPanel.SetActive(false);
        isOpen = false;
    }

    private void HandleInputFallback()
    {
        // 'I' 키로 인벤토리 토글 (BuildingUI가 닫혀있을 때만)
        if (Input.GetKeyDown(KeyCode.I))
        {
            if (BuildingUIManager.Instance != null && BuildingUIManager.Instance.IsOpen) return;
            Toggle();
        }
    }

    // ==================== 열기/닫기 (BuildingUIManager 스타일) ====================

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
        inventoryPanel.SetActive(true);
        RefreshUI();

        // 첫 번째 아이템 자동 선택
        if (allSlots.Count > 0) SelectSlot(0);

        // 1. 패널 슬라이드 (왼쪽에서 들어옴)
        panelTween = panelRectTransform
            .DOAnchorPosX(openedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true); // 일시정지 중에도 작동

        // 2. Depth of Field 블러
        if (depthOfField != null)
        {
            blurTween = DOTween.To(() => depthOfField.focalLength.value,
                x => depthOfField.focalLength.value = x, blurFocalLengthMax, slideDuration)
                .SetUpdate(true);
        }

        // 3. 게임 속도 감소
        timeTween = DOTween.To(() => Time.timeScale, x => Time.timeScale = x, slowTimeScale, timeScaleDuration)
            .SetUpdate(true);

        OnPanelOpened?.Invoke();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        KillAllTweens();
        DeselectAll();
        if (infoPanel != null) infoPanel.Hide();

        // 1. 패널 슬라이드 (왼쪽 밖으로 나감)
        panelTween = panelRectTransform
            .DOAnchorPosX(closedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true)
            .OnComplete(() => inventoryPanel.SetActive(false));

        // 2. 블러 해제
        if (depthOfField != null)
        {
            blurTween = DOTween.To(() => depthOfField.focalLength.value,
                x => depthOfField.focalLength.value = x, blurFocalLengthMin, slideDuration)
                .SetUpdate(true);
        }

        // 3. 게임 속도 복구
        timeTween = DOTween.To(() => Time.timeScale, x => Time.timeScale = x, normalTimeScale, timeScaleDuration)
            .SetUpdate(true);

        OnPanelClosed?.Invoke();
    }

    private void KillAllTweens()
    {
        panelTween?.Kill();
        blurTween?.Kill();
        timeTween?.Kill();
    }

    // ==================== 데이터 및 슬롯 관리 (기존 로직 최적화) ====================

    public void RefreshUI()
    {
        if (ResourceManager.Instance == null) return;

        ClearAll();
        var resources = ResourceManager.Instance.GetOwnedResources();
        var grouped = resources.GroupBy(r => r.Item.Category).OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var categoryBox = CreateCategoryBox(group.Key);
            foreach (var stored in group.OrderBy(r => r.Item.ResourceName))
            {
                CreateItemSlot(categoryBox, stored);
            }
        }

        Canvas.ForceUpdateCanvases();
        if (contentParent is RectTransform rt) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    private void SelectSlot(int index)
    {
        if (allSlots.Count == 0) return;
        index = Mathf.Clamp(index, 0, allSlots.Count - 1);
        if (index == selectedIndex) return;

        if (selectedSlot != null) selectedSlot.SetSelected(false);

        selectedIndex = index;
        selectedSlot = allSlots[index];
        selectedSlot.SetSelected(true);

        UpdateInfoPanel();
    }

    private void DeselectAll()
    {
        if (selectedSlot != null) selectedSlot.SetSelected(false);
        selectedSlot = null;
        selectedIndex = -1;
    }

    private void UpdateInfoPanel()
    {
        if (infoPanel != null && selectedSlot != null)
            infoPanel.ShowItem(selectedSlot.StoredResource);
    }

    private InventoryCategoryBox CreateCategoryBox(ResourceCategory category)
    {
        if (categoryBoxes.TryGetValue(category, out var existing)) return existing;
        GameObject boxObj = Instantiate(categoryBoxPrefab, contentParent);
        InventoryCategoryBox categoryBox = boxObj.GetComponent<InventoryCategoryBox>();
        categoryBox.Initialize(category);
        categoryBoxes[category] = categoryBox;
        return categoryBox;
    }

    private void CreateItemSlot(InventoryCategoryBox categoryBox, StoredResource stored)
    {
        GameObject slotObj = Instantiate(itemSlotPrefab, categoryBox.ItemContainer);
        InventorySlotUI slotUI = slotObj.GetComponent<InventorySlotUI>();
        slotUI.Initialize(stored, allSlots.Count, OnSlotClicked, OnSlotHovered);
        slotDict[stored.Item.ID] = slotUI;
        allSlots.Add(slotUI);
    }

    private void ClearAll()
    {
        foreach (var box in categoryBoxes.Values) if (box != null) Destroy(box.gameObject);
        categoryBoxes.Clear();
        slotDict.Clear();
        allSlots.Clear();
        selectedSlot = null;
        selectedIndex = -1;
    }

    private void OnResourceChanged(int resourceID, int newAmount)
    {
        if (!isOpen) return;
        if (slotDict.TryGetValue(resourceID, out var slot))
        {
            if (newAmount <= 0) RefreshUI();
            else slot.UpdateAmount(newAmount);
        }
        else if (newAmount > 0) RefreshUI();
    }

    private void HandleNavigation()
    {
        if (allSlots.Count == 0) return;
        int col = 4; // 그리드 열 수
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) SelectSlot(selectedIndex + 1);
        else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) SelectSlot(selectedIndex - 1);
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) SelectSlot(selectedIndex + col);
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) SelectSlot(selectedIndex - col);
    }

    private void OnSlotClicked(InventorySlotUI slot) => SelectSlot(allSlots.IndexOf(slot));
    private void OnSlotHovered(InventorySlotUI slot) => SelectSlot(allSlots.IndexOf(slot));
}