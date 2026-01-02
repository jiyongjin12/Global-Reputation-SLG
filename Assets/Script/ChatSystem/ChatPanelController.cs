using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System;

/// <summary>
/// 채팅 패널 열기/닫기 컨트롤러
/// - ESC키로 토글
/// - DOTween 슬라이드 애니메이션
/// - 배경 흐림 + 게임 속도 조절
/// </summary>
public class ChatPanelController : MonoBehaviour
{
    [Header("=== UI 참조 ===")]
    [SerializeField] private RectTransform chatPanel;           // 채팅 패널
    //[SerializeField] private CanvasGroup backgroundDim;         // 배경 흐림 (Image + CanvasGroup)
    [SerializeField] private Button openButton;                 // 열기 버튼
    [SerializeField] private Button closeButton;                // 닫기 버튼 (패널 내부)
    [SerializeField] private Button backgroundCloseArea;        // 빈 공간 클릭 시 닫기용

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float closedPosX = -1400f;         // 닫힌 상태 X 위치
    [SerializeField] private float openedPosX = 0f;             // 열린 상태 X 위치
    [SerializeField] private float slideDuration = 0.4f;        // 슬라이드 애니메이션 시간
    [SerializeField] private float fadeDuration = 0.3f;         // 페이드 애니메이션 시간
    [SerializeField] private Ease slideEase = Ease.OutQuart;    // 슬라이드 이징
    [SerializeField] private Ease fadeEase = Ease.OutQuad;      // 페이드 이징

    [Header("=== 게임 속도 설정 ===")]
    [SerializeField] private float slowTimeScale = 0.05f;       // 열렸을 때 게임 속도
    [SerializeField] private float normalTimeScale = 1f;        // 닫혔을 때 게임 속도
    [SerializeField] private float timeScaleDuration = 0.3f;    // 속도 변경 시간

    [Header("=== 상태 ===")]
    [SerializeField] private bool isOpen = false;

    // 현재 진행 중인 트윈
    private Tweener panelTween;
    private Tweener dimTween;
    private Tweener timeTween;

    // 이벤트
    public event Action OnPanelOpened;
    public event Action OnPanelClosed;

    private void Awake()
    {
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

        if (backgroundCloseArea != null)
            backgroundCloseArea.onClick.AddListener(Close);
    }

    private void Update()
    {
        // ESC 키로 토글
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Toggle();
        }
    }

    private void OnDestroy()
    {
        // 트윈 정리
        panelTween?.Kill();
        dimTween?.Kill();
        timeTween?.Kill();

        // TimeScale 복구 (중요!)
        Time.timeScale = normalTimeScale;
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

        // 배경 흐림
        //if (backgroundDim != null)
        //{
        //    backgroundDim.alpha = 0f;
        //    backgroundDim.gameObject.SetActive(false);
        //}

        // 버튼 상태
        UpdateButtonVisibility();

        isOpen = false;
    }

    /// <summary>
    /// 토글
    /// </summary>
    public void Toggle()
    {
        if (isOpen)
            Close();
        else
            Open();
    }

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
        //if (backgroundDim != null)
        //    backgroundDim.gameObject.SetActive(true);

        // 버튼 상태 업데이트
        UpdateButtonVisibility();

        // ===== 애니메이션 시작 =====

        // 1. 패널 슬라이드
        panelTween = chatPanel
            .DOAnchorPosX(openedPosX, slideDuration)
            .SetEase(slideEase)
            .SetUpdate(true); // TimeScale 영향 안 받음

        // 2. 배경 흐림 페이드 인
        //if (backgroundDim != null)
        //{
        //    dimTween = backgroundDim
        //        .DOFade(1f, fadeDuration)
        //        .SetEase(fadeEase)
        //        .SetUpdate(true);
        //}

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

        // 2. 배경 흐림 페이드 아웃
        //if (backgroundDim != null)
        //{
        //    dimTween = backgroundDim
        //        .DOFade(0f, fadeDuration)
        //        .SetEase(fadeEase)
        //        .SetUpdate(true)
        //        .OnComplete(() =>
        //        {
        //            backgroundDim.gameObject.SetActive(false);
        //        });
        //}

        // 3. 게임 속도 복구
        timeTween = DOTween
            .To(() => Time.timeScale, x => Time.timeScale = x, normalTimeScale, timeScaleDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        OnPanelClosed?.Invoke();
    }

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
        dimTween?.Kill();
        timeTween?.Kill();
    }

    /// <summary>
    /// 현재 열린 상태인지
    /// </summary>
    public bool IsOpen => isOpen;
}