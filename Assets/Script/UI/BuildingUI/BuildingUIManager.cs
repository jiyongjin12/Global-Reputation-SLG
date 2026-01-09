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
/// - ★ 이동모드(C키) 연동: 패널 숨김 + ESC/B키로 복귀
/// </summary>
public class BuildingUIManager : MonoBehaviour, IEscapableUI
{
    [Header("=== References ===")]
    [SerializeField] private ObjectsDatabaseSO database;
    [SerializeField] private PlacementSystem placementSystem;

    [Header("=== UI References ===")]
    [SerializeField] private GameObject buildingPanel;
    [SerializeField] private Transform contentParent;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private BuildingInfoPanel infoPanel;

    private RectTransform panelRectTransform;

    [Header("=== Prefabs ===")]
    [SerializeField] private GameObject categoryPrefab;
    [SerializeField] private GameObject buildingButtonPrefab;

    [Header("=== Post Processing ===")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private float blurFocalLengthMin = 0f;
    [SerializeField] private float blurFocalLengthMax = 50f;

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float closedPosX = 1400f;
    [SerializeField] private float openedPosX = 0f;
    [SerializeField] private float slideDuration = 0.4f;
    [SerializeField] private Ease slideEase = Ease.OutQuart;

    [Header("=== 카드 페이드인 설정 ===")]
    [SerializeField] private float cardFadeInDelay = 0.02f;
    [SerializeField] private float cardFadeInStartDelay = 0.1f;

    [Header("=== 게임 속도 설정 ===")]
    [SerializeField] private float slowTimeScale = 0.05f;
    [SerializeField] private float normalTimeScale = 1f;

    [Header("=== ESC 우선순위 ===")]
    [SerializeField] private int escapePriority = 90;

    // Depth of Field 참조
    private DepthOfField depthOfField;

    // 트윈
    private Tweener panelTween;
    private Tweener blurTween;

    // 내부 상태
    private List<CategoryUI> categoryUIs = new List<CategoryUI>();
    private List<BuildingButton> allButtons = new List<BuildingButton>();
    private int selectedIndex = 0;
    private bool isOpen = false;

    // ★ 이동 모드 상태
    private bool isInMoveMode = false;
    private bool wasPanelOpenBeforeMove = false;

    // 마우스 상태
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

        if (buildingPanel != null)
            panelRectTransform = buildingPanel.GetComponent<RectTransform>();

        InitializeDepthOfField();
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
    }

    private void OnDestroy()
    {
        KillAllTweens();

        // ★ TimeScale 복구 (중요!)
        Time.timeScale = normalTimeScale;

        if (depthOfField != null)
            depthOfField.focalLength.value = blurFocalLengthMin;

        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.UnregisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered -= HandleGameAction;
        }
    }

    private void Update()
    {
        // UI 열려있을 때 네비게이션
        if (isOpen && !isInMoveMode)
        {
            HandleNavigationInput();
        }
    }

    // ==================== 초기화 ====================

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

    private void InitializeClosedState()
    {
        if (panelRectTransform != null)
        {
            Vector2 pos = panelRectTransform.anchoredPosition;
            pos.x = closedPosX;
            panelRectTransform.anchoredPosition = pos;
        }

        if (buildingPanel != null)
            buildingPanel.SetActive(false);

        if (depthOfField != null)
            depthOfField.focalLength.value = blurFocalLengthMin;

        isOpen = false;
    }

    // ==================== 입력 처리 ====================

    private void HandleGameAction(GameAction action)
    {
        switch (action)
        {
            case GameAction.OpenBuilding:
                HandleBuildingKey();
                break;

            case GameAction.MoveBuilding:
                HandleMoveBuildingKey();
                break;

            case GameAction.DeleteBuilding:
                HandleDeleteBuildingKey();
                break;
        }
    }

    private void HandleBuildingKey()
    {
        Debug.Log($"[BuildingUIManager] B키 - isOpen:{isOpen}, isInMoveMode:{isInMoveMode}, isPlacing:{isPlacing}");

        // 1. 이동 모드 중이면 → 이동 모드 종료 + Building 패널 복귀
        if (isInMoveMode)
        {
            ExitMoveMode(returnToPanel: true);
            return;
        }

        // 2. 배치 중이면 → 배치 취소 + Building 패널 복귀
        if (isPlacing)
        {
            CancelPlacing();
            Open();
            return;
        }

        // 3. 일반 토글
        if (isOpen)
            Close();
        else
            Open();
    }

    private void HandleMoveBuildingKey()
    {
        Debug.Log($"[BuildingUIManager] C키 - isOpen:{isOpen}, isInMoveMode:{isInMoveMode}");

        // 이미 이동 모드면 → 이동 모드 종료 + Building 패널 복귀
        if (isInMoveMode)
        {
            ExitMoveMode(returnToPanel: true);
            return;
        }

        // Building 패널 열려있을 때만 이동 모드 진입
        if (isOpen)
        {
            EnterMoveMode();
        }
    }

    private void HandleDeleteBuildingKey()
    {
        Debug.Log("[BuildingUIManager] X키 (삭제 모드) - 미구현");
    }

    // ==================== 이동 모드 ====================

    private void EnterMoveMode()
    {
        Debug.Log("[BuildingUIManager] 이동 모드 진입");

        wasPanelOpenBeforeMove = isOpen;
        isInMoveMode = true;

        // 패널 숨기기 (TimeScale/DOF는 유지!)
        HidePanelOnly();

        // PlacementSystem에 이동 모드 시작 요청
        if (placementSystem != null)
        {
            placementSystem.StartMoving();
        }

        // ESC 시 ExitMoveMode 호출되도록 등록
        GameInputManager.Instance?.SetCurrentMode(() => ExitMoveMode(returnToPanel: true), 60);
    }

    public void ExitMoveMode(bool returnToPanel)
    {
        Debug.Log($"[BuildingUIManager] 이동 모드 종료, returnToPanel: {returnToPanel}");

        isInMoveMode = false;

        // PlacementSystem 이동 모드 종료
        if (placementSystem != null)
        {
            placementSystem.StopPlacement();
        }

        // GameInputManager 모드 해제
        GameInputManager.Instance?.ClearCurrentMode();

        // 패널로 복귀
        if (returnToPanel && wasPanelOpenBeforeMove)
        {
            ShowPanelOnly();
        }
    }

    private void HidePanelOnly()
    {
        if (panelRectTransform != null)
        {
            panelTween?.Kill();
            panelTween = panelRectTransform
                .DOAnchorPosX(closedPosX, slideDuration * 0.5f)
                .SetEase(slideEase)
                .SetUpdate(true)
                .OnComplete(() => buildingPanel.SetActive(false));
        }

        DeselectAll();
        if (infoPanel != null)
            infoPanel.Hide();
    }

    private void ShowPanelOnly()
    {
        buildingPanel.SetActive(true);
        RefreshAllButtonsAffordability();

        if (allButtons.Count > 0)
        {
            selectedIndex = 0;
            allButtons[0].SetSelected(true);
            UpdateInfoPanel();
        }

        if (panelRectTransform != null)
        {
            panelTween?.Kill();
            panelTween = panelRectTransform
                .DOAnchorPosX(openedPosX, slideDuration)
                .SetEase(slideEase)
                .SetUpdate(true);
        }

        PlayCardsFadeIn();
    }

    // ==================== 열기/닫기 ====================

    public void Open()
    {
        if (isOpen || isPlacing) return;
        isOpen = true;

        Debug.Log("[BuildingUIManager] Open() 호출됨");

        KillAllTweens();
        buildingPanel.SetActive(true);
        RefreshAllButtonsAffordability();

        if (allButtons.Count > 0)
        {
            selectedIndex = 0;
            allButtons[0].SetSelected(true);
            UpdateInfoPanel();
        }

        // 1. 패널 슬라이드
        if (panelRectTransform != null)
        {
            panelTween = panelRectTransform
                .DOAnchorPosX(openedPosX, slideDuration)
                .SetEase(slideEase)
                .SetUpdate(true);
        }

        // 2. Depth of Field 블러
        if (depthOfField != null)
        {
            blurTween = DOTween.To(
                () => depthOfField.focalLength.value,
                x => depthOfField.focalLength.value = x,
                blurFocalLengthMax,
                slideDuration
            ).SetUpdate(true);
        }

        // ★ 3. TimeScale 즉시 설정 (DOTween 대신 직접!)
        Time.timeScale = slowTimeScale;
        Debug.Log($"[BuildingUIManager] TimeScale 설정: {slowTimeScale}");

        PlayCardsFadeIn();
        OnPanelOpened?.Invoke();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        Debug.Log("[BuildingUIManager] Close() 호출됨");

        // 이동 모드 중이면 이동 모드도 종료
        if (isInMoveMode)
        {
            ExitMoveMode(returnToPanel: false);
        }

        KillAllTweens();
        DeselectAll();

        if (infoPanel != null)
            infoPanel.Hide();

        // 1. 패널 슬라이드
        if (panelRectTransform != null)
        {
            panelTween = panelRectTransform
                .DOAnchorPosX(closedPosX, slideDuration)
                .SetEase(slideEase)
                .SetUpdate(true)
                .OnComplete(() => buildingPanel.SetActive(false));
        }

        // 2. Depth of Field 복구
        if (depthOfField != null)
        {
            blurTween = DOTween.To(
                () => depthOfField.focalLength.value,
                x => depthOfField.focalLength.value = x,
                blurFocalLengthMin,
                slideDuration
            ).SetUpdate(true);
        }

        // ★ 3. TimeScale 즉시 복구 (DOTween 대신 직접!)
        Time.timeScale = normalTimeScale;
        Debug.Log($"[BuildingUIManager] TimeScale 복구: {normalTimeScale}");

        OnPanelClosed?.Invoke();
    }

    private void KillAllTweens()
    {
        panelTween?.Kill();
        blurTween?.Kill();
    }

    private void PlayCardsFadeIn()
    {
        for (int i = 0; i < allButtons.Count; i++)
        {
            float delay = cardFadeInStartDelay + (i * cardFadeInDelay);
            allButtons[i].PlayFadeInAnimation(delay);
        }
    }

    // ==================== UI 생성 ====================

    private void GenerateUI()
    {
        Debug.Log("[BuildingUIManager] === GenerateUI 시작 ===");

        if (database == null || categoryPrefab == null || buildingButtonPrefab == null)
        {
            Debug.LogError("[BuildingUIManager] 필수 참조가 null!");
            return;
        }

        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }
        categoryUIs.Clear();
        allButtons.Clear();

        List<BuildingCategory> categories = database.GetAllCategories();

        foreach (var category in categories)
        {
            List<ObjectData> buildings = database.GetObjectsByCategory(category);
            buildings = buildings.FindAll(b => b.ShowInBuildMenu);

            if (buildings.Count == 0)
                continue;

            GameObject categoryObj = Instantiate(categoryPrefab, contentParent);
            CategoryUI categoryUI = categoryObj.GetComponent<CategoryUI>();

            if (categoryUI == null)
                continue;

            categoryUI.Initialize(category, buildings.Count);
            categoryUIs.Add(categoryUI);

            foreach (var building in buildings)
            {
                GameObject buttonObj = Instantiate(buildingButtonPrefab, categoryUI.ButtonContainer);
                BuildingButton button = buttonObj.GetComponent<BuildingButton>();

                if (button == null)
                    continue;

                int buttonIndex = allButtons.Count;
                button.Initialize(building, buttonIndex, OnBuildingButtonClicked, OnBuildingButtonHovered);
                allButtons.Add(button);
            }
        }

        Debug.Log($"[BuildingUIManager] === 완료: {allButtons.Count}개 버튼 ===");
    }

    private void RefreshAllButtonsAffordability()
    {
        foreach (var button in allButtons)
        {
            button.UpdateAffordability();
        }
    }

    // ==================== 네비게이션 ====================

    private void HandleNavigationInput()
    {
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            SelectButtonByKeyboard(selectedIndex + 1);
        else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            SelectButtonByKeyboard(selectedIndex - 1);
        else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            SelectButtonByKeyboard(selectedIndex + GetColumnsPerRow());
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            SelectButtonByKeyboard(selectedIndex - GetColumnsPerRow());

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
        {
            if (selectedIndex >= 0 && selectedIndex < allButtons.Count)
            {
                allButtons[selectedIndex].OnClick();
            }
        }
    }

    private void SelectButtonByKeyboard(int index)
    {
        if (allButtons.Count == 0)
            return;

        index = Mathf.Clamp(index, 0, allButtons.Count - 1);

        if (index == selectedIndex)
            return;

        if (selectedIndex >= 0 && selectedIndex < allButtons.Count)
            allButtons[selectedIndex].SetSelected(false);

        selectedIndex = index;
        allButtons[selectedIndex].SetSelected(true);
        EnsureButtonVisible(allButtons[selectedIndex]);
        UpdateInfoPanel();
    }

    private void SelectButtonByMouse(int index)
    {
        if (allButtons.Count == 0)
            return;

        index = Mathf.Clamp(index, 0, allButtons.Count - 1);

        if (index == selectedIndex)
            return;

        if (selectedIndex >= 0 && selectedIndex < allButtons.Count)
            allButtons[selectedIndex].SetSelected(false);

        selectedIndex = index;
        allButtons[selectedIndex].SetSelected(true);
        UpdateInfoPanel();
    }

    private void UpdateInfoPanel(ObjectData building = null)
    {
        if (infoPanel == null)
            return;

        if (building == null && selectedIndex >= 0 && selectedIndex < allButtons.Count)
            building = allButtons[selectedIndex].BuildingData;

        if (building != null)
            infoPanel.ShowBuilding(building);
        else
            infoPanel.Hide();
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

    // ==================== 콜백 ====================

    private void OnBuildingButtonClicked(ObjectData building)
    {
        if (building == null)
            return;

        if (building.ConstructionCosts != null && building.ConstructionCosts.Length > 0)
        {
            if (ResourceManager.Instance != null && !ResourceManager.Instance.CanAfford(building.ConstructionCosts))
            {
                Debug.Log($"[BuildingUI] 자원 부족: {building.Name}");
                return;
            }
        }

        Close();
        StartPlacing(building.ID);
    }

    private void OnBuildingButtonHovered(int index)
    {
        isMouseOverButton = true;
        SelectButtonByMouse(index);
    }

    public void OnBuildingButtonUnhovered()
    {
        isMouseOverButton = false;
    }

    // ==================== 배치 ====================

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