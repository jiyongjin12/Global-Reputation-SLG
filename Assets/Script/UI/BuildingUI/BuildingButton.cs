using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

/// <summary>
/// 건물 버튼 UI
/// - 건물 아이콘 표시
/// - 가격 표시 (CostItemUI 사용)
/// - 호버/선택 시 확대 효과
/// - 자원 부족 시 비활성화 표시
/// </summary>
public class BuildingButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image borderImage;
    [SerializeField] private Transform costContainer;            // 가격 아이템들이 들어갈 곳
    [SerializeField] private TextMeshProUGUI nameText;           // 건물 이름 (선택사항)
    [SerializeField] private GameObject lockedOverlay;           // 자원 부족 시 표시

    [Header("Cost Prefab")]
    [SerializeField] private GameObject costItemPrefab;          // 가격 아이템 프리팹

    [Header("Hover Settings")]
    [SerializeField] private float normalScale = 1f;
    [SerializeField] private float hoverScale = 1.15f;
    [SerializeField] private float selectedScale = 1.1f;
    [SerializeField] private float scaleDuration = 0.15f;

    [Header("Colors")]
    [SerializeField] private Color normalBorderColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color hoverBorderColor = new Color(0.8f, 0.8f, 0.8f, 1f);
    [SerializeField] private Color selectedBorderColor = new Color(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color cannotAffordColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);

    // 내부 상태
    private ObjectData buildingData;
    private int buttonIndex;
    private bool isSelected = false;
    private bool isHovered = false;
    private bool canAfford = true;

    // 콜백
    private Action<ObjectData> onClickCallback;
    private Action<int> onHoverCallback;

    private RectTransform rectTransform;
    private Tween scaleTween;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void OnDestroy()
    {
        scaleTween?.Kill();
    }

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(ObjectData data, int index, Action<ObjectData> onClick, Action<int> onHover)
    {
        Debug.Log($"[BuildingButton] Initialize 호출됨: {data?.Name ?? "NULL"}, index={index}");

        buildingData = data;
        buttonIndex = index;
        onClickCallback = onClick;
        onHoverCallback = onHover;

        // 아이콘 설정
        if (iconImage != null && data.Icon != null)
        {
            iconImage.sprite = data.Icon;
        }

        // 이름 설정
        if (nameText != null)
        {
            nameText.text = data.Name;
        }

        // 가격 표시 생성
        GenerateCostItems();

        // 자원 체크
        UpdateAffordability();

        // 초기 스케일
        transform.localScale = Vector3.one * normalScale;

        gameObject.name = $"BuildingBtn_{data.Name}";
    }

    /// <summary>
    /// 가격 아이템 UI 생성
    /// </summary>
    private void GenerateCostItems()
    {
        Debug.Log($"[BuildingButton] GenerateCostItems 호출: {buildingData?.Name}");
        Debug.Log($"[BuildingButton] costContainer null? {costContainer == null}");
        Debug.Log($"[BuildingButton] costItemPrefab null? {costItemPrefab == null}");

        if (costContainer == null)
        {
            Debug.LogError("[BuildingButton] costContainer가 null! Inspector에서 연결하세요.");
            return;
        }

        if (costItemPrefab == null)
        {
            Debug.LogError("[BuildingButton] costItemPrefab이 null! Inspector에서 CostItemPrefab을 연결하세요.");
            return;
        }

        // 기존 정리
        foreach (Transform child in costContainer)
        {
            Destroy(child.gameObject);
        }

        if (buildingData.ConstructionCosts == null || buildingData.ConstructionCosts.Length == 0)
        {
            Debug.Log($"[BuildingButton] {buildingData?.Name}: ConstructionCosts가 없음");
            return;
        }

        Debug.Log($"[BuildingButton] {buildingData?.Name}: {buildingData.ConstructionCosts.Length}개 비용 생성 시작");

        // 가격 아이템 생성
        foreach (var cost in buildingData.ConstructionCosts)
        {
            GameObject costObj = Instantiate(costItemPrefab, costContainer);
            CostItemUI costItem = costObj.GetComponent<CostItemUI>();

            if (costItem != null)
            {
                costItem.Initialize(cost);
            }
            else
            {
                Debug.LogError("[BuildingButton] CostItemPrefab에 CostItemUI 컴포넌트가 없습니다!");
            }
        }

        // 가격 아이템 크기 조정 (개수에 따라)
        AdjustCostItemsSize();

        Debug.Log($"[BuildingButton] {buildingData?.Name}: 비용 생성 완료");
    }

    /// <summary>
    /// 가격 아이템 크기 조정 (개수에 따라 축소)
    /// </summary>
    private void AdjustCostItemsSize()
    {
        if (costContainer == null)
            return;

        int costCount = buildingData.ConstructionCosts?.Length ?? 0;

        if (costCount <= 2)
            return; // 2개 이하는 조정 불필요

        // 3개 이상이면 스케일 축소
        float scale = costCount switch
        {
            3 => 0.85f,
            4 => 0.75f,
            _ => 0.65f
        };

        foreach (Transform child in costContainer)
        {
            child.localScale = Vector3.one * scale;
        }
    }

    /// <summary>
    /// 자원 보유 여부 업데이트
    /// </summary>
    public void UpdateAffordability()
    {
        Debug.Log($"[BuildingButton] === {buildingData?.Name} 자원 체크 시작 ===");

        // buildingData 체크
        if (buildingData == null)
        {
            Debug.LogError("[BuildingButton] buildingData가 null입니다!");
            canAfford = false;
            return;
        }

        // ConstructionCosts 체크
        if (buildingData.ConstructionCosts == null)
        {
            Debug.Log($"[BuildingButton] {buildingData.Name}: ConstructionCosts가 null → 건설 가능");
            canAfford = true;
        }
        else if (buildingData.ConstructionCosts.Length == 0)
        {
            Debug.Log($"[BuildingButton] {buildingData.Name}: ConstructionCosts가 비어있음 → 건설 가능");
            canAfford = true;
        }
        else
        {
            Debug.Log($"[BuildingButton] {buildingData.Name}: ConstructionCosts 개수 = {buildingData.ConstructionCosts.Length}");

            // 각 비용 상세 출력
            for (int i = 0; i < buildingData.ConstructionCosts.Length; i++)
            {
                var cost = buildingData.ConstructionCosts[i];
                if (cost == null)
                {
                    Debug.LogWarning($"  [{i}] cost가 null!");
                    continue;
                }
                if (cost.Resource == null)
                {
                    Debug.LogWarning($"  [{i}] cost.Resource가 null!");
                    continue;
                }

                int has = ResourceManager.Instance != null
                    ? ResourceManager.Instance.GetResourceAmount(cost.Resource)
                    : -1;
                Debug.Log($"  [{i}] {cost.Resource.ResourceName}: 필요 {cost.Amount}, 보유 {has}");
            }

            // ResourceManager 체크
            if (ResourceManager.Instance == null)
            {
                Debug.LogWarning($"[BuildingButton] ResourceManager.Instance가 null! → 일단 허용");
                canAfford = true;
            }
            else
            {
                canAfford = ResourceManager.Instance.CanAfford(buildingData.ConstructionCosts);
                Debug.Log($"[BuildingButton] {buildingData.Name}: CanAfford 결과 = {canAfford}");

                if (!canAfford)
                {
                    var missing = ResourceManager.Instance.GetMissingResources(buildingData.ConstructionCosts);
                    foreach (var (item, required, has) in missing)
                    {
                        Debug.Log($"  → 부족: {item.ResourceName} ({has}/{required})");
                    }
                }
            }
        }

        // 비활성화 오버레이
        if (lockedOverlay != null)
        {
            lockedOverlay.SetActive(!canAfford);
        }

        // 아이콘 색상
        if (iconImage != null)
        {
            iconImage.color = canAfford ? Color.white : cannotAffordColor;
        }

        // 가격 아이템 색상 업데이트
        UpdateCostItemColors();
    }

    /// <summary>
    /// 가격 아이템 색상 업데이트 (부족한 자원 빨간색)
    /// </summary>
    private void UpdateCostItemColors()
    {
        if (costContainer == null)
            return;

        CostItemUI[] costItems = costContainer.GetComponentsInChildren<CostItemUI>();
        foreach (var item in costItems)
        {
            item.UpdateAffordability();
        }
    }

    /// <summary>
    /// 선택 상태 설정
    /// </summary>
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateVisual();
    }

    /// <summary>
    /// 시각 효과 업데이트
    /// </summary>
    private void UpdateVisual()
    {
        // 테두리 색상
        if (borderImage != null)
        {
            if (isSelected)
                borderImage.color = selectedBorderColor;
            else if (isHovered)
                borderImage.color = hoverBorderColor;
            else
                borderImage.color = normalBorderColor;
        }

        // 스케일 애니메이션
        float targetScale = normalScale;
        if (isSelected)
            targetScale = selectedScale;
        else if (isHovered)
            targetScale = hoverScale;

        scaleTween?.Kill();
        scaleTween = transform.DOScale(targetScale, scaleDuration)
            .SetEase(Ease.OutBack)
            .SetUpdate(true); // TimeScale 영향 안 받음
    }

    /// <summary>
    /// 클릭 처리
    /// </summary>
    public void OnClick()
    {
        // ★ 클릭 시점에 canAfford 다시 계산!
        UpdateAffordability();

        Debug.Log($"[BuildingButton] ★ OnClick: {buildingData?.Name}, canAfford={canAfford}");

        if (!canAfford)
        {
            // 자원 부족 피드백
            ShakeButton();
            return;
        }

        onClickCallback?.Invoke(buildingData);
    }

    /// <summary>
    /// 자원 부족 시 흔들기 효과
    /// </summary>
    private void ShakeButton()
    {
        transform.DOShakePosition(0.3f, 5f, 20, 90f, false, true)
            .SetUpdate(true);
    }

    #region Pointer Events

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        UpdateVisual();
        onHoverCallback?.Invoke(buttonIndex);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        UpdateVisual();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"[BuildingButton] OnPointerClick 호출됨: {buildingData?.Name}, button={eventData.button}");

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            OnClick();
        }
    }

    #endregion
}