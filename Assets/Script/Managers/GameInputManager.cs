using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 액션 종류
/// </summary>
public enum GameAction
{
    // UI 관련
    OpenChat,           // 채팅 열기 (Enter)
    CloseUI,            // UI 닫기 / 취소 (ESC)
    OpenSettings,       // 설정 열기

    // 건물 관련
    MoveBuilding,       // 건물 이동 모드 (C)
    DeleteBuilding,     // 건물 삭제 모드 (X)
    RotateBuilding,     // 건물 회전 (R)

    // 게임플레이
    Pause,              // 일시정지 (P)
    QuickSave,          // 빠른 저장 (F5)
    QuickLoad,          // 빠른 로드 (F9)
}

/// <summary>
/// ESC로 닫을 수 있는 UI 인터페이스
/// </summary>
public interface IEscapableUI
{
    bool IsOpen { get; }
    void Close();
    int EscapePriority { get; } // 높을수록 먼저 닫힘
}

/// <summary>
/// 게임 입력 중앙 관리자
/// - 단축키 바인딩 관리 (설정에서 변경 가능)
/// - ESC 우선순위 시스템
/// - PlayerPrefs로 저장/로드
/// </summary>
public class GameInputManager : MonoBehaviour
{
    public static GameInputManager Instance { get; private set; }

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // ==================== 키 바인딩 ====================

    /// <summary>
    /// 기본 키 바인딩
    /// </summary>
    private readonly Dictionary<GameAction, KeyCode> defaultBindings = new()
    {
        { GameAction.OpenChat, KeyCode.Return },
        { GameAction.CloseUI, KeyCode.Escape },
        { GameAction.OpenSettings, KeyCode.Escape },
        { GameAction.MoveBuilding, KeyCode.C },
        { GameAction.DeleteBuilding, KeyCode.X },
        { GameAction.RotateBuilding, KeyCode.R },
        { GameAction.Pause, KeyCode.P },
        { GameAction.QuickSave, KeyCode.F5 },
        { GameAction.QuickLoad, KeyCode.F9 },
    };

    /// <summary>
    /// 현재 키 바인딩 (설정에서 변경 가능)
    /// </summary>
    private Dictionary<GameAction, KeyCode> currentBindings = new();

    // ==================== ESC 우선순위 시스템 ====================

    /// <summary>
    /// ESC로 닫을 수 있는 UI 목록
    /// </summary>
    private List<IEscapableUI> escapableUIs = new();

    /// <summary>
    /// 현재 활성화된 모드 (배치, 이동, 삭제 등)
    /// </summary>
    private Action currentModeExitAction = null;
    private int currentModePriority = 0;

    // ==================== 이벤트 ====================

    public event Action<GameAction> OnActionTriggered;
    public event Action OnEscapeHandled;        // ESC가 처리됨 (UI 닫힘)
    public event Action OnEscapeUnhandled;      // ESC가 처리 안 됨 (설정 열기 등)
    public event Action<GameAction, KeyCode> OnKeyBindingChanged;

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadKeyBindings();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        // ESC 키 특별 처리 (우선순위 시스템)
        if (Input.GetKeyDown(GetKeyForAction(GameAction.CloseUI)))
        {
            HandleEscapeKey();
            return;
        }

        // 다른 키 입력 체크
        foreach (var action in currentBindings.Keys)
        {
            if (action == GameAction.CloseUI || action == GameAction.OpenSettings)
                continue; // ESC는 위에서 처리

            if (Input.GetKeyDown(currentBindings[action]))
            {
                if (showDebugLogs)
                    Debug.Log($"[GameInputManager] Action: {action}");

                OnActionTriggered?.Invoke(action);
            }
        }
    }

    // ==================== ESC 우선순위 처리 ====================

    /// <summary>
    /// ESC 키 처리 (우선순위 순서대로)
    /// </summary>
    private void HandleEscapeKey()
    {
        // 1. 등록된 UI 중 열린 것 찾기 (우선순위 높은 순)
        escapableUIs.Sort((a, b) => b.EscapePriority.CompareTo(a.EscapePriority));

        foreach (var ui in escapableUIs)
        {
            if (ui != null && ui.IsOpen)
            {
                ui.Close();

                if (showDebugLogs)
                    Debug.Log($"[GameInputManager] ESC: UI 닫음 (Priority: {ui.EscapePriority})");

                OnEscapeHandled?.Invoke();
                return; // 하나만 닫고 종료
            }
        }

        // 2. 현재 모드 취소 (배치/이동/삭제 모드)
        if (currentModeExitAction != null)
        {
            currentModeExitAction.Invoke();
            ClearCurrentMode();

            if (showDebugLogs)
                Debug.Log("[GameInputManager] ESC: 모드 취소");

            OnEscapeHandled?.Invoke();
            return;
        }

        // 3. 아무것도 닫을 게 없으면 설정 열기 등
        if (showDebugLogs)
            Debug.Log("[GameInputManager] ESC: 처리할 UI 없음 → 설정 열기");

        OnEscapeUnhandled?.Invoke();
        OnActionTriggered?.Invoke(GameAction.OpenSettings);
    }

    // ==================== UI 등록/해제 ====================

    /// <summary>
    /// ESC로 닫을 수 있는 UI 등록
    /// </summary>
    public void RegisterEscapableUI(IEscapableUI ui)
    {
        if (!escapableUIs.Contains(ui))
        {
            escapableUIs.Add(ui);

            if (showDebugLogs)
                Debug.Log($"[GameInputManager] UI 등록: Priority {ui.EscapePriority}");
        }
    }

    /// <summary>
    /// ESC로 닫을 수 있는 UI 해제
    /// </summary>
    public void UnregisterEscapableUI(IEscapableUI ui)
    {
        escapableUIs.Remove(ui);
    }

    /// <summary>
    /// 현재 모드 설정 (배치/이동/삭제 모드 진입 시)
    /// </summary>
    public void SetCurrentMode(Action exitAction, int priority = 50)
    {
        currentModeExitAction = exitAction;
        currentModePriority = priority;

        if (showDebugLogs)
            Debug.Log($"[GameInputManager] 모드 설정: Priority {priority}");
    }

    /// <summary>
    /// 현재 모드 해제
    /// </summary>
    public void ClearCurrentMode()
    {
        currentModeExitAction = null;
        currentModePriority = 0;
    }

    // ==================== 키 바인딩 관리 ====================

    /// <summary>
    /// 특정 액션의 키 가져오기
    /// </summary>
    public KeyCode GetKeyForAction(GameAction action)
    {
        return currentBindings.TryGetValue(action, out var key) ? key : KeyCode.None;
    }

    /// <summary>
    /// 특정 액션이 눌렸는지 확인
    /// </summary>
    public bool GetActionDown(GameAction action)
    {
        var key = GetKeyForAction(action);
        return key != KeyCode.None && Input.GetKeyDown(key);
    }

    /// <summary>
    /// 특정 액션이 눌려있는지 확인
    /// </summary>
    public bool GetAction(GameAction action)
    {
        var key = GetKeyForAction(action);
        return key != KeyCode.None && Input.GetKey(key);
    }

    /// <summary>
    /// 키 바인딩 변경
    /// </summary>
    public bool SetKeyBinding(GameAction action, KeyCode newKey)
    {
        // 중복 체크 (같은 키를 다른 액션에서 사용 중인지)
        foreach (var kvp in currentBindings)
        {
            if (kvp.Key != action && kvp.Value == newKey)
            {
                Debug.LogWarning($"[GameInputManager] 키 중복: {newKey}는 이미 {kvp.Key}에서 사용 중");
                return false;
            }
        }

        currentBindings[action] = newKey;
        SaveKeyBindings();
        OnKeyBindingChanged?.Invoke(action, newKey);

        Debug.Log($"[GameInputManager] 키 바인딩 변경: {action} → {newKey}");
        return true;
    }

    /// <summary>
    /// 기본 키 바인딩으로 초기화
    /// </summary>
    public void ResetToDefaultBindings()
    {
        currentBindings.Clear();
        foreach (var kvp in defaultBindings)
        {
            currentBindings[kvp.Key] = kvp.Value;
        }
        SaveKeyBindings();

        Debug.Log("[GameInputManager] 키 바인딩 초기화됨");
    }

    /// <summary>
    /// 모든 키 바인딩 가져오기 (설정 UI용)
    /// </summary>
    public Dictionary<GameAction, KeyCode> GetAllBindings()
    {
        return new Dictionary<GameAction, KeyCode>(currentBindings);
    }

    /// <summary>
    /// 기본 키 바인딩 가져오기
    /// </summary>
    public KeyCode GetDefaultKey(GameAction action)
    {
        return defaultBindings.TryGetValue(action, out var key) ? key : KeyCode.None;
    }

    // ==================== 저장/로드 ====================

    private void SaveKeyBindings()
    {
        foreach (var kvp in currentBindings)
        {
            PlayerPrefs.SetInt($"KeyBinding_{kvp.Key}", (int)kvp.Value);
        }
        PlayerPrefs.Save();
    }

    private void LoadKeyBindings()
    {
        currentBindings.Clear();

        foreach (var action in defaultBindings.Keys)
        {
            string key = $"KeyBinding_{action}";

            if (PlayerPrefs.HasKey(key))
            {
                currentBindings[action] = (KeyCode)PlayerPrefs.GetInt(key);
            }
            else
            {
                currentBindings[action] = defaultBindings[action];
            }
        }

        if (showDebugLogs)
            Debug.Log("[GameInputManager] 키 바인딩 로드됨");
    }

    // ==================== 헬퍼 ====================

    /// <summary>
    /// 키 이름 가져오기 (UI 표시용)
    /// </summary>
    public static string GetKeyDisplayName(KeyCode key)
    {
        return key switch
        {
            KeyCode.Return => "Enter",
            KeyCode.KeypadEnter => "Enter (Numpad)",
            KeyCode.Escape => "ESC",
            KeyCode.Space => "Space",
            KeyCode.LeftShift => "L-Shift",
            KeyCode.RightShift => "R-Shift",
            KeyCode.LeftControl => "L-Ctrl",
            KeyCode.RightControl => "R-Ctrl",
            KeyCode.LeftAlt => "L-Alt",
            KeyCode.RightAlt => "R-Alt",
            _ => key.ToString()
        };
    }
}