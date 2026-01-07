using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 인벤토리 UI (Cult of the Lamb 스타일)
/// - 카테고리별 Box 동적 생성
/// - 아이템 개수에 따라 크기 자동 조절
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("=== UI References ===")]
    [SerializeField] private GameObject inventoryPanel;          // 전체 패널
    [SerializeField] private Transform contentParent;            // ScrollView의 Content
    [SerializeField] private InventoryInfoPanel infoPanel;       // 아이템 정보 패널 (우측)

    [Header("=== Prefabs ===")]
    [SerializeField] private GameObject categoryBoxPrefab;       // CategoryBox 프리팹
    [SerializeField] private GameObject itemSlotPrefab;          // ItemSlot 프리팹

    [Header("=== Animation ===")]
    [SerializeField] private float fadeInDuration = 0.2f;
    [SerializeField] private CanvasGroup canvasGroup;

    // 카테고리별 Box (Category -> CategoryBox)
    private Dictionary<ResourceCategory, InventoryCategoryBox> categoryBoxes = new Dictionary<ResourceCategory, InventoryCategoryBox>();

    // 전체 슬롯 (resourceID -> SlotUI)
    private Dictionary<int, InventorySlotUI> slotDict = new Dictionary<int, InventorySlotUI>();

    // 현재 선택된 슬롯
    private InventorySlotUI selectedSlot;
    private int selectedIndex = -1;
    private List<InventorySlotUI> allSlots = new List<InventorySlotUI>();

    private bool isOpen = false;

    public static InventoryUI Instance { get; private set; }
    public bool IsOpen => isOpen;

    private void Awake()
    {
        Instance = this;

        if (canvasGroup == null && inventoryPanel != null)
            canvasGroup = inventoryPanel.GetComponent<CanvasGroup>();

        // 초기 상태: 숨김
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);
    }

    private void Start()
    {
        // ResourceManager 이벤트 구독
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnInventoryChanged += RefreshUI;
            ResourceManager.Instance.OnResourceChanged += OnResourceChanged;
        }
    }

    private void OnDestroy()
    {
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnInventoryChanged -= RefreshUI;
            ResourceManager.Instance.OnResourceChanged -= OnResourceChanged;
        }
    }

    private void Update()
    {
        if (!isOpen) return;

        // 키보드 네비게이션
        HandleNavigation();
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

        inventoryPanel.SetActive(true);

        // UI 생성/갱신
        RefreshUI();

        // 첫 번째 아이템 선택
        if (allSlots.Count > 0)
        {
            SelectSlot(0);
        }

        // 페이드인
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.DOFade(1f, fadeInDuration).SetUpdate(true);
        }

        Debug.Log("[InventoryUI] 열림");
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        // 선택 해제
        DeselectAll();

        // 정보 패널 숨기기
        if (infoPanel != null)
            infoPanel.Hide();

        if (canvasGroup != null)
        {
            canvasGroup.DOFade(0f, fadeInDuration)
                .SetUpdate(true)
                .OnComplete(() => inventoryPanel.SetActive(false));
        }
        else
        {
            inventoryPanel.SetActive(false);
        }

        Debug.Log("[InventoryUI] 닫힘");
    }

    // ==================== UI 생성/갱신 ====================

    /// <summary>
    /// 전체 UI 갱신
    /// </summary>
    public void RefreshUI()
    {
        if (ResourceManager.Instance == null)
            return;

        // 기존 정리
        ClearAll();

        // 카테고리별로 아이템 분류
        var resources = ResourceManager.Instance.GetOwnedResources();
        var grouped = resources.GroupBy(r => r.Item.Category)
                               .OrderBy(g => g.Key);  // 카테고리 순서대로

        foreach (var group in grouped)
        {
            // 카테고리 Box 생성
            var categoryBox = CreateCategoryBox(group.Key);

            // 해당 카테고리의 아이템들 생성
            foreach (var stored in group.OrderBy(r => r.Item.ResourceName))
            {
                CreateItemSlot(categoryBox, stored);
            }
        }

        // Layout 갱신
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentParent as RectTransform);
    }

    /// <summary>
    /// 특정 자원만 갱신
    /// </summary>
    private void OnResourceChanged(int resourceID, int newAmount)
    {
        if (!isOpen) return;

        if (slotDict.TryGetValue(resourceID, out var slot))
        {
            if (newAmount <= 0)
            {
                // 0개가 되면 전체 갱신 (Box 정리 필요)
                RefreshUI();
            }
            else
            {
                slot.UpdateAmount(newAmount);
            }
        }
        else if (newAmount > 0)
        {
            // 새 아이템이면 전체 갱신
            RefreshUI();
        }
    }

    /// <summary>
    /// 카테고리 Box 생성
    /// </summary>
    private InventoryCategoryBox CreateCategoryBox(ResourceCategory category)
    {
        if (categoryBoxes.TryGetValue(category, out var existing))
            return existing;

        if (categoryBoxPrefab == null || contentParent == null)
            return null;

        GameObject boxObj = Instantiate(categoryBoxPrefab, contentParent);
        InventoryCategoryBox categoryBox = boxObj.GetComponent<InventoryCategoryBox>();

        if (categoryBox != null)
        {
            categoryBox.Initialize(category);
            categoryBoxes[category] = categoryBox;
        }

        return categoryBox;
    }

    /// <summary>
    /// 아이템 슬롯 생성
    /// </summary>
    private void CreateItemSlot(InventoryCategoryBox categoryBox, StoredResource stored)
    {
        if (itemSlotPrefab == null || categoryBox == null)
            return;

        GameObject slotObj = Instantiate(itemSlotPrefab, categoryBox.ItemContainer);
        InventorySlotUI slotUI = slotObj.GetComponent<InventorySlotUI>();

        if (slotUI != null)
        {
            int slotIndex = allSlots.Count;
            slotUI.Initialize(stored, slotIndex, OnSlotClicked, OnSlotHovered);

            slotDict[stored.Item.ID] = slotUI;
            allSlots.Add(slotUI);
        }
    }

    /// <summary>
    /// 전체 정리
    /// </summary>
    private void ClearAll()
    {
        foreach (var box in categoryBoxes.Values)
        {
            if (box != null)
                Destroy(box.gameObject);
        }
        categoryBoxes.Clear();
        slotDict.Clear();
        allSlots.Clear();
        selectedSlot = null;
        selectedIndex = -1;
    }

    // ==================== 선택/네비게이션 ====================

    private void HandleNavigation()
    {
        if (allSlots.Count == 0) return;

        // 방향키
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
        {
            SelectSlot(selectedIndex + 1);
        }
        else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
        {
            SelectSlot(selectedIndex - 1);
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
        {
            SelectSlot(selectedIndex + GetColumnsPerRow());
        }
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
        {
            SelectSlot(selectedIndex - GetColumnsPerRow());
        }
    }

    private int GetColumnsPerRow()
    {
        // GridLayoutGroup의 컬럼 수 추정 (기본 4)
        return 4;
    }

    private void SelectSlot(int index)
    {
        if (allSlots.Count == 0) return;

        index = Mathf.Clamp(index, 0, allSlots.Count - 1);

        if (index == selectedIndex) return;

        // 이전 선택 해제
        if (selectedSlot != null)
            selectedSlot.SetSelected(false);

        selectedIndex = index;
        selectedSlot = allSlots[index];
        selectedSlot.SetSelected(true);

        // 정보 패널 업데이트
        UpdateInfoPanel();
    }

    private void DeselectAll()
    {
        if (selectedSlot != null)
            selectedSlot.SetSelected(false);

        selectedSlot = null;
        selectedIndex = -1;
    }

    // ==================== 콜백 ====================

    private void OnSlotClicked(InventorySlotUI slot)
    {
        int index = allSlots.IndexOf(slot);
        if (index >= 0)
        {
            SelectSlot(index);
        }
    }

    private void OnSlotHovered(InventorySlotUI slot)
    {
        int index = allSlots.IndexOf(slot);
        if (index >= 0)
        {
            SelectSlot(index);
        }
    }

    // ==================== 정보 패널 ====================

    private void UpdateInfoPanel()
    {
        if (infoPanel == null || selectedSlot == null)
            return;

        infoPanel.ShowItem(selectedSlot.StoredResource);
    }
}