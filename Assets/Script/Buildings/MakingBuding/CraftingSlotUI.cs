using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 통합 슬롯 UI (레시피/대기열 겸용)
/// - 모드에 따라 다르게 동작
/// </summary>
public class CraftingSlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public enum SlotMode { Recipe, Queue }

    [Header("=== UI References ===")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TextMeshProUGUI countText;        // 레시피: 제작가능 수 / 대기열: 사용안함
    [SerializeField] private Image progressFill;               // 대기열 전용: 진행도
    [SerializeField] private GameObject selectedOutline;       // 레시피 전용: 선택 표시
    [SerializeField] private GameObject processingIndicator;   // 대기열 전용: 제작중 표시

    [Header("=== 색상 ===")]
    [SerializeField] private Color normalColor = new(0.2f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color hoverColor = new(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color selectedColor = new(0.4f, 0.4f, 0.2f, 1f);
    [SerializeField] private Color disabledColor = new(0.15f, 0.15f, 0.15f, 0.5f);
    [SerializeField] private Color processingColor = new(0.2f, 0.4f, 0.2f, 1f);

    // 상태
    private SlotMode mode;
    private int slotIndex;
    private bool isSelected;
    private bool isEmpty = true;

    // 레시피 모드
    private RecipeSO recipe;
    private int craftableCount;
    private Action<CraftingSlotUI> onRecipeClicked;

    // 대기열 모드
    private CraftingQueueItem queueItem;
    private Action<CraftingSlotUI> onQueueClicked;

    // Properties
    public SlotMode Mode => mode;
    public RecipeSO Recipe => recipe;
    public int SlotIndex => slotIndex;
    public bool IsEmpty => isEmpty;
    private bool CanCraft => craftableCount > 0;
    private bool IsProcessing => queueItem?.IsProcessing == true;

    #region Initialization

    /// <summary>
    /// 레시피 슬롯으로 초기화
    /// </summary>
    public void InitializeAsRecipe(RecipeSO recipeData, Action<CraftingSlotUI> clickCallback)
    {
        mode = SlotMode.Recipe;
        recipe = recipeData;
        onRecipeClicked = clickCallback;
        isSelected = false;
        isEmpty = false;

        // 대기열 전용 UI 숨기기
        if (progressFill != null) progressFill.gameObject.SetActive(false);
        if (processingIndicator != null) processingIndicator.SetActive(false);

        // 레시피 전용 UI 보이기
        if (countText != null) countText.gameObject.SetActive(true);
        if (selectedOutline != null) selectedOutline.SetActive(false);

        UpdateRecipeDisplay();
    }

    /// <summary>
    /// 대기열 슬롯으로 초기화
    /// </summary>
    public void InitializeAsQueue(int index, Action<CraftingSlotUI> clickCallback)
    {
        mode = SlotMode.Queue;
        slotIndex = index;
        onQueueClicked = clickCallback;
        isEmpty = true;

        // 레시피 전용 UI 숨기기
        if (countText != null) countText.gameObject.SetActive(false);
        if (selectedOutline != null) selectedOutline.SetActive(false);

        // 대기열 전용 UI 보이기
        if (progressFill != null)
        {
            progressFill.gameObject.SetActive(true);
            progressFill.fillAmount = 0f;
        }

        Clear();
    }

    #endregion

    #region Recipe Mode

    public void UpdateRecipeDisplay()
    {
        if (mode != SlotMode.Recipe || recipe == null) return;

        // 아이콘
        SetIcon(recipe.Icon);

        // 제작 가능 개수
        craftableCount = CalculateCraftableCount();

        if (countText != null)
        {
            countText.text = craftableCount.ToString();
            countText.color = CanCraft ? Color.white : Color.red;
        }

        // 아이콘 색상
        if (iconImage != null)
            iconImage.color = CanCraft ? Color.white : new Color(1f, 1f, 1f, 0.5f);

        UpdateBackgroundColor();
    }

    private int CalculateCraftableCount()
    {
        if (recipe?.Ingredients == null || ResourceManager.Instance == null)
            return 0;

        int max = int.MaxValue;
        foreach (var ing in recipe.Ingredients)
        {
            if (ing.Item == null || ing.Amount <= 0) continue;
            int owned = ResourceManager.Instance.GetResourceAmount(ing.Item.ID);
            max = Mathf.Min(max, owned / ing.Amount);
        }
        return max == int.MaxValue ? 0 : max;
    }

    public void SetSelected(bool selected)
    {
        if (mode != SlotMode.Recipe) return;

        isSelected = selected;
        if (selectedOutline != null)
            selectedOutline.SetActive(selected);
        UpdateBackgroundColor();
    }

    #endregion

    #region Queue Mode

    public void SetQueueItem(CraftingQueueItem item)
    {
        if (mode != SlotMode.Queue) return;

        queueItem = item;
        isEmpty = item == null;

        if (isEmpty)
        {
            Clear();
            return;
        }

        // 아이콘 설정
        if (item.Recipe != null)
            SetIcon(item.Recipe.Icon);

        // 진행도
        UpdateProgress(item.Progress);

        // 제작중 표시
        if (processingIndicator != null)
            processingIndicator.SetActive(item.IsProcessing);

        UpdateBackgroundColor();
    }

    public void UpdateProgress(float progress)
    {
        if (mode != SlotMode.Queue) return;

        if (progressFill != null)
            progressFill.fillAmount = Mathf.Clamp01(progress);

        if (IsProcessing && backgroundImage != null)
            backgroundImage.color = processingColor;
    }

    public void Clear()
    {
        queueItem = null;
        isEmpty = true;

        // Icon을 SetActive(false)로 완전히 숨김
        if (iconImage != null)
        {
            iconImage.enabled = false;
            iconImage.gameObject.SetActive(false);
        }
        if (progressFill != null) progressFill.fillAmount = 0f;
        if (processingIndicator != null) processingIndicator.SetActive(false);
        if (backgroundImage != null) backgroundImage.color = normalColor;
    }

    #endregion

    #region Common

    private void SetIcon(Sprite sprite)
    {
        if (iconImage == null) return;

        // 먼저 GameObject 활성화
        iconImage.gameObject.SetActive(true);
        iconImage.sprite = sprite;
        iconImage.enabled = sprite != null;
    }

    private void UpdateBackgroundColor()
    {
        if (backgroundImage == null) return;

        if (mode == SlotMode.Recipe)
        {
            backgroundImage.color = !CanCraft ? disabledColor
                                  : isSelected ? selectedColor
                                  : normalColor;
        }
        else // Queue
        {
            backgroundImage.color = IsProcessing ? processingColor : normalColor;
        }
    }

    #endregion

    #region Pointer Events

    public void OnPointerClick(PointerEventData eventData)
    {
        if (mode == SlotMode.Recipe)
        {
            onRecipeClicked?.Invoke(this);
        }
        else // Queue
        {
            // 빈 슬롯이거나 제작 중이면 무시
            if (isEmpty || IsProcessing) return;
            onQueueClicked?.Invoke(this);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (mode == SlotMode.Recipe)
        {
            if (!isSelected && CanCraft && backgroundImage != null)
                backgroundImage.color = hoverColor;
        }
        else // Queue
        {
            if (!isEmpty && !IsProcessing && backgroundImage != null)
                backgroundImage.color = hoverColor;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isSelected)
            UpdateBackgroundColor();
    }

    #endregion
}