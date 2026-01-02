using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal; // URP 사용 시
// using UnityEngine.Rendering.HighDefinition; // HDRP 사용 시
using DG.Tweening;
using System;

/// <summary>
/// 채팅 패널 열기/닫기 컨트롤러
/// - Enter키/버튼으로 열기
/// - ESC키/버튼으로 닫기 (GameInputManager 연동)
/// - DOTween 슬라이드 애니메이션
/// - Depth of Field 블러 + 게임 속도 조절
/// 
/// ★ IEscapableUI 구현: ESC 우선순위 시스템 참여
/// </summary>
public class ChatPanelController : MonoBehaviour, IEscapableUI
{
    [Header("=== UI 참조 ===")]
    [SerializeField] private RectTransform chatPanel;           // 채팅 패널
    [SerializeField] private Button openButton;                 // 열기 버튼
    [SerializeField] private Button closeButton;                // 닫기 버튼 (패널 내부)

    [Header("=== Post Processing ===")]
    [SerializeField] private Volume globalVolume;               // Global Volume 참조
    [SerializeField] private float blurFocalLengthMin = 0f;     // 닫힌 상태 (블러 없음)
    [SerializeField] private float blurFocalLengthMax = 50f;    // 열린 상태 (블러 최대)

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float closedPosX = -1400f;         // 닫힌 상태 X 위치
    [SerializeField] private float openedPosX = 0f;             // 열린 상태 X 위치
    [SerializeField] private float slideDuration = 0.4f;        // 슬라이드 애니메이션 시간
    [SerializeField] private Ease slideEase = Ease.OutQuart;    // 슬라이드 이징

    [Header("=== 게임 속도 설정 ===")]
    [SerializeField] private float slowTimeScale = 0.05f;       // 열렸을 때 게임 속도
    [SerializeField] private float normalTimeScale = 1f;        // 닫혔을 때 게임 속도
    [SerializeField] private float timeScaleDuration = 0.3f;    // 속도 변경 시간

    [Header("=== ESC 우선순위 ===")]
    [Tooltip("높을수록 ESC 시 먼저 닫힘 (채팅창: 100, 설정창: 50)")]
    [SerializeField] private int escapePriority = 100;

    [Header("=== 상태 ===")]
    [SerializeField] private bool isOpen = false;

    // Depth of Field 참조
    private DepthOfField depthOfField;

    // 현재 진행 중인 트윈
    private Tweener panelTween;
    private Tweener blurTween;
    private Tweener timeTween;

    // 이벤트
    public event Action OnPanelOpened;
    public event Action OnPanelClosed;

    // ==================== IEscapableUI 구현 ====================

    public bool IsOpen => isOpen;
    public int EscapePriority => escapePriority;

    // IEscapableUI.Close()는 아래 Close() 메서드 사용

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        // Depth of Field 가져오기
        InitializeDepthOfField();

        // 초기 상태 설정 (닫힌 상태)
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
        }
        else
        {
            Debug.LogWarning("[ChatPanelController] GameInputManager가 없습니다. 직접 입력 처리합니다.");
        }
    }

    private void Update()
    {
        // ★ GameInputManager가 없을 때만 직접 처리 (폴백)
        if (GameInputManager.Instance == null)
        {
            HandleInputFallback();
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

    // ==================== 입력 처리 ====================

    /// <summary>
    /// ★ GameInputManager에서 액션 처리
    /// </summary>
    private void HandleGameAction(GameAction action)
    {
        switch (action)
        {
            case GameAction.OpenChat:
                if (!isOpen)
                    Open();
                break;

                // ESC는 GameInputManager의 우선순위 시스템에서 처리
                // Close()가 IEscapableUI를 통해 호출됨
        }
    }

    /// <summary>
    /// GameInputManager가 없을 때 폴백 (직접 입력 처리)
    /// </summary>
    private void HandleInputFallback()
    {
        // Enter 키로 열기 (닫혀있을 때만)
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (!isOpen)
                Open();
        }

        // ESC 키로 닫기 (열려있을 때만)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isOpen)
                Close();
        }
    }

    // ==================== 초기화 ====================

    /// <summary>
    /// Depth of Field 초기화
    /// </summary>
    private void InitializeDepthOfField()
    {
        if (globalVolume == null)
        {
            // 씬에서 Global Volume 자동 찾기
            globalVolume = FindObjectOfType<Volume>();
        }

        if (globalVolume != null && globalVolume.profile != null)
        {
            // Volume Profile에서 Depth of Field 가져오기
            if (!globalVolume.profile.TryGet(out depthOfField))
            {
                Debug.LogWarning("[ChatPanelController] Global Volume에 Depth of Field가 없습니다!");
            }
        }
        else
        {
            Debug.LogWarning("[ChatPanelController] Global Volume을 찾을 수 없습니다!");
        }
    }

    /// <summary>
    /// 초기 닫힌 상태 설정
    /// </summary>
    private void InitializeClosedState()
    {
        // 패널 위치
        if (chatPanel != null)
        {
            Vector2 pos = chatPanel.anchoredPosition;
            pos.x = closedPosX;
            chatPanel.anchoredPosition = pos;
            chatPanel.gameObject.SetActive(false);
        }

        // Depth of Field 초기값
        if (depthOfField != null)
        {
            depthOfField.focalLength.value = blurFocalLengthMin;
        }

        // 버튼 상태
        UpdateButtonVisibility();

        isOpen = false;
    }

    // ==================== 열기/닫기 ====================

    /// <summary>
    /// 패널 열기
    /// </summary>
    public void Open()
    {
        if (isOpen) return;
        isOpen = true;

        // 기존 트윈 정지
        KillAllTweens();

        // 패널 활성화
        chatPanel.gameObject.SetActive(true);

        // 버튼 상태 업데이트
        UpdateButtonVisibility();

        // ===== 애니메이션 시작 =====

        // 1. 패널 슬라이드
        panelTween = chatPanel
            .DOAnchorPosX(openedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true); // TimeScale 영향 안 받음

        // 2. Depth of Field 블러 (0 → 50)
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
    }

    /// <summary>
    /// 패널 닫기
    /// </summary>
    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        // 기존 트윈 정지
        KillAllTweens();

        // 버튼 상태 업데이트
        UpdateButtonVisibility();

        // ===== 애니메이션 시작 =====

        // 1. 패널 슬라이드
        panelTween = chatPanel
            .DOAnchorPosX(closedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                // 애니메이션 완료 후 비활성화 (최적화)
                chatPanel.gameObject.SetActive(false);
            });

        // 2. Depth of Field 블러 (50 → 0)
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
    }

    // ==================== 유틸리티 ====================

    /// <summary>
    /// 버튼 가시성 업데이트
    /// </summary>
    private void UpdateButtonVisibility()
    {
        // 열기 버튼: 닫혀있을 때만 표시
        if (openButton != null)
            openButton.gameObject.SetActive(!isOpen);

        // 닫기 버튼: 열려있을 때만 표시
        if (closeButton != null)
            closeButton.gameObject.SetActive(isOpen);
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
}