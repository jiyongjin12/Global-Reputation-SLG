using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// 인벤토리 툴팁
/// - 아이템 이름, 설명, 카테고리 표시
/// - 마우스 따라다님
/// </summary>
public class InventoryTooltip : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI categoryText;
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private Image iconImage;

    [Header("Settings")]
    [SerializeField] private Vector2 offset = new Vector2(10f, -10f);
    [SerializeField] private float fadeSpeed = 0.1f;

    private RectTransform rectTransform;
    private RectTransform canvasRectTransform;
    private CanvasGroup canvasGroup;

    private bool isVisible = false;

    public static InventoryTooltip Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Canvas 찾기
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
            canvasRectTransform = canvas.GetComponent<RectTransform>();

        // 초기 상태: 숨김
        if (tooltipPanel != null)
            tooltipPanel.SetActive(false);
        canvasGroup.alpha = 0f;
    }

    private void Update()
    {
        if (isVisible)
        {
            FollowMouse();
        }
    }

    /// <summary>
    /// 툴팁 표시
    /// </summary>
    public void Show(ResourceItemSO item, int amount)
    {
        if (item == null)
            return;

        isVisible = true;

        // 패널 활성화
        if (tooltipPanel != null)
            tooltipPanel.SetActive(true);

        // 데이터 설정
        if (nameText != null)
            nameText.text = item.ResourceName;

        if (descriptionText != null)
            descriptionText.text = item.Description ?? "";

        if (categoryText != null)
            categoryText.text = $"[{item.Category}]";

        if (amountText != null)
            amountText.text = $"보유: {amount}개";

        if (iconImage != null && item.Icon != null)
        {
            iconImage.sprite = item.Icon;
            iconImage.enabled = true;
        }

        // 위치 업데이트
        FollowMouse();

        // 페이드인
        canvasGroup.DOKill();
        canvasGroup.DOFade(1f, fadeSpeed).SetUpdate(true);
    }

    /// <summary>
    /// 툴팁 숨기기
    /// </summary>
    public void Hide()
    {
        isVisible = false;

        canvasGroup.DOKill();
        canvasGroup.DOFade(0f, fadeSpeed)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                if (tooltipPanel != null)
                    tooltipPanel.SetActive(false);
            });
    }

    /// <summary>
    /// 마우스 따라가기
    /// </summary>
    private void FollowMouse()
    {
        Vector2 mousePos = Input.mousePosition;

        // 화면 밖으로 나가지 않게 조정
        if (rectTransform != null && canvasRectTransform != null)
        {
            Vector2 tooltipSize = rectTransform.sizeDelta;
            Vector2 canvasSize = canvasRectTransform.sizeDelta;

            float x = mousePos.x + offset.x;
            float y = mousePos.y + offset.y;

            // 오른쪽 경계
            if (x + tooltipSize.x > Screen.width)
                x = mousePos.x - tooltipSize.x - offset.x;

            // 아래쪽 경계
            if (y - tooltipSize.y < 0)
                y = mousePos.y + tooltipSize.y - offset.y;

            rectTransform.position = new Vector2(x, y);
        }
    }
}