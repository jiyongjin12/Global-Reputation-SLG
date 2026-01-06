using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections.Generic;

/// <summary>
/// 건물 정보 패널 (Cult of the Lamb 스타일 카드)
/// - 선택한 건물의 상세 정보 표시
/// - 이름, 설명, 가격(보유량) 표시
/// - ★ 스케일 + 페이드 애니메이션
/// </summary>
public class BuildingInfoPanel : MonoBehaviour
{
    [Header("=== UI References ===")]
    [SerializeField] private GameObject panelRoot;               // 패널 전체
    [SerializeField] private RectTransform cardTransform;        // ★ 스케일 애니메이션 대상
    [SerializeField] private Image buildingIcon;                 // 건물 아이콘
    [SerializeField] private TextMeshProUGUI nameText;           // 건물 이름
    [SerializeField] private TextMeshProUGUI descriptionText;    // 건물 설명
    [SerializeField] private Transform costContainer;            // 비용 아이템 컨테이너
    [SerializeField] private GameObject infoCostItemPrefab;      // 정보용 비용 아이템 프리팹

    [Header("=== Colors ===")]
    [SerializeField] private Color normalAmountColor = Color.white;
    [SerializeField] private Color ownedAmountColor = new Color(0.5f, 0.5f, 0.5f, 1f);  // 흐린색 (보유량)
    [SerializeField] private Color insufficientColor = new Color(1f, 0.3f, 0.3f, 1f);   // 빨간색 (부족)

    [Header("=== Card Animation ===")]
    [SerializeField] private float animationDuration = 0.1f;     // 애니메이션 시간
    [SerializeField] private float startScale = 1.2f;            // 시작/끝 스케일 (커진 상태)
    [SerializeField] private float normalScale = 1f;             // 정상 스케일
    [SerializeField] private CanvasGroup canvasGroup;

    // 현재 표시 중인 건물
    private ObjectData currentBuilding;
    private List<GameObject> spawnedCostItems = new List<GameObject>();

    // 트윈 참조
    private Tween scaleTween;
    private Tween fadeTween;

    // 첫 열림 여부
    private bool isFirstShow = true;

    public static BuildingInfoPanel Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        // cardTransform 자동 설정
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
    /// 건물 정보 표시 (선택 변경 시 호출)
    /// </summary>
    public void ShowBuilding(ObjectData building)
    {
        if (building == null)
        {
            Hide();
            return;
        }

        // 같은 건물이면 무시
        if (currentBuilding == building)
            return;

        bool wasActive = panelRoot != null && panelRoot.activeSelf;
        currentBuilding = building;

        // 패널 활성화
        if (panelRoot != null)
            panelRoot.SetActive(true);

        // 데이터 업데이트
        UpdateVisuals(building);

        // 애니메이션
        if (wasActive)
        {
            // 이미 열려있었으면: 선택 변경 애니메이션
            PlaySwitchAnimation();
        }
        else
        {
            // 처음 열리면: 페이드인 애니메이션
            PlayShowAnimation();
        }
    }

    /// <summary>
    /// 비주얼 업데이트 (아이콘, 이름, 설명, 비용)
    /// </summary>
    private void UpdateVisuals(ObjectData building)
    {
        // 아이콘
        if (buildingIcon != null)
        {
            if (building.Icon != null)
            {
                buildingIcon.sprite = building.Icon;
                buildingIcon.enabled = true;
            }
            else
            {
                buildingIcon.enabled = false;
            }
        }

        // 이름
        if (nameText != null)
        {
            nameText.text = building.Name;
        }

        // 설명
        if (descriptionText != null)
        {
            descriptionText.text = building.Description ?? "";
        }

        // 비용 생성
        GenerateCostItems(building);
    }

    /// <summary>
    /// ★ 처음 열릴 때 애니메이션
    /// 커진 상태 + 투명 → 원래 크기 + 불투명
    /// </summary>
    private void PlayShowAnimation()
    {
        KillTweens();

        if (cardTransform != null)
        {
            cardTransform.localScale = Vector3.one * startScale;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
        }

        // 애니메이션
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
    /// ★ 선택 변경 애니메이션
    /// 커진 상태 + 투명 → 원래 크기 + 불투명 (데이터는 이미 변경됨)
    /// </summary>
    private void PlaySwitchAnimation()
    {
        KillTweens();

        // 시작: 커진 상태 + 투명
        if (cardTransform != null)
        {
            cardTransform.localScale = Vector3.one * startScale;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0.3f;
        }

        // 애니메이션: 원래 크기 + 불투명
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
    /// 비용 아이템 생성 (보유량 포함)
    /// </summary>
    private void GenerateCostItems(ObjectData building)
    {
        // 기존 정리
        foreach (var item in spawnedCostItems)
        {
            if (item != null)
                Destroy(item);
        }
        spawnedCostItems.Clear();

        if (costContainer == null || infoCostItemPrefab == null)
            return;

        if (building.ConstructionCosts == null || building.ConstructionCosts.Length == 0)
            return;

        foreach (var cost in building.ConstructionCosts)
        {
            GameObject costObj = Instantiate(infoCostItemPrefab, costContainer);
            spawnedCostItems.Add(costObj);

            // InfoCostItemUI 사용 (보유량 표시 포함)
            var costUI = costObj.GetComponent<InfoCostItemUI>();
            if (costUI != null)
            {
                costUI.Initialize(cost, normalAmountColor, ownedAmountColor, insufficientColor);
            }
        }
    }

    /// <summary>
    /// 패널 숨기기
    /// </summary>
    public void Hide()
    {
        KillTweens();

        if (panelRoot != null)
            panelRoot.SetActive(false);

        currentBuilding = null;
    }

    /// <summary>
    /// 보유량 업데이트 (자원 변경 시)
    /// </summary>
    public void RefreshCosts()
    {
        if (currentBuilding != null)
        {
            GenerateCostItems(currentBuilding);
        }
    }
}