using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// 건물 카드 (오버레이 애니메이션용)
/// - 선택된 버튼 위에 표시
/// - 스케일/페이드 애니메이션 담당
/// - 2개만 사용해서 모든 전환 처리
/// </summary>
public class BuildingCard : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image borderImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Transform costContainer;
    [SerializeField] private GameObject costItemPrefab;

    [Header("Colors")]
    [SerializeField] private Color normalBorderColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color selectedBorderColor = new Color(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color cannotAffordColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);

    [Header("Animation")]
    [SerializeField] private float transitionDuration = 0.1f;
    [SerializeField] private float selectedScale = 1.1f;
    [SerializeField] private float deselectedScale = 1.15f;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Tween scaleTween;
    private Tween fadeTween;
    private Tween moveTween;

    private ObjectData currentData;
    private bool isActive = false;

    public bool IsActive => isActive;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        // 초기 상태: 숨김
        canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        KillTweens();
    }

    private void KillTweens()
    {
        scaleTween?.Kill();
        fadeTween?.Kill();
        moveTween?.Kill();
    }

    /// <summary>
    /// 카드 데이터 설정 (비주얼 업데이트)
    /// </summary>
    public void SetData(ObjectData data)
    {
        currentData = data;

        if (data == null)
            return;

        // 아이콘
        if (iconImage != null && data.Icon != null)
        {
            iconImage.sprite = data.Icon;
        }

        // 이름
        if (nameText != null)
        {
            nameText.text = data.Name;
        }

        // 비용 생성
        GenerateCostItems(data);

        // 테두리 색상
        if (borderImage != null)
        {
            borderImage.color = selectedBorderColor;
        }
    }

    /// <summary>
    /// 비용 아이템 생성
    /// </summary>
    private void GenerateCostItems(ObjectData data)
    {
        if (costContainer == null || costItemPrefab == null)
            return;

        // 기존 정리
        foreach (Transform child in costContainer)
        {
            Destroy(child.gameObject);
        }

        if (data.ConstructionCosts == null)
            return;

        foreach (var cost in data.ConstructionCosts)
        {
            GameObject costObj = Instantiate(costItemPrefab, costContainer);
            CostItemUI costUI = costObj.GetComponent<CostItemUI>();
            if (costUI != null)
            {
                costUI.Initialize(cost);
            }
        }
    }

    /// <summary>
    /// ★ 나타나기 애니메이션 (새로 선택됨)
    /// 커진상태+투명 → 원래크기+불투명
    /// </summary>
    public void ShowAt(Vector3 worldPosition, ObjectData data)
    {
        Debug.Log($"[BuildingCard] ShowAt: {data?.Name}");

        KillTweens();

        gameObject.SetActive(true);
        isActive = true;

        // 데이터 설정
        SetData(data);

        // 위치 설정
        rectTransform.position = worldPosition;

        // 시작 상태: 크고 투명
        transform.localScale = Vector3.one * deselectedScale;
        canvasGroup.alpha = 0.3f;

        // 애니메이션: 선택 크기 + 불투명
        scaleTween = transform.DOScale(selectedScale, transitionDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        fadeTween = canvasGroup.DOFade(1f, transitionDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);
    }

    /// <summary>
    /// ★ 사라지기 애니메이션 (선택 해제됨)
    /// 커지면서 투명해짐
    /// </summary>
    public void Hide()
    {
        if (!isActive)
            return;

        Debug.Log("[BuildingCard] Hide");

        KillTweens();

        // 애니메이션: 커지면서 투명
        scaleTween = transform.DOScale(deselectedScale, transitionDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        fadeTween = canvasGroup.DOFade(0f, transitionDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                isActive = false;
                gameObject.SetActive(false);
            });
    }

    /// <summary>
    /// 즉시 숨기기 (애니메이션 없음)
    /// </summary>
    public void HideImmediate()
    {
        KillTweens();
        isActive = false;
        canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// UI 열릴 때 페이드인 (첫 선택)
    /// </summary>
    public void FadeIn(Vector3 worldPosition, ObjectData data, float delay)
    {
        Debug.Log($"[BuildingCard] FadeIn: {data?.Name}, delay={delay}");

        KillTweens();

        gameObject.SetActive(true);
        isActive = true;

        // 데이터 설정
        SetData(data);

        // 위치 설정
        rectTransform.position = worldPosition;

        // 시작 상태: 투명 + 작음
        transform.localScale = Vector3.one * 0.8f;
        canvasGroup.alpha = 0f;

        // 애니메이션
        scaleTween = transform.DOScale(selectedScale, 0.2f)
            .SetDelay(delay)
            .SetEase(Ease.OutBack)
            .SetUpdate(true);

        fadeTween = canvasGroup.DOFade(1f, 0.2f)
            .SetDelay(delay)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);
    }
}