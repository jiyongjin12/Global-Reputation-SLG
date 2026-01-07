using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 인벤토리 UI (ResourceManager 데이터 표시)
/// - 그리드 형태로 보유 자원 표시
/// - 자원 변경 시 자동 갱신
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("=== UI References ===")]
    [SerializeField] private GameObject inventoryPanel;          // 인벤토리 패널
    [SerializeField] private Transform slotContainer;            // 슬롯들이 들어갈 컨테이너
    [SerializeField] private GameObject slotPrefab;              // InventorySlotUI 프리팹

    [Header("=== Settings ===")]
    [SerializeField] private bool showEmptySlots = false;        // 0개인 자원도 표시할지
    [SerializeField] private SortType sortType = SortType.Category;

    [Header("=== Animation ===")]
    [SerializeField] private float fadeInDuration = 0.2f;
    [SerializeField] private CanvasGroup canvasGroup;

    public enum SortType
    {
        Category,       // 카테고리순
        Name,           // 이름순
        Amount          // 수량순 (많은 순)
    }

    // 생성된 슬롯들 (resourceID -> SlotUI)
    private Dictionary<int, InventorySlotUI> slotDict = new Dictionary<int, InventorySlotUI>();
    private List<InventorySlotUI> allSlots = new List<InventorySlotUI>();

    private bool isOpen = false;

    public static InventoryUI Instance { get; private set; }

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
        // 이벤트 해제
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.OnInventoryChanged -= RefreshUI;
            ResourceManager.Instance.OnResourceChanged -= OnResourceChanged;
        }
    }

    private void Update()
    {
        // I키로 열기/닫기 (임시 - 나중에 InputManager로 이동)
        if (Input.GetKeyDown(KeyCode.I))
        {
            Toggle();
        }
    }

    // ==================== 열기/닫기 ====================

    public void Toggle()
    {
        if (isOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;

        inventoryPanel.SetActive(true);

        // UI 갱신
        RefreshUI();

        // 페이드인 애니메이션
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

    // ==================== UI 갱신 ====================

    /// <summary>
    /// 전체 UI 갱신
    /// </summary>
    public void RefreshUI()
    {
        if (ResourceManager.Instance == null)
            return;

        // 정렬된 자원 목록 가져오기
        List<StoredResource> resources = GetSortedResources();

        // 기존 슬롯 정리
        ClearAllSlots();

        // 슬롯 생성
        foreach (var stored in resources)
        {
            if (!showEmptySlots && stored.Amount <= 0)
                continue;

            CreateOrUpdateSlot(stored);
        }
    }

    /// <summary>
    /// 특정 자원만 갱신 (최적화)
    /// </summary>
    private void OnResourceChanged(int resourceID, int newAmount)
    {
        if (!isOpen) return;

        // 해당 슬롯만 업데이트
        if (slotDict.TryGetValue(resourceID, out var slot))
        {
            if (newAmount <= 0 && !showEmptySlots)
            {
                // 0개가 되면 슬롯 제거
                RemoveSlot(resourceID);
            }
            else
            {
                slot.UpdateAmount(newAmount);
            }
        }
        else if (newAmount > 0)
        {
            // 새 자원이면 전체 갱신 (정렬 때문에)
            RefreshUI();
        }
    }

    /// <summary>
    /// 정렬된 자원 목록 가져오기
    /// </summary>
    private List<StoredResource> GetSortedResources()
    {
        var rm = ResourceManager.Instance;

        switch (sortType)
        {
            case SortType.Category:
                return rm.GetResourcesSortedByCategoryThenName();
            case SortType.Name:
                return rm.GetResourcesSortedByName();
            case SortType.Amount:
                return rm.GetResourcesSortedByAmount();
            default:
                return rm.GetOwnedResources();
        }
    }

    // ==================== 슬롯 관리 ====================

    private void CreateOrUpdateSlot(StoredResource stored)
    {
        if (slotDict.TryGetValue(stored.Item.ID, out var existingSlot))
        {
            existingSlot.UpdateAmount(stored.Amount);
            return;
        }

        // 새 슬롯 생성
        if (slotPrefab == null || slotContainer == null)
            return;

        GameObject slotObj = Instantiate(slotPrefab, slotContainer);
        InventorySlotUI slotUI = slotObj.GetComponent<InventorySlotUI>();

        if (slotUI != null)
        {
            slotUI.Initialize(stored);
            slotDict[stored.Item.ID] = slotUI;
            allSlots.Add(slotUI);
        }
    }

    private void RemoveSlot(int resourceID)
    {
        if (slotDict.TryGetValue(resourceID, out var slot))
        {
            allSlots.Remove(slot);
            slotDict.Remove(resourceID);
            Destroy(slot.gameObject);
        }
    }

    private void ClearAllSlots()
    {
        foreach (var slot in allSlots)
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
        allSlots.Clear();
        slotDict.Clear();
    }

    // ==================== 정렬 변경 ====================

    public void SetSortType(SortType type)
    {
        sortType = type;
        if (isOpen)
            RefreshUI();
    }

    public void CycleSortType()
    {
        sortType = (SortType)(((int)sortType + 1) % 3);
        if (isOpen)
            RefreshUI();
    }
}