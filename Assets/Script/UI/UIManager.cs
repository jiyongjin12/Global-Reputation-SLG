using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI 패널 타입
/// </summary>
public enum UIPanelType
{
    None,
    Building,       // 건물 UI
    Chat,           // 채팅 UI
    Inventory,      // 인벤토리
    Pause,          // 일시정지 메뉴
    Settings,       // 설정
    // 필요시 추가...
}

/// <summary>
/// UI 패널 인터페이스 - 모든 UI 패널이 구현해야 함
/// </summary>
public interface IUIPanel
{
    UIPanelType PanelType { get; }
    bool IsOpen { get; }
    int Priority { get; }  // 높을수록 먼저 닫힘

    void Open();
    void Close();
    void OnEscapePressed();  // ESC 눌렀을 때 동작 (닫기 또는 다른 처리)
}

/// <summary>
/// UI 통합 관리자
/// - 여러 UI 패널 관리
/// - ESC 키 우선순위 처리
/// - 시간 조절 (TimeScale)
/// - 단축키 관리
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Time Settings")]
    [SerializeField] private float uiOpenTimeScale = 0.1f;      // UI 열렸을 때 시간 속도
    [SerializeField] private float normalTimeScale = 1f;
    [SerializeField] private float timeScaleLerpSpeed = 5f;     // 시간 전환 속도

    [Header("Shortcut Keys")]
    [SerializeField] private KeyCode buildingKey = KeyCode.B;
    [SerializeField] private KeyCode chatKey = KeyCode.T;
    [SerializeField] private KeyCode inventoryKey = KeyCode.I;
    [SerializeField] private KeyCode pauseKey = KeyCode.Escape;

    // 등록된 UI 패널들
    private Dictionary<UIPanelType, IUIPanel> registeredPanels = new Dictionary<UIPanelType, IUIPanel>();

    // 현재 열린 패널들 (스택 - 마지막에 열린 게 먼저 닫힘)
    private List<IUIPanel> openPanels = new List<IUIPanel>();

    // 목표 TimeScale
    private float targetTimeScale = 1f;

    // 입력 잠금 (특정 UI에서 입력 막을 때)
    private bool isInputLocked = false;

    public bool IsAnyPanelOpen => openPanels.Count > 0;
    public bool IsInputLocked => isInputLocked;
    public UIPanelType CurrentTopPanel => openPanels.Count > 0 ? openPanels[openPanels.Count - 1].PanelType : UIPanelType.None;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        // TimeScale 부드럽게 전환
        UpdateTimeScale();

        // 입력 잠금 상태면 단축키 무시
        if (isInputLocked)
            return;

        // ESC 키 처리 (열린 패널 닫기)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleEscapeKey();
            return;
        }

        // 단축키 처리
        HandleShortcutKeys();
    }

    /// <summary>
    /// UI 패널 등록
    /// </summary>
    public void RegisterPanel(IUIPanel panel)
    {
        if (panel == null)
            return;

        if (!registeredPanels.ContainsKey(panel.PanelType))
        {
            registeredPanels[panel.PanelType] = panel;
            Debug.Log($"[UIManager] 패널 등록: {panel.PanelType}");
        }
    }

    /// <summary>
    /// UI 패널 등록 해제
    /// </summary>
    public void UnregisterPanel(IUIPanel panel)
    {
        if (panel == null)
            return;

        if (registeredPanels.ContainsKey(panel.PanelType))
        {
            registeredPanels.Remove(panel.PanelType);
            openPanels.Remove(panel);
        }
    }

    /// <summary>
    /// 특정 패널 열기
    /// </summary>
    public void OpenPanel(UIPanelType panelType)
    {
        if (!registeredPanels.TryGetValue(panelType, out var panel))
        {
            Debug.LogWarning($"[UIManager] 등록되지 않은 패널: {panelType}");
            return;
        }

        if (panel.IsOpen)
            return;

        panel.Open();

        // 열린 패널 목록에 추가 (Priority 순으로 정렬)
        openPanels.Add(panel);
        openPanels.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        // 시간 조절
        UpdateTargetTimeScale();

        Debug.Log($"[UIManager] 패널 열림: {panelType}, 현재 열린 패널 수: {openPanels.Count}");
    }

    /// <summary>
    /// 특정 패널 닫기
    /// </summary>
    public void ClosePanel(UIPanelType panelType)
    {
        if (!registeredPanels.TryGetValue(panelType, out var panel))
            return;

        if (!panel.IsOpen)
            return;

        panel.Close();
        openPanels.Remove(panel);

        // 시간 조절
        UpdateTargetTimeScale();

        Debug.Log($"[UIManager] 패널 닫힘: {panelType}, 남은 열린 패널 수: {openPanels.Count}");
    }

    /// <summary>
    /// 패널 토글
    /// </summary>
    public void TogglePanel(UIPanelType panelType)
    {
        if (!registeredPanels.TryGetValue(panelType, out var panel))
            return;

        if (panel.IsOpen)
            ClosePanel(panelType);
        else
            OpenPanel(panelType);
    }

    /// <summary>
    /// 모든 패널 닫기
    /// </summary>
    public void CloseAllPanels()
    {
        // 복사본으로 순회 (원본 수정 방지)
        var panelsToClose = new List<IUIPanel>(openPanels);

        foreach (var panel in panelsToClose)
        {
            panel.Close();
        }

        openPanels.Clear();
        UpdateTargetTimeScale();
    }

    /// <summary>
    /// ESC 키 처리 - 가장 위에 있는 패널 닫기
    /// </summary>
    private void HandleEscapeKey()
    {
        if (openPanels.Count == 0)
        {
            // 열린 패널 없으면 일시정지 메뉴 열기 (선택사항)
            // OpenPanel(UIPanelType.Pause);
            return;
        }

        // 가장 높은 Priority 패널에 ESC 이벤트 전달
        var topPanel = openPanels[0];  // 이미 Priority 순으로 정렬됨
        topPanel.OnEscapePressed();

        // 패널이 닫혔으면 목록에서 제거
        if (!topPanel.IsOpen)
        {
            openPanels.Remove(topPanel);
            UpdateTargetTimeScale();
        }
    }

    /// <summary>
    /// 단축키 처리
    /// </summary>
    private void HandleShortcutKeys()
    {
        // 다른 패널이 열려있으면 단축키 무시 (선택사항)
        // if (IsAnyPanelOpen) return;

        if (Input.GetKeyDown(buildingKey))
        {
            TogglePanel(UIPanelType.Building);
        }
        else if (Input.GetKeyDown(chatKey))
        {
            TogglePanel(UIPanelType.Chat);
        }
        else if (Input.GetKeyDown(inventoryKey))
        {
            TogglePanel(UIPanelType.Inventory);
        }
    }

    /// <summary>
    /// 목표 TimeScale 업데이트
    /// </summary>
    private void UpdateTargetTimeScale()
    {
        if (openPanels.Count > 0)
        {
            targetTimeScale = uiOpenTimeScale;
        }
        else
        {
            targetTimeScale = normalTimeScale;
        }
    }

    /// <summary>
    /// TimeScale 부드럽게 전환
    /// </summary>
    private void UpdateTimeScale()
    {
        if (Mathf.Approximately(Time.timeScale, targetTimeScale))
            return;

        // unscaledDeltaTime 사용 (TimeScale 영향 안 받음)
        Time.timeScale = Mathf.Lerp(Time.timeScale, targetTimeScale, Time.unscaledDeltaTime * timeScaleLerpSpeed);

        // 거의 도달하면 정확히 맞춤
        if (Mathf.Abs(Time.timeScale - targetTimeScale) < 0.01f)
        {
            Time.timeScale = targetTimeScale;
        }
    }

    /// <summary>
    /// 입력 잠금 설정 (채팅 입력 중 등)
    /// </summary>
    public void SetInputLock(bool locked)
    {
        isInputLocked = locked;
    }

    /// <summary>
    /// 특정 패널이 열려있는지 확인
    /// </summary>
    public bool IsPanelOpen(UIPanelType panelType)
    {
        if (registeredPanels.TryGetValue(panelType, out var panel))
        {
            return panel.IsOpen;
        }
        return false;
    }

    /// <summary>
    /// TimeScale 직접 설정 (특수 상황용)
    /// </summary>
    public void SetTimeScale(float scale, bool immediate = false)
    {
        targetTimeScale = scale;

        if (immediate)
        {
            Time.timeScale = scale;
        }
    }
}