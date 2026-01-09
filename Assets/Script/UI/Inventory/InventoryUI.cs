using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DG.Tweening;

/// <summary>
/// 인벤토리 UI
/// - IEscapableUI 구현: GameInputManager 연동
/// - DOTween 슬라이드 애니메이션 + 시간 느려짐 + Depth of Field 블러
/// - ★ I키로 열기/닫기 (토글)
/// </summary>
public class InventoryUI : MonoBehaviour, IEscapableUI
{
    [Header("=== UI References ===")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private Transform contentParent;
    [SerializeField] private InventoryInfoPanel infoPanel;
    private RectTransform panelRectTransform;

    [Header("=== Prefabs ===")]
    [SerializeField] private GameObject categoryBoxPrefab;
    [SerializeField] private GameObject itemSlotPrefab;

    [Header("=== Post Processing ===")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private float blurFocalLengthMin = 0f;
    [SerializeField] private float blurFocalLengthMax = 50f;

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float closedPosX = -1400f;
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

    // Depth of Field 참조
    private DepthOfField depthOfField;

    // 트윈
    private Tweener panelTween;
    private Tweener blurTween;

    // 데이터 관리
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

        // ★ GameInputManager에 등록
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.RegisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered += HandleGameAction;
            Debug.Log("[InventoryUI] GameInputManager에 등록됨");
        }
    }

    private void OnDestroy()
    {
        KillAllTweens();

        // ★ TimeScale 복구 (중요!)
        Time.timeScale = normalTimeScale;

        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnInventoryChanged -= RefreshUI;
            ResourceManager.Instance.OnResourceChanged -= OnResourceChanged;
        }

        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.UnregisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered -= HandleGameAction;
        }
    }

    private void Update()
    {
        if (!isOpen) return;
        HandleNavigation();
    }

    // ==================== 입력 처리 ====================

    private void HandleGameAction(GameAction action)
    {
        switch (action)
        {
            case GameAction.OpenInventory:
                // 다른 UI가 열려있으면 무시
                if (BuildingUIManager.Instance != null && BuildingUIManager.Instance.IsOpen)
                    return;
                if (ChatPanelController.Instance != null && ChatPanelController.Instance.IsOpen)
                    return;

                Toggle();
                break;
        }
    }

    // ==================== 초기화 ====================

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

        Debug.Log("[InventoryUI] Open() 호출됨");

        KillAllTweens();
        inventoryPanel.SetActive(true);
        RefreshUI();

        // 첫 번째 아이템 자동 선택
        if (allSlots.Count > 0) SelectSlot(0);

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

        // ★ 3. TimeScale 즉시 설정
        Time.timeScale = slowTimeScale;
        Debug.Log($"[InventoryUI] TimeScale 설정: {slowTimeScale}");

        OnPanelOpened?.Invoke();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        Debug.Log("[InventoryUI] Close() 호출됨");

        KillAllTweens();
        DeselectAll();
        if (infoPanel != null) infoPanel.Hide();

        // 1. 패널 슬라이드
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

        // ★ 3. TimeScale 즉시 복구
        Time.timeScale = normalTimeScale;
        Debug.Log($"[InventoryUI] TimeScale 복구: {normalTimeScale}");

        OnPanelClosed?.Invoke();
    }

    private void KillAllTweens()
    {
        panelTween?.Kill();
        blurTween?.Kill();
    }

    // ==================== 데이터 및 슬롯 관리 ====================

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
        int col = 4;
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) SelectSlot(selectedIndex + 1);
        else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) SelectSlot(selectedIndex - 1);
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) SelectSlot(selectedIndex + col);
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) SelectSlot(selectedIndex - col);
    }

    private void OnSlotClicked(InventorySlotUI slot) => SelectSlot(allSlots.IndexOf(slot));
    private void OnSlotHovered(InventorySlotUI slot) => SelectSlot(allSlots.IndexOf(slot));
}