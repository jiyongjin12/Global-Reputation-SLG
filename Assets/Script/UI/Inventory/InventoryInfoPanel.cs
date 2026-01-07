using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// 인벤토리 아이템 정보 패널 (BuildingInfoPanel 스타일)
/// - 선택한 아이템의 상세 정보 표시
/// - 아이콘, 이름, 설명, 카테고리, 보유량
/// </summary>
public class InventoryInfoPanel : MonoBehaviour
{
    [Header("=== UI References ===")]
    [SerializeField] private GameObject panelRoot;               // 패널 전체
    [SerializeField] private RectTransform cardTransform;        // 스케일 애니메이션 대상
    [SerializeField] private Image itemIcon;                     // 아이템 아이콘
    [SerializeField] private TextMeshProUGUI nameText;           // 아이템 이름
    [SerializeField] private TextMeshProUGUI descriptionText;    // 아이템 설명
    [SerializeField] private TextMeshProUGUI categoryText;       // 카테고리
    [SerializeField] private TextMeshProUGUI amountText;         // 보유량

    [Header("=== Optional ===")]
    [SerializeField] private TextMeshProUGUI stackInfoText;      // 스택 정보 (예: 최대 99개)
    [SerializeField] private Image rarityBorder;                 // 희귀도 테두리 (선택사항)

    [Header("=== Card Animation ===")]
    [SerializeField] private float animationDuration = 0.1f;
    [SerializeField] private float startScale = 1.2f;
    [SerializeField] private float normalScale = 1f;
    [SerializeField] private CanvasGroup canvasGroup;

    // 현재 표시 중인 아이템
    private StoredResource currentResource;

    // 트윈 참조
    private Tween scaleTween;
    private Tween fadeTween;

    public static InventoryInfoPanel Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (cardTransform == null && panelRoot != null)
            cardTransform = panelRoot.GetComponent<RectTransform>();

        // 초기 상태: 숨김
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        KillTweens();
    }

    private void KillTweens()
    {
        scaleTween?.Kill();
        fadeTween?.Kill();
    }

    /// <summary>
    /// 아이템 정보 표시
    /// </summary>
    public void ShowItem(StoredResource stored)
    {
        if (stored == null || stored.Item == null)
        {
            Hide();
            return;
        }

        // 같은 아이템이면 무시
        if (currentResource == stored)
            return;

        bool wasActive = panelRoot != null && panelRoot.activeSelf;
        currentResource = stored;

        // 패널 활성화
        if (panelRoot != null)
            panelRoot.SetActive(true);

        // 데이터 업데이트
        UpdateVisuals(stored);

        // 애니메이션
        if (wasActive)
        {
            PlaySwitchAnimation();
        }
        else
        {
            PlayShowAnimation();
        }
    }

    /// <summary>
    /// 비주얼 업데이트
    /// </summary>
    private void UpdateVisuals(StoredResource stored)
    {
        var item = stored.Item;

        // 아이콘
        if (itemIcon != null)
        {
            if (item.Icon != null)
            {
                itemIcon.sprite = item.Icon;
                itemIcon.enabled = true;
            }
            else
            {
                itemIcon.enabled = false;
            }
        }

        // 이름
        if (nameText != null)
        {
            nameText.text = item.ResourceName;
        }

        // 설명
        if (descriptionText != null)
        {
            descriptionText.text = item.Description ?? "";
        }

        // 카테고리
        if (categoryText != null)
        {
            categoryText.text = GetCategoryDisplayName(item.Category);
        }

        // 보유량
        if (amountText != null)
        {
            amountText.text = $"보유: {stored.Amount}개";
        }

        // 스택 정보 (선택사항)
        if (stackInfoText != null)
        {
            stackInfoText.text = $"최대 스택: {item.MaxStackSize}개";
        }
    }

    /// <summary>
    /// 카테고리 표시 이름
    /// </summary>
    private string GetCategoryDisplayName(ResourceCategory cat)
    {
        switch (cat)
        {
            case ResourceCategory.Currency: return "재화";
            case ResourceCategory.Food: return "음식";
            case ResourceCategory.Material: return "재료";
            case ResourceCategory.Equipment: return "장비";
            case ResourceCategory.Seed: return "씨앗";
            case ResourceCategory.Special: return "특수";
            default: return cat.ToString();
        }
    }

    /// <summary>
    /// 처음 열릴 때 애니메이션
    /// </summary>
    private void PlayShowAnimation()
    {
        KillTweens();

        if (cardTransform != null)
            cardTransform.localScale = Vector3.one * startScale;

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        if (cardTransform != null)
        {
            scaleTween = cardTransform.DOScale(normalScale, animationDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        if (canvasGroup != null)
        {
            fadeTween = canvasGroup.DOFade(1f, animationDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }
    }

    /// <summary>
    /// 아이템 변경 시 애니메이션
    /// </summary>
    private void PlaySwitchAnimation()
    {
        KillTweens();

        if (cardTransform != null)
            cardTransform.localScale = Vector3.one * startScale;

        if (canvasGroup != null)
            canvasGroup.alpha = 0.3f;

        if (cardTransform != null)
        {
            scaleTween = cardTransform.DOScale(normalScale, animationDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        if (canvasGroup != null)
        {
            fadeTween = canvasGroup.DOFade(1f, animationDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }
    }

    /// <summary>
    /// 숨기기
    /// </summary>
    public void Hide()
    {
        KillTweens();

        if (panelRoot != null)
            panelRoot.SetActive(false);

        currentResource = null;
    }

    /// <summary>
    /// 현재 표시 중인 아이템의 수량 갱신
    /// </summary>
    public void RefreshAmount()
    {
        if (currentResource != null && amountText != null)
        {
            amountText.text = $"보유: {currentResource.Amount}개";
        }
    }
}