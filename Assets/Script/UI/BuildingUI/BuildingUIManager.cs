using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DG.Tweening;

/// <summary>
/// 건물 UI 전체 관리
/// - 카테고리별 건물 표시
/// - 키보드/마우스 네비게이션
/// - 건물 배치 연동
/// - ★ IEscapableUI 구현: GameInputManager 연동
/// - ★ DOTween 슬라이드 애니메이션
/// - ★ TimeScale 조절 + Depth of Field 블러
/// - ★ 컬트오브더램 스타일 카드 애니메이션
/// </summary>
public class BuildingUIManager : MonoBehaviour, IEscapableUI
{
    [Header("=== References ===")]
    [SerializeField] private ObjectsDatabaseSO database;
    [SerializeField] private PlacementSystem placementSystem;

    [Header("=== UI References ===")]
    [SerializeField] private GameObject buildingPanel;           // ★ GameObject로 유지 (기존 연결 호환)
    [SerializeField] private Transform contentParent;            // Scroll Rect의 Content
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private BuildingInfoPanel infoPanel;        // ★ 건물 정보 패널 (카드)

    // 내부에서 사용할 RectTransform
    private RectTransform panelRectTransform;

    [Header("=== Prefabs ===")]
    [SerializeField] private GameObject categoryPrefab;          // 카테고리 배경 프리팹
    [SerializeField] private GameObject buildingButtonPrefab;    // 건물 버튼 프리팹

    [Header("=== Post Processing ===")]
    [SerializeField] private Volume globalVolume;                // Global Volume 참조
    [SerializeField] private float blurFocalLengthMin = 0f;      // 닫힌 상태 (블러 없음)
    [SerializeField] private float blurFocalLengthMax = 50f;     // 열린 상태 (블러 최대)

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float closedPosX = 1400f;           // 닫힌 상태 X 위치 (오른쪽 밖)
    [SerializeField] private float openedPosX = 0f;              // 열린 상태 X 위치
    [SerializeField] private float slideDuration = 0.4f;         // 슬라이드 애니메이션 시간
    [SerializeField] private Ease slideEase = Ease.OutQuart;     // 슬라이드 이징

    [Header("=== 카드 페이드인 설정 ===")]
    [SerializeField] private float cardFadeInDelay = 0.02f;      // 카드 간 딜레이
    [SerializeField] private float cardFadeInStartDelay = 0.1f;  // 첫 카드 딜레이

    [Header("=== 게임 속도 설정 ===")]
    [SerializeField] private float slowTimeScale = 0.05f;        // 열렸을 때 게임 속도
    [SerializeField] private float normalTimeScale = 1f;         // 닫혔을 때 게임 속도
    [SerializeField] private float timeScaleDuration = 0.3f;     // 속도 변경 시간

    [Header("=== ESC 우선순위 ===")]
    [Tooltip("높을수록 ESC 시 먼저 닫힘 (채팅창: 100, 건물UI: 90)")]
    [SerializeField] private int escapePriority = 90;

    // Depth of Field 참조
    private DepthOfField depthOfField;

    // 현재 진행 중인 트윈
    private Tweener panelTween;
    private Tweener blurTween;
    private Tweener timeTween;

    // 내부 상태
    private List<CategoryUI> categoryUIs = new List<CategoryUI>();
    private List<BuildingButton> allButtons = new List<BuildingButton>();
    private int selectedIndex = 0;
    private bool isOpen = false;

    // 마우스 상태 (마우스 우선 선택용)
    private bool isMouseOverButton = false;

    // 현재 배치 중 상태
    private bool isPlacing = false;
    private int currentPlacingID = -1;

    // 이벤트
    public event Action OnPanelOpened;
    public event Action OnPanelClosed;

    // ===== IEscapableUI 구현 =====
    public bool IsOpen => isOpen;
    public int EscapePriority => escapePriority;

    public static BuildingUIManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        // ★ RectTransform 가져오기
        if (buildingPanel != null)
        {
            panelRectTransform = buildingPanel.GetComponent<RectTransform>();
        }

        // Depth of Field 초기화
        InitializeDepthOfField();

        // 초기 상태 설정 (닫힌 상태)
        InitializeClosedState();
    }

    private void Start()
    {
        Debug.Log("[BuildingUIManager] Start 호출됨");

        GenerateUI();

        // ★ GameInputManager에 등록
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.RegisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered += HandleGameAction;
            Debug.Log("[BuildingUIManager] GameInputManager에 등록됨");
        }
        else
        {
            Debug.LogWarning("[BuildingUIManager] GameInputManager가 없습니다. 직접 입력 처리합니다.");
        }
    }

    private void OnDestroy()
    {
        // 트윈 정리
        KillAllTweens();

        // TimeScale 복구 (중요!)
        Time.timeScale = normalTimeScale;

        // Depth of Field 복구
        if (depthOfField != null)
            depthOfField.focalLength.value = blurFocalLengthMin;

        // ★ GameInputManager에서 해제
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.UnregisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered -= HandleGameAction;
        }
    }

    private void Update()
    {
        // 배치 중일 때 ESC 처리
        if (isPlacing)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPlacing();
            }
            return;
        }

        // GameInputManager가 없을 때만 직접 처리 (폴백)
        if (GameInputManager.Instance == null)
        {
            HandleInputFallback();
        }

        // UI 열려있을 때 네비게이션
        if (isOpen)
        {
            HandleNavigationInput();
        }
    }

    // ==================== 초기화 ====================

    /// <summary>
    /// Depth of Field 초기화
    /// </summary>
    private void InitializeDepthOfField()
    {
        if (globalVolume != null && globalVolume.profile != null)
        {
            if (!globalVolume.profile.TryGet(out depthOfField))
            {
                Debug.LogWarning("[BuildingUIManager] Global Volume에 Depth of Field가 없습니다!");
            }
        }
    }

    /// <summary>
    /// 초기 닫힌 상태 설정
    /// </summary>
    private void InitializeClosedState()
    {
        if (panelRectTransform != null)
        {
            Vector2 pos = panelRectTransform.anchoredPosition;
            pos.x = closedPosX;
            panelRectTransform.anchoredPosition = pos;
        }

        if (buildingPanel != null)
        {
            buildingPanel.SetActive(false);
        }

        if (depthOfField != null)
        {
            depthOfField.focalLength.value = blurFocalLengthMin;
        }

        isOpen = false;
    }

    // ==================== 입력 처리 ====================

    /// <summary>
    /// ★ GameInputManager에서 액션 처리
    /// </summary>
    private void HandleGameAction(GameAction action)
    {
        switch (action)
        {
            case GameAction.OpenBuilding:
                if (!isOpen && !isPlacing)
                    Open();
                break;
        }
    }

    /// <summary>
    /// GameInputManager 없을 때 폴백 (직접 입력 처리)
    /// </summary>
    private void HandleInputFallback()
    {
        // B키로 열기
        if (Input.GetKeyDown(KeyCode.B))
        {
            if (!isOpen && !isPlacing)
                Open();
        }

        // ESC로 닫기
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isOpen)
                Close();
        }
    }

    // ==================== 열기/닫기 (애니메이션) ====================

    /// <summary>
    /// UI 열기 (슬라이드 애니메이션 + 시간 느려짐 + 카드 페이드인)
    /// </summary>
    public void Open()
    {
        if (isOpen || isPlacing) return;
        isOpen = true;

        // 기존 트윈 정지
        KillAllTweens();

        // 패널 활성화
        buildingPanel.SetActive(true);

        // 자원 상태 업데이트
        RefreshAllButtonsAffordability();

        // 첫 번째 버튼 선택
        if (allButtons.Count > 0)
        {
            selectedIndex = 0;
            allButtons[0].SetSelected(true);
            UpdateInfoPanel();  // ★ 카드 애니메이션은 여기서 처리됨
        }

        // ★ 버튼들 페이드인 애니메이션
        PlayAllCardsFadeIn();

        // ===== 애니메이션 시작 =====

        // 1. 패널 슬라이드 (오른쪽에서 들어옴)
        if (panelRectTransform != null)
        {
            panelTween = panelRectTransform
                .DOAnchorPosX(openedPosX, slideDuration)
                .SetEase(slideEase)
                .SetUpdate(true);  // TimeScale 영향 안 받음
        }

        // 2. Depth of Field 블러
        if (depthOfField != null)
        {
            blurTween = DOTween
                .To(() => depthOfField.focalLength.value,
                    x => depthOfField.focalLength.value = x,
                    blurFocalLengthMax,
                    slideDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        // 3. 게임 속도 감소
        timeTween = DOTween
            .To(() => Time.timeScale, x => Time.timeScale = x, slowTimeScale, timeScaleDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        OnPanelOpened?.Invoke();
        Debug.Log("[BuildingUIManager] UI 열림");
    }

    /// <summary>
    /// ★ 버튼들 페이드인 애니메이션
    /// </summary>
    private void PlayAllCardsFadeIn()
    {
        Debug.Log($"[BuildingUIManager] PlayAllCardsFadeIn 호출, 버튼 수: {allButtons.Count}");

        for (int i = 0; i < allButtons.Count; i++)
        {
            float delay = cardFadeInStartDelay + (i * cardFadeInDelay);
            allButtons[i].PlayFadeInAnimation(delay);
        }
    }

    /// <summary>
    /// UI 닫기 (슬라이드 애니메이션 + 시간 복구)
    /// </summary>
    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        // 기존 트윈 정지
        KillAllTweens();

        // 선택 해제
        DeselectAll();

        // ★ 정보 패널(카드) 숨기기
        if (infoPanel != null)
            infoPanel.Hide();

        // ===== 애니메이션 시작 =====

        // 1. 패널 슬라이드 (오른쪽으로 나감)
        if (panelRectTransform != null)
        {
            panelTween = panelRectTransform
                .DOAnchorPosX(closedPosX, slideDuration)
                .SetEase(slideEase)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    // 애니메이션 완료 후 비활성화 (최적화)
                    if (buildingPanel != null)
                        buildingPanel.SetActive(false);
                });
        }
        else if (buildingPanel != null)
        {
            // RectTransform 없으면 바로 비활성화
            buildingPanel.SetActive(false);
        }

        // 2. Depth of Field 블러 해제
        if (depthOfField != null)
        {
            blurTween = DOTween
                .To(() => depthOfField.focalLength.value,
                    x => depthOfField.focalLength.value = x,
                    blurFocalLengthMin,
                    slideDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        // 3. 게임 속도 복구
        timeTween = DOTween
            .To(() => Time.timeScale, x => Time.timeScale = x, normalTimeScale, timeScaleDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        OnPanelClosed?.Invoke();
        Debug.Log("[BuildingUIManager] UI 닫힘");
    }

    /// <summary>
    /// 모든 트윈 정지
    /// </summary>
    private void KillAllTweens()
    {
        panelTween?.Kill();
        blurTween?.Kill();
        timeTween?.Kill();
    }

    // ==================== 기존 메서드들 ====================

    /// <summary>
    /// 모든 버튼의 자원 상태 새로고침
    /// </summary>
    public void RefreshAllButtonsAffordability()
    {
        foreach (var button in allButtons)
        {
            button.UpdateAffordability();
        }
    }

    /// <summary>
    /// UI 생성 (시작 시 1회)
    /// </summary>
    private void GenerateUI()
    {
        Debug.Log("[BuildingUIManager] GenerateUI 시작");

        if (database == null || categoryPrefab == null || buildingButtonPrefab == null)
        {
            Debug.LogError("[BuildingUIManager] Missing references!");
            return;
        }

        // 기존 UI 정리
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }
        categoryUIs.Clear();
        allButtons.Clear();

        // 카테고리별로 생성
        List<BuildingCategory> categories = database.GetAllCategories();

        foreach (var category in categories)
        {
            List<ObjectData> buildings = database.GetObjectsByCategory(category);
            buildings = buildings.FindAll(b => b.ShowInBuildMenu);

            if (buildings.Count == 0)
                continue;

            // 카테고리 UI 생성
            GameObject categoryObj = Instantiate(categoryPrefab, contentParent);
            CategoryUI categoryUI = categoryObj.GetComponent<CategoryUI>();

            if (categoryUI != null)
            {
                categoryUI.Initialize(category, buildings.Count);
                categoryUIs.Add(categoryUI);

                // 건물 버튼들 생성
                foreach (var building in buildings)
                {
                    GameObject buttonObj = Instantiate(buildingButtonPrefab, categoryUI.ButtonContainer);
                    BuildingButton button = buttonObj.GetComponent<BuildingButton>();

                    if (button != null)
                    {
                        int buttonIndex = allButtons.Count;
                        button.Initialize(building, buttonIndex, OnBuildingButtonClicked, OnBuildingButtonHovered);
                        allButtons.Add(button);
                    }
                }
            }
        }

        Debug.Log($"[BuildingUIManager] 생성 완료: {categoryUIs.Count}개 카테고리, {allButtons.Count}개 버튼");
    }

    /// <summary>
    /// 네비게이션 입력 처리
    /// </summary>
    private void HandleNavigationInput()
    {
        // 마우스가 버튼 위에 있으면 키보드 네비게이션 무시 (마우스 우선)
        if (isMouseOverButton)
            return;

        // 방향키 네비게이션
        bool moved = false;

        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
        {
            SelectButtonByKeyboard(selectedIndex + 1);
            moved = true;
        }
        else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
        {
            SelectButtonByKeyboard(selectedIndex - 1);
            moved = true;
        }
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
        {
            SelectButtonByKeyboard(selectedIndex + GetColumnsPerRow());
            moved = true;
        }
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
        {
            SelectButtonByKeyboard(selectedIndex - GetColumnsPerRow());
            moved = true;
        }

        // Enter/Space로 선택
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            if (selectedIndex >= 0 && selectedIndex < allButtons.Count)
            {
                allButtons[selectedIndex].OnClick();
            }
        }

        // 마우스 스크롤 (ScrollRect가 자체 처리하므로 제거)
        // ScrollRect의 Scroll Sensitivity 설정으로 조절
    }

    /// <summary>
    /// 키보드로 버튼 선택 (스크롤 이동 포함)
    /// </summary>
    private void SelectButtonByKeyboard(int index)
    {
        if (allButtons.Count == 0)
            return;

        index = Mathf.Clamp(index, 0, allButtons.Count - 1);

        // 같은 버튼이면 무시
        if (index == selectedIndex)
            return;

        // 이전 선택 해제
        if (selectedIndex >= 0 && selectedIndex < allButtons.Count)
        {
            allButtons[selectedIndex].SetSelected(false);
        }

        selectedIndex = index;
        allButtons[selectedIndex].SetSelected(true);

        // 키보드 이동 시에만 스크롤 따라가기
        EnsureButtonVisible(allButtons[selectedIndex]);

        // ★ 정보 패널(카드) 업데이트 - 여기서 카드 애니메이션 처리됨
        UpdateInfoPanel();
    }

    /// <summary>
    /// 마우스로 버튼 선택 (스크롤 이동 없음)
    /// </summary>
    private void SelectButtonByMouse(int index)
    {
        if (allButtons.Count == 0)
            return;

        index = Mathf.Clamp(index, 0, allButtons.Count - 1);

        // 같은 버튼이면 무시
        if (index == selectedIndex)
            return;

        // 이전 선택 해제
        if (selectedIndex >= 0 && selectedIndex < allButtons.Count)
        {
            allButtons[selectedIndex].SetSelected(false);
        }

        selectedIndex = index;
        allButtons[selectedIndex].SetSelected(true);

        // 마우스 선택 시에는 스크롤 이동 안 함!

        // ★ 정보 패널(카드) 업데이트 - 여기서 카드 애니메이션 처리됨
        UpdateInfoPanel();
    }

    private void SelectButton(int index)
    {
        SelectButtonByMouse(index);
    }

    /// <summary>
    /// ★ 정보 패널(카드) 업데이트
    /// </summary>
    private void UpdateInfoPanel(ObjectData building = null)
    {
        if (infoPanel == null)
            return;

        // 파라미터가 없으면 현재 선택된 건물 사용
        if (building == null && selectedIndex >= 0 && selectedIndex < allButtons.Count)
        {
            building = allButtons[selectedIndex].BuildingData;
        }

        if (building != null)
        {
            infoPanel.ShowBuilding(building);
        }
        else
        {
            infoPanel.Hide();
        }
    }

    private void DeselectAll()
    {
        foreach (var button in allButtons)
        {
            button.SetSelected(false);
        }
        selectedIndex = -1;
    }

    private void EnsureButtonVisible(BuildingButton button)
    {
        if (scrollRect == null)
            return;

        Canvas.ForceUpdateCanvases();

        RectTransform buttonRect = button.GetComponent<RectTransform>();
        RectTransform contentRect = contentParent as RectTransform;
        RectTransform viewportRect = scrollRect.viewport;

        float buttonY = -buttonRect.anchoredPosition.y;
        float contentHeight = contentRect.rect.height;
        float viewportHeight = viewportRect.rect.height;

        if (contentHeight > viewportHeight)
        {
            float normalizedY = buttonY / (contentHeight - viewportHeight);
            scrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01(normalizedY);
        }
    }

    private int GetColumnsPerRow()
    {
        if (categoryUIs.Count > 0 && categoryUIs[0].ButtonContainer != null)
        {
            var grid = categoryUIs[0].ButtonContainer.GetComponent<GridLayoutGroup>();
            if (grid != null && grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
            {
                return grid.constraintCount;
            }
        }
        return 4;
    }

    private void OnBuildingButtonClicked(ObjectData building)
    {
        if (building == null)
            return;

        // 자원 체크
        if (building.ConstructionCosts != null && building.ConstructionCosts.Length > 0)
        {
            if (ResourceManager.Instance != null && !ResourceManager.Instance.CanAfford(building.ConstructionCosts))
            {
                Debug.Log($"[BuildingUI] 자원 부족: {building.Name}");
                return;
            }
        }

        // UI 닫고 배치 시작
        Close();
        StartPlacing(building.ID);
    }

    private void OnBuildingButtonHovered(int index)
    {
        isMouseOverButton = true;
        SelectButtonByMouse(index);
    }

    /// <summary>
    /// 마우스가 버튼에서 나갔을 때 호출
    /// </summary>
    public void OnBuildingButtonUnhovered()
    {
        isMouseOverButton = false;
    }

    private void StartPlacing(int buildingID)
    {
        isPlacing = true;
        currentPlacingID = buildingID;

        if (placementSystem != null)
            placementSystem.StartPlacement(buildingID);
    }

    public void CancelPlacing()
    {
        isPlacing = false;
        currentPlacingID = -1;
    }

    public void OnBuildingPlaced()
    {
        if (currentPlacingID >= 0)
        {
            ObjectData data = database.GetObjectByID(currentPlacingID);
            if (data != null && data.ConstructionCosts != null)
            {
                if (ResourceManager.Instance != null && !ResourceManager.Instance.CanAfford(data.ConstructionCosts))
                {
                    CancelPlacing();
                    Debug.Log("[BuildingUI] 자원 소진, 배치 종료");
                }
            }
        }
    }

    public void RefreshUI()
    {
        GenerateUI();
    }
}