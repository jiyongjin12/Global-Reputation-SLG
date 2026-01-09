using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DG.Tweening;
using System;

/// <summary>
/// 채팅 패널 열기/닫기 컨트롤러
/// - EnterKey/버튼으로 열기
/// - ESC키/버튼으로 닫기 (GameInputManager 연동)
/// - DOTween 슬라이드 애니메이션
/// - Depth of Field 블러 + 게임 속도 조절
/// 
/// ★ IEscapableUI 구현: ESC 우선순위 시스템 참여
/// </summary>
public class ChatPanelController : MonoBehaviour, IEscapableUI
{
    [Header("=== UI 참조 ===")]
    [SerializeField] private RectTransform chatPanel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;

    [Header("=== Post Processing ===")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private float blurFocalLengthMin = 0f;
    [SerializeField] private float blurFocalLengthMax = 50f;

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float closedPosX = -1400f;
    [SerializeField] private float openedPosX = 0f;
    [SerializeField] private float slideDuration = 0.4f;
    [SerializeField] private Ease slideEase = Ease.OutQuart;

    [Header("=== 게임 속도 설정 ===")]
    [SerializeField] private float slowTimeScale = 0.05f;
    [SerializeField] private float normalTimeScale = 1f;

    [Header("=== ESC 우선순위 ===")]
    [SerializeField] private int escapePriority = 100;

    [Header("=== 상태 ===")]
    [SerializeField] private bool isOpen = false;

    // Depth of Field 참조
    private DepthOfField depthOfField;

    // 트윈
    private Tweener panelTween;
    private Tweener blurTween;

    // 이벤트
    public event Action OnPanelOpened;
    public event Action OnPanelClosed;

    // ==================== IEscapableUI 구현 ====================

    public bool IsOpen => isOpen;
    public int EscapePriority => escapePriority;

    // ★ Singleton Instance
    public static ChatPanelController Instance { get; private set; }

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);

        InitializeDepthOfField();
        InitializeClosedState();
    }

    private void Start()
    {
        // 버튼 이벤트 연결
        if (openButton != null)
            openButton.onClick.AddListener(Open);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        // ★ GameInputManager에 등록
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.RegisterEscapableUI(this);
            GameInputManager.Instance.OnActionTriggered += HandleGameAction;
            Debug.Log("[ChatPanelController] GameInputManager에 등록됨");
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

    // ==================== 입력 처리 ====================

    private void HandleGameAction(GameAction action)
    {
        switch (action)
        {
            case GameAction.OpenChat:
                // 다른 UI가 열려있으면 무시
                if (BuildingUIManager.Instance != null && BuildingUIManager.Instance.IsOpen)
                    return;
                if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen)
                    return;

                if (!isOpen)
                    Open();
                break;
        }
    }

    // ==================== 초기화 ====================

    private void InitializeDepthOfField()
    {
        if (globalVolume == null)
            globalVolume = FindObjectOfType<Volume>();

        if (globalVolume != null && globalVolume.profile != null)
        {
            if (!globalVolume.profile.TryGet(out depthOfField))
            {
                Debug.LogWarning("[ChatPanelController] Global Volume에 Depth of Field가 없습니다!");
            }
        }
    }

    private void InitializeClosedState()
    {
        if (chatPanel != null)
        {
            Vector2 pos = chatPanel.anchoredPosition;
            pos.x = closedPosX;
            chatPanel.anchoredPosition = pos;
            chatPanel.gameObject.SetActive(false);
        }

        if (depthOfField != null)
            depthOfField.focalLength.value = blurFocalLengthMin;

        UpdateButtonVisibility();
        isOpen = false;
    }

    // ==================== 열기/닫기 ====================

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;

        Debug.Log("[ChatPanelController] Open() 호출됨");

        KillAllTweens();
        chatPanel.gameObject.SetActive(true);
        UpdateButtonVisibility();

        // 1. 패널 슬라이드
        panelTween = chatPanel
            .DOAnchorPosX(openedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true);

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

        // ★ 3. TimeScale 즉시 설정
        Time.timeScale = slowTimeScale;
        Debug.Log($"[ChatPanelController] TimeScale 설정: {slowTimeScale}");

        OnPanelOpened?.Invoke();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        Debug.Log("[ChatPanelController] Close() 호출됨");

        KillAllTweens();
        UpdateButtonVisibility();

        // 1. 패널 슬라이드
        panelTween = chatPanel
            .DOAnchorPosX(closedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true)
            .OnComplete(() => chatPanel.gameObject.SetActive(false));

        // 2. Depth of Field 복구
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

        // ★ 3. TimeScale 즉시 복구
        Time.timeScale = normalTimeScale;
        Debug.Log($"[ChatPanelController] TimeScale 복구: {normalTimeScale}");

        OnPanelClosed?.Invoke();
    }

    // ==================== 유틸리티 ====================

    private void UpdateButtonVisibility()
    {
        if (openButton != null)
            openButton.gameObject.SetActive(!isOpen);

        if (closeButton != null)
            closeButton.gameObject.SetActive(isOpen);
    }

    private void KillAllTweens()
    {
        panelTween?.Kill();
        blurTween?.Kill();
    }
}