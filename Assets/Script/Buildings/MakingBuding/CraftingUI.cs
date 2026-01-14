using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// 제작/요리 UI (InfoCard 통합 버전)
/// 
/// 구조:
/// - 레시피 목록 (스크롤)
/// - 대기열 (6칸)
/// - 상세 카드 (내부 통합)
/// </summary>
public class CraftingUI : MonoBehaviour, IEscapableUI
{
    public static CraftingUI Instance { get; private set; }

    [Header("=== 메인 패널 ===")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private RectTransform panelRect;

    [Header("=== 레시피 영역 ===")]
    [SerializeField] private Transform recipeContent;
    [SerializeField] private GameObject slotPrefab;

    [Header("=== 대기열 영역 ===")]
    [SerializeField] private Transform queueContent;
    [SerializeField] private int maxQueueSlots = 6;

    [Header("=== 상세 카드 (InventoryCard) ===")]
    [SerializeField] private RectTransform cardRect;
    [SerializeField] private Image cardIcon;
    [SerializeField] private TextMeshProUGUI cardName;
    [SerializeField] private TextMeshProUGUI cardDescription;
    [SerializeField] private TextMeshProUGUI cardCraftTime;
    [SerializeField] private Transform materialContainer;     // 재료 표시 영역
    [SerializeField] private GameObject materialPrefab;       // 재료 Prefab (Icon + 수량)
    [SerializeField] private TextMeshProUGUI hungerText;      // 포만감
    [SerializeField] private TextMeshProUGUI mentalText;      // 정신력

    [Header("=== 애니메이션 ===")]
    [SerializeField] private float panelClosedX = -1400f;
    [SerializeField] private float panelOpenedX = 0f;
    [SerializeField] private float cardClosedX = 600f;
    [SerializeField] private float cardOpenedX = 0f;
    [SerializeField] private float slideDuration = 0.4f;

    [Header("=== 게임 속도 ===")]
    [SerializeField] private float slowTimeScale = 0.05f;

    [Header("=== ESC 우선순위 ===")]
    [SerializeField] private int escapePriority = 90;

    // 상태
    private bool isOpen;
    private CraftingBuildingComponent currentBuilding;

    // 슬롯 관리
    private readonly List<CraftingSlotUI> recipeSlots = new();
    private readonly List<CraftingSlotUI> queueSlots = new();
    private CraftingSlotUI selectedSlot;

    // 카드 재료 UI
    private readonly List<GameObject> materialUIs = new();

    // Tweens
    private Tweener panelTween, cardTween;

    // 이벤트
    public event Action OnPanelOpened;
    public event Action OnPanelClosed;

    // Properties
    public bool IsOpen => isOpen;
    public int EscapePriority => escapePriority;

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        InitializeClosedState();
        CreateQueueSlots();
    }

    private void Start()
    {
        GameInputManager.Instance?.RegisterEscapableUI(this);
    }

    private void OnDestroy()
    {
        KillTweens();
        Time.timeScale = 1f;
        GameInputManager.Instance?.UnregisterEscapableUI(this);
        if (Instance == this) Instance = null;
    }

    private void InitializeClosedState()
    {
        if (panelRect != null)
            panelRect.anchoredPosition = new Vector2(panelClosedX, panelRect.anchoredPosition.y);
        if (cardRect != null)
            cardRect.anchoredPosition = new Vector2(cardClosedX, cardRect.anchoredPosition.y);

        mainPanel?.SetActive(false);
        cardRect?.gameObject.SetActive(false);
    }

    private void CreateQueueSlots()
    {
        if (queueContent == null || slotPrefab == null) return;

        for (int i = 0; i < maxQueueSlots; i++)
        {
            var go = Instantiate(slotPrefab, queueContent);
            if (go.TryGetComponent<CraftingSlotUI>(out var slot))
            {
                slot.InitializeAsQueue(i, OnQueueSlotClicked);
                queueSlots.Add(slot);
            }
        }
    }

    #endregion

    #region Open / Close

    public void Open(CraftingBuildingComponent building)
    {
        if (building == null) return;
        if (isOpen) Close();

        currentBuilding = building;
        isOpen = true;

        KillTweens();
        mainPanel?.SetActive(true);

        // 이벤트 구독
        currentBuilding.OnQueueChanged += RefreshQueueUI;
        currentBuilding.OnCraftingProgress += OnCraftingProgress;

        // UI 갱신
        RefreshRecipeUI();
        RefreshQueueUI();

        // 애니메이션
        panelTween = panelRect?.DOAnchorPosX(panelOpenedX, slideDuration)
            .SetEase(Ease.OutQuart).SetUpdate(true);

        Time.timeScale = slowTimeScale;
        OnPanelOpened?.Invoke();

        Debug.Log($"[CraftingUI] Open - {building.name}");
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        KillTweens();
        DeselectRecipe();

        // 이벤트 해제
        if (currentBuilding != null)
        {
            currentBuilding.OnQueueChanged -= RefreshQueueUI;
            currentBuilding.OnCraftingProgress -= OnCraftingProgress;
        }

        // 카드 닫기
        if (cardRect != null && cardRect.gameObject.activeSelf)
        {
            cardTween = cardRect.DOAnchorPosX(cardClosedX, slideDuration * 0.7f)
                .SetEase(Ease.InBack).SetUpdate(true)
                .OnComplete(() => cardRect.gameObject.SetActive(false));
        }

        // 패널 닫기
        panelTween = panelRect?.DOAnchorPosX(panelClosedX, slideDuration)
            .SetEase(Ease.OutQuart).SetUpdate(true)
            .OnComplete(() => mainPanel?.SetActive(false));

        Time.timeScale = 1f;
        currentBuilding = null;
        OnPanelClosed?.Invoke();

        Debug.Log("[CraftingUI] Close");
    }

    public void Toggle(CraftingBuildingComponent building)
    {
        if (isOpen && currentBuilding == building)
            Close();
        else
            Open(building);
    }

    private void KillTweens()
    {
        panelTween?.Kill();
        cardTween?.Kill();
    }

    #endregion

    #region Recipe UI

    private void RefreshRecipeUI()
    {
        ClearRecipeSlots();
        if (currentBuilding == null) return;

        foreach (var recipe in currentBuilding.AvailableRecipes)
        {
            var go = Instantiate(slotPrefab, recipeContent);
            if (go.TryGetComponent<CraftingSlotUI>(out var slot))
            {
                slot.InitializeAsRecipe(recipe, OnRecipeSlotClicked);
                recipeSlots.Add(slot);
            }
        }
    }

    private void ClearRecipeSlots()
    {
        foreach (var slot in recipeSlots)
            if (slot != null) Destroy(slot.gameObject);
        recipeSlots.Clear();
        selectedSlot = null;
    }

    private void OnRecipeSlotClicked(CraftingSlotUI slot)
    {
        if (slot == null) return;

        // 이미 선택된 슬롯 다시 클릭 → 대기열 추가
        if (selectedSlot == slot)
        {
            TryAddToQueue(slot.Recipe);
            return;
        }

        // 새 슬롯 선택
        SelectRecipe(slot);
    }

    private void SelectRecipe(CraftingSlotUI slot)
    {
        selectedSlot?.SetSelected(false);
        selectedSlot = slot;
        selectedSlot.SetSelected(true);
        ShowCard(slot.Recipe);
    }

    private void DeselectRecipe()
    {
        selectedSlot?.SetSelected(false);
        selectedSlot = null;
    }

    private void TryAddToQueue(RecipeSO recipe)
    {
        if (currentBuilding == null || recipe == null) return;
        if (!currentBuilding.HasQueueSpace || !recipe.CanCraft()) return;

        if (currentBuilding.AddToQueue(recipe))
        {
            // 재료 소모 후 UI 갱신
            RefreshRecipeUI();
            RefreshCardMaterials(recipe);
        }
    }

    #endregion

    #region Queue UI

    private void RefreshQueueUI()
    {
        if (currentBuilding == null) return;

        var queue = currentBuilding.Queue;
        for (int i = 0; i < queueSlots.Count; i++)
            queueSlots[i].SetQueueItem(i < queue.Count ? queue[i] : null);
    }

    private void OnQueueSlotClicked(CraftingSlotUI slot)
    {
        if (slot == null || currentBuilding == null) return;

        if (currentBuilding.RemoveFromQueue(slot.SlotIndex))
        {
            // 재료 환불 후 UI 갱신
            RefreshRecipeUI();
            if (selectedSlot != null)
                RefreshCardMaterials(selectedSlot.Recipe);
        }
    }

    private void OnCraftingProgress(CraftingQueueItem item)
    {
        if (queueSlots.Count > 0)
            queueSlots[0].UpdateProgress(item.Progress);
    }

    #endregion

    #region Info Card (내부 통합)

    private void ShowCard(RecipeSO recipe)
    {
        if (cardRect == null || recipe == null) return;

        cardRect.gameObject.SetActive(true);

        // 기본 정보
        if (cardIcon != null)
        {
            cardIcon.sprite = recipe.Icon;
            cardIcon.enabled = recipe.Icon != null;
        }

        if (cardName != null)
            cardName.text = recipe.RecipeName;

        if (cardDescription != null)
            cardDescription.text = recipe.Description;

        if (cardCraftTime != null)
            cardCraftTime.text = $"{recipe.CraftingTime:F1}초";

        // 재료 목록
        RefreshCardMaterials(recipe);

        // 음식 정보
        RefreshFoodInfo(recipe);

        // 애니메이션
        cardTween?.Kill();
        cardTween = cardRect.DOAnchorPosX(cardOpenedX, slideDuration * 0.7f)
            .SetEase(Ease.OutBack).SetUpdate(true);
    }

    private void RefreshCardMaterials(RecipeSO recipe)
    {
        // 기존 재료 UI 정리
        foreach (var ui in materialUIs)
            if (ui != null) Destroy(ui);
        materialUIs.Clear();

        if (materialContainer == null || materialPrefab == null || recipe?.Ingredients == null)
            return;

        // 새 재료 UI 생성
        foreach (var ing in recipe.Ingredients)
        {
            if (ing.Item == null) continue;

            var go = Instantiate(materialPrefab, materialContainer);
            materialUIs.Add(go);

            // MaterialDisplay 내부 구조: Image, UseCount_Tx, MY_Assete_Tx
            var icon = go.transform.Find("Image")?.GetComponent<Image>();
            var requiredText = go.transform.Find("UseCount_Tx")?.GetComponent<TextMeshProUGUI>();
            var ownedText = go.transform.Find("MY_Assete_Tx")?.GetComponent<TextMeshProUGUI>();

            if (icon != null)
            {
                icon.sprite = ing.Item.Icon;
                icon.enabled = ing.Item.Icon != null;
            }

            int owned = ResourceManager.Instance?.GetResourceAmount(ing.Item.ID) ?? 0;
            bool sufficient = owned >= ing.Amount;

            if (requiredText != null)
                requiredText.text = ing.Amount.ToString();

            if (ownedText != null)
            {
                ownedText.text = $"({owned})";
                ownedText.color = sufficient ? Color.white : new Color(1f, 0.5f, 0.5f);
            }

            if (icon != null)
                icon.color = sufficient ? Color.white : new Color(1f, 1f, 1f, 0.6f);
        }
    }

    private void RefreshFoodInfo(RecipeSO recipe)
    {
        bool isCooking = recipe.Category == RecipeCategory.Cooking;

        // 음식 정보 패널 (hungerText, mentalText의 부모)
        var foodPanel = hungerText?.transform.parent?.gameObject;
        if (foodPanel != null)
            foodPanel.SetActive(isCooking);

        if (!isCooking) return;

        // 결과물에서 음식 정보 가져오기
        var output = recipe.Outputs?.Length > 0 ? recipe.Outputs[0]?.Item : null;
        if (output != null && output.IsFood)
        {
            if (hungerText != null)
                hungerText.text = output.NutritionValue.ToString("F0");
            if (mentalText != null)
                mentalText.text = output.HealthRestore.ToString("F0");
        }
    }

    #endregion
}