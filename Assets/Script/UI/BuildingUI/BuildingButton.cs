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
/// - ★ 컬트오브더램 스타일 카드 애니메이션
/// </summary>
public class BuildingButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("UI References")]
    [SerializeField] private RectTransform cardContent;             // ★ 스케일/페이드 애니메이션 대상
    [SerializeField] private Image iconImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image borderImage;
    [SerializeField] private Transform costContainer;            // 가격 아이템들이 들어갈 곳
    [SerializeField] private TextMeshProUGUI nameText;           // 건물 이름 (선택사항)
    [SerializeField] private GameObject lockedOverlay;           // 자원 부족 시 표시

    [Header("Cost Prefab")]
    [SerializeField] private GameObject costItemPrefab;          // 가격 아이템 프리팹

    [Header("Scale Settings")]
    [SerializeField] private float normalScale = 1f;
    [SerializeField] private float selectedScale = 1.1f;
    [SerializeField] private float deselectedScale = 1.15f;      // 선택 해제 시 커지는 크기
    [SerializeField] private float scaleDuration = 0.1f;

    [Header("Colors")]
    [SerializeField] private Color normalBorderColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color hoverBorderColor = new Color(0.8f, 0.8f, 0.8f, 1f);
    [SerializeField] private Color selectedBorderColor = new Color(1f, 0.8f, 0.2f, 1f);
    [SerializeField] private Color cannotAffordColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);

    [Header("Card Animation")]
    [SerializeField] private float fadeInDuration = 0.2f;        // 처음 나타날 때
    [SerializeField] private float transitionDuration = 0.1f;    // 선택 변경 시

    // 내부 상태
    private ObjectData buildingData;
    private int buttonIndex;
    private bool isSelected = false;
    private bool canAfford = true;

    // 콜백
    private Action<ObjectData> onClickCallback;
    private Action<int> onHoverCallback;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Tween scaleTween;
    private Tween fadeTween;

    public ObjectData BuildingData => buildingData;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        // ★ cardContent가 있으면 거기서 CanvasGroup 가져오기
        if (cardContent != null)
        {
            canvasGroup = cardContent.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = cardContent.gameObject.AddComponent<CanvasGroup>();
        }
        else
        {
            // cardContent가 없으면 자기 자신 사용 (하위 호환)
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void OnDestroy()
    {
        scaleTween?.Kill();
        fadeTween?.Kill();
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

        // 개수에 따른 스케일 결정
        float scale = costCount switch
        {
            0 => 1f,
            1 => 1f,
            2 => 1f,      // 2개까지는 기본 크기
            3 => 0.8f,    // 3개면 80%
            4 => 0.7f,    // 4개면 70%
            _ => 0.6f     // 5개 이상이면 60%
        };

        // 각 CostItemUI에 스케일 적용
        CostItemUI[] costItems = costContainer.GetComponentsInChildren<CostItemUI>();
        foreach (var item in costItems)
        {
            item.SetScale(scale);
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
    /// 선택 상태 설정 (애니메이션 포함)
    /// </summary>
    public void SetSelected(bool selected)
    {
        bool wasSelected = isSelected;
        isSelected = selected;

        Debug.Log($"[BuildingButton] SetSelected: {buildingData?.Name}, wasSelected={wasSelected}, selected={selected}");

        // 선택 변경 시 애니메이션
        if (wasSelected && !selected)
        {
            // 선택 해제: 커지면서 투명해짐
            Debug.Log($"[BuildingButton] PlayDeselectAnimation: {buildingData?.Name}");
            PlayDeselectAnimation();
        }
        else if (!wasSelected && selected)
        {
            // 새로 선택: 커진상태+투명 → 원래크기+불투명
            Debug.Log($"[BuildingButton] PlaySelectAnimation: {buildingData?.Name}");
            PlaySelectAnimation();
        }
        else
        {
            // 변경 없음: 즉시 적용
            UpdateVisualImmediate();
        }
    }

    /// <summary>
    /// 선택 애니메이션 (커진상태+투명 → 원래크기+불투명)
    /// </summary>
    private void PlaySelectAnimation()
    {
        if (canvasGroup == null)
        {
            Debug.LogError($"[BuildingButton] CanvasGroup이 null! {buildingData?.Name}");
            return;
        }

        scaleTween?.Kill();
        fadeTween?.Kill();

        // 애니메이션 대상 (cardContent가 있으면 cardContent, 없으면 자기 자신)
        Transform animTarget = cardContent != null ? cardContent : transform;

        // 시작: 크고 투명
        animTarget.localScale = Vector3.one * deselectedScale;
        canvasGroup.alpha = 0.3f;

        Debug.Log($"[BuildingButton] SelectAnim 시작: scale={deselectedScale}, alpha=0.3");

        // 종료: 선택 크기 + 불투명
        scaleTween = animTarget.DOScale(selectedScale, transitionDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        fadeTween = canvasGroup.DOFade(1f, transitionDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        // 테두리
        if (borderImage != null)
            borderImage.color = selectedBorderColor;
    }

    /// <summary>
    /// 선택 해제 애니메이션 (원래크기 → 커지면서 투명)
    /// </summary>
    private void PlayDeselectAnimation()
    {
        if (canvasGroup == null)
        {
            Debug.LogError($"[BuildingButton] CanvasGroup이 null! {buildingData?.Name}");
            return;
        }

        scaleTween?.Kill();
        fadeTween?.Kill();

        // 애니메이션 대상
        Transform animTarget = cardContent != null ? cardContent : transform;

        Debug.Log($"[BuildingButton] DeselectAnim 시작");

        // 커지면서 투명
        Sequence seq = DOTween.Sequence();
        seq.Append(animTarget.DOScale(deselectedScale, transitionDuration * 0.5f)
            .SetEase(Ease.OutQuad));
        seq.Join(canvasGroup.DOFade(0.5f, transitionDuration * 0.5f)
            .SetEase(Ease.OutQuad));

        // 다시 원래대로
        seq.Append(animTarget.DOScale(normalScale, transitionDuration * 0.5f)
            .SetEase(Ease.OutQuad));
        seq.Join(canvasGroup.DOFade(1f, transitionDuration * 0.5f)
            .SetEase(Ease.OutQuad));

        seq.SetUpdate(true);
        scaleTween = seq;

        // 테두리
        if (borderImage != null)
            borderImage.color = normalBorderColor;
    }

    /// <summary>
    /// 즉시 시각 효과 적용 (애니메이션 없음)
    /// </summary>
    private void UpdateVisualImmediate()
    {
        // 테두리 색상
        if (borderImage != null)
        {
            borderImage.color = isSelected ? selectedBorderColor : normalBorderColor;
        }

        // 애니메이션 대상
        Transform animTarget = cardContent != null ? cardContent : transform;

        // 스케일
        animTarget.localScale = Vector3.one * (isSelected ? selectedScale : normalScale);

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
    }

    /// <summary>
    /// ★ UI 열릴 때 페이드인 애니메이션
    /// </summary>
    public void PlayFadeInAnimation(float delay)
    {
        if (canvasGroup == null)
        {
            Debug.LogError($"[BuildingButton] PlayFadeIn - CanvasGroup null!");
            return;
        }

        Debug.Log($"[BuildingButton] PlayFadeInAnimation: {buildingData?.Name}, delay={delay}");

        scaleTween?.Kill();
        fadeTween?.Kill();

        // 애니메이션 대상
        Transform animTarget = cardContent != null ? cardContent : transform;

        // 시작: 투명 + 약간 작음
        canvasGroup.alpha = 0f;
        animTarget.localScale = Vector3.one * 0.8f;

        // 페이드인 + 스케일업
        fadeTween = canvasGroup.DOFade(1f, fadeInDuration)
            .SetDelay(delay)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        float targetScale = isSelected ? selectedScale : normalScale;
        scaleTween = animTarget.DOScale(targetScale, fadeInDuration)
            .SetDelay(delay)
            .SetEase(Ease.OutBack)
            .SetUpdate(true);
    }

    /// <summary>
    /// 시각 효과 업데이트 (호환성용)
    /// </summary>
    private void UpdateVisual()
    {
        UpdateVisualImmediate();
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
        // 마우스가 버튼 위에 들어오면 선택 콜백 호출 (isHovered 제거)
        onHoverCallback?.Invoke(buttonIndex);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 마우스가 버튼에서 나가면 매니저에 알림
        if (BuildingUIManager.Instance != null)
        {
            BuildingUIManager.Instance.OnBuildingButtonUnhovered();
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            OnClick();
        }
    }

    #endregion
}