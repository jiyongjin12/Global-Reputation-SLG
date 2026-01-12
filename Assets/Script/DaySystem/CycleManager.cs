using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 주기 상태 (낮/밤)
/// </summary>
public enum CyclePhase
{
    Day,
    Night
}

/// <summary>
/// 주기 이벤트 데이터
/// </summary>
public struct CycleEventData
{
    public int CycleCount;          // 현재 주기 번호 (1, 2, 3...)
    public CyclePhase Phase;        // 현재 페이즈 (낮/밤)
    public float NormalizedTime;    // 현재 페이즈 내 진행률 (0~1)
    public float TotalElapsedTime;  // 게임 시작 후 총 경과 시간 (초)

    public override string ToString()
    {
        return $"주기 {CycleCount} - {Phase} ({NormalizedTime:P0})";
    }
}

/// <summary>
/// 주기 관리 시스템
/// - 낮/밤 주기 관리
/// - 이벤트 기반 설계 (구독 패턴)
/// - 주기마다 자동 저장
/// </summary>
public class CycleManager : MonoBehaviour
{
    public static CycleManager Instance { get; private set; }

    [Header("=== 주기 설정 ===")]
    [Tooltip("낮 지속 시간 (분)")]
    [SerializeField] private float dayDurationMinutes = 7f;

    [Tooltip("밤 지속 시간 (분)")]
    [SerializeField] private float nightDurationMinutes = 3f;

    [Tooltip("시작 페이즈")]
    [SerializeField] private CyclePhase startPhase = CyclePhase.Day;

    [Tooltip("시작 시 일시정지 상태")]
    [SerializeField] private bool startPaused = false;

    [Header("=== 자동 저장 ===")]
    [Tooltip("주기 완료 시 자동 저장")]
    [SerializeField] private bool autoSaveOnCycleComplete = true;

    [Header("=== 디버그 ===")]
    [Tooltip("시간 배율 (테스트용)")]
    [Range(0.1f, 10f)]
    [SerializeField] private float timeScale = 1f;

    [Tooltip("디버그 로그 출력")]
    [SerializeField] private bool debugLog = true;

    // ==================== 이벤트 (C# Events) ====================

    /// <summary>낮 시작 시 호출</summary>
    public event Action<CycleEventData> OnDayStart;

    /// <summary>밤 시작 시 호출</summary>
    public event Action<CycleEventData> OnNightStart;

    /// <summary>주기 완료 시 호출 (밤 → 낮 전환 직전)</summary>
    public event Action<CycleEventData> OnCycleComplete;

    /// <summary>페이즈 변경 시 호출 (낮↔밤)</summary>
    public event Action<CycleEventData> OnPhaseChanged;

    /// <summary>매 프레임 시간 업데이트 시 호출 (UI 갱신용)</summary>
    public event Action<CycleEventData> OnTimeUpdated;

    // ==================== UnityEvents (Inspector에서 연결 가능) ====================

    [Header("=== Unity Events ===")]
    [SerializeField] private UnityEvent<int> onCycleCompleteUnity;      // 주기 번호 전달
    [SerializeField] private UnityEvent<CyclePhase> onPhaseChangedUnity; // 페이즈 전달

    // ==================== 상태 ====================

    private CyclePhase currentPhase;
    private int cycleCount = 0;
    private float phaseTimer = 0f;
    private float totalElapsedTime = 0f;
    private bool isPaused = false;

    // ==================== Properties ====================

    /// <summary>현재 페이즈 (낮/밤)</summary>
    public CyclePhase CurrentPhase => currentPhase;

    /// <summary>현재 주기 번호 (1부터 시작)</summary>
    public int CycleCount => cycleCount;

    /// <summary>일시정지 상태</summary>
    public bool IsPaused => isPaused;

    /// <summary>낮 지속 시간 (초)</summary>
    public float DayDurationSeconds => dayDurationMinutes * 60f;

    /// <summary>밤 지속 시간 (초)</summary>
    public float NightDurationSeconds => nightDurationMinutes * 60f;

    /// <summary>1주기 총 시간 (초)</summary>
    public float CycleDurationSeconds => DayDurationSeconds + NightDurationSeconds;

    /// <summary>현재 페이즈 내 진행률 (0~1)</summary>
    public float NormalizedPhaseTime
    {
        get
        {
            float duration = currentPhase == CyclePhase.Day ? DayDurationSeconds : NightDurationSeconds;
            return duration > 0 ? Mathf.Clamp01(phaseTimer / duration) : 0f;
        }
    }

    /// <summary>현재 주기 내 진행률 (0~1, 낮+밤 합산)</summary>
    public float NormalizedCycleTime
    {
        get
        {
            if (CycleDurationSeconds <= 0) return 0f;

            float elapsed = phaseTimer;
            if (currentPhase == CyclePhase.Night)
                elapsed += DayDurationSeconds;

            return Mathf.Clamp01(elapsed / CycleDurationSeconds);
        }
    }

    /// <summary>낮인지 여부</summary>
    public bool IsDay => currentPhase == CyclePhase.Day;

    /// <summary>밤인지 여부</summary>
    public bool IsNight => currentPhase == CyclePhase.Night;

    /// <summary>총 경과 시간 (초)</summary>
    public float TotalElapsedTime => totalElapsedTime;

    /// <summary>현재 이벤트 데이터</summary>
    public CycleEventData CurrentEventData => new CycleEventData
    {
        CycleCount = cycleCount,
        Phase = currentPhase,
        NormalizedTime = NormalizedPhaseTime,
        TotalElapsedTime = totalElapsedTime
    };

    // ==================== Lifecycle ====================

    // ==================== 상태 (인스펙터 표시용) ====================
    [Header("=== Inspector Real-time Status ===")]
    [Tooltip("현재 주기와 페이즈 정보")]
    [SerializeField] private string currentStatusDisplay;

    [Tooltip("현재 페이즈 남은 시간")]
    [SerializeField] private string remainingTimeDisplay;

    [Tooltip("페이즈 진행률 (ProgressBar 대용)")]
    [Range(0f, 1f)]
    [SerializeField] private float inspectorProgress;

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
            return;
        }

        Initialize();
    }

    private void Initialize()
    {
        currentPhase = startPhase;
        cycleCount = startPhase == CyclePhase.Day ? 1 : 0;
        phaseTimer = 0f;
        totalElapsedTime = 0f;
        isPaused = startPaused;

        if (debugLog)
            Debug.Log($"[CycleManager] 초기화 완료 - 주기 {cycleCount}, {currentPhase}");
    }

    private void Update()
    {
        if (isPaused) return;

        float deltaTime = Time.deltaTime * timeScale;
        phaseTimer += deltaTime;
        totalElapsedTime += deltaTime;

        UpdateInspectorStatus();

        // 시간 업데이트 이벤트
        OnTimeUpdated?.Invoke(CurrentEventData);

        // 페이즈 전환 체크
        CheckPhaseTransition();
    }

    // ==================== 페이즈 전환 ====================

    private void CheckPhaseTransition()
    {
        float currentDuration = currentPhase == CyclePhase.Day ? DayDurationSeconds : NightDurationSeconds;

        if (phaseTimer >= currentDuration)
        {
            phaseTimer -= currentDuration;
            TransitionToNextPhase();
        }
    }

    private void TransitionToNextPhase()
    {
        CyclePhase previousPhase = currentPhase;

        if (currentPhase == CyclePhase.Day)
        {
            // 낮 → 밤
            currentPhase = CyclePhase.Night;

            if (debugLog)
                Debug.Log($"[CycleManager] 밤 시작 (주기 {cycleCount})");

            OnNightStart?.Invoke(CurrentEventData);
        }
        else
        {
            // 밤 → 낮 (새 주기)

            // 주기 완료 이벤트 먼저 호출
            OnCycleComplete?.Invoke(CurrentEventData);
            onCycleCompleteUnity?.Invoke(cycleCount);

            if (debugLog)
                Debug.Log($"[CycleManager] 주기 {cycleCount} 완료!");

            // 자동 저장
            if (autoSaveOnCycleComplete)
            {
                PerformAutoSave();
            }

            // 새 주기 시작
            cycleCount++;
            currentPhase = CyclePhase.Day;

            if (debugLog)
                Debug.Log($"[CycleManager] 주기 {cycleCount} 시작 - 낮");

            OnDayStart?.Invoke(CurrentEventData);
        }

        // 페이즈 변경 공통 이벤트
        OnPhaseChanged?.Invoke(CurrentEventData);
        onPhaseChangedUnity?.Invoke(currentPhase);
    }

    // ==================== 자동 저장 ====================

    private void PerformAutoSave()
    {
        if (debugLog)
            Debug.Log($"[CycleManager] 자동 저장 시작 (주기 {cycleCount} 완료)");

        // TODO: SaveManager.Instance?.Save(); 연동
        // 현재는 PlayerPrefs로 임시 저장
        PlayerPrefs.SetInt("LastSaveCycle", cycleCount);
        PlayerPrefs.SetFloat("TotalPlayTime", totalElapsedTime);
        PlayerPrefs.Save();

        if (debugLog)
            Debug.Log($"[CycleManager] 자동 저장 완료");
    }

    // ==================== 외부 제어 ====================

    /// <summary>일시정지 토글</summary>
    public void TogglePause()
    {
        isPaused = !isPaused;

        if (debugLog)
            Debug.Log($"[CycleManager] {(isPaused ? "일시정지" : "재개")}");
    }

    /// <summary>일시정지 설정</summary>
    public void SetPaused(bool paused)
    {
        isPaused = paused;
    }

    /// <summary>시간 배율 설정 (테스트/치트용)</summary>
    public void SetTimeScale(float scale)
    {
        timeScale = Mathf.Clamp(scale, 0.1f, 10f);

        if (debugLog)
            Debug.Log($"[CycleManager] 시간 배율: {timeScale}x");
    }

    /// <summary>강제 페이즈 전환</summary>
    public void ForceNextPhase()
    {
        phaseTimer = 0f;
        TransitionToNextPhase();
    }

    /// <summary>강제 주기 스킵 (밤까지 전부 건너뜀)</summary>
    public void ForceNextCycle()
    {
        if (currentPhase == CyclePhase.Day)
        {
            // 낮 → 밤 → 다음 낮
            phaseTimer = 0f;
            TransitionToNextPhase();  // → 밤
            phaseTimer = 0f;
            TransitionToNextPhase();  // → 낮 (새 주기)
        }
        else
        {
            // 밤 → 다음 낮
            phaseTimer = 0f;
            TransitionToNextPhase();
        }
    }

    /// <summary>주기 데이터 저장용</summary>
    public CycleSaveData GetSaveData()
    {
        return new CycleSaveData
        {
            cycleCount = cycleCount,
            currentPhase = currentPhase,
            phaseTimer = phaseTimer,
            totalElapsedTime = totalElapsedTime
        };
    }

    /// <summary>주기 데이터 불러오기</summary>
    public void LoadSaveData(CycleSaveData data)
    {
        cycleCount = data.cycleCount;
        currentPhase = data.currentPhase;
        phaseTimer = data.phaseTimer;
        totalElapsedTime = data.totalElapsedTime;

        if (debugLog)
            Debug.Log($"[CycleManager] 저장 데이터 로드 완료 - 주기 {cycleCount}, {currentPhase}");
    }

    // ==================== 유틸리티 ====================

    /// <summary>현재 페이즈의 남은 시간 (초)</summary>
    public float GetRemainingPhaseTime()
    {
        float duration = currentPhase == CyclePhase.Day ? DayDurationSeconds : NightDurationSeconds;
        return Mathf.Max(0f, duration - phaseTimer);
    }

    /// <summary>현재 주기의 남은 시간 (초)</summary>
    public float GetRemainingCycleTime()
    {
        float remaining = GetRemainingPhaseTime();

        if (currentPhase == CyclePhase.Day)
            remaining += NightDurationSeconds;

        return remaining;
    }

    /// <summary>시간 포맷팅 (MM:SS)</summary>
    public static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int secs = Mathf.FloorToInt(seconds % 60f);
        return $"{minutes:D2}:{secs:D2}";
    }

    /// <summary>현재 상태 문자열</summary>
    public string GetStatusString()
    {
        string remaining = FormatTime(GetRemainingPhaseTime());
        return $"주기 {cycleCount} | {(IsDay ? " 낮" : " 밤")} | 남은 시간: {remaining}";
    }

    // ==================== Gizmos ====================

    private void OnGUI()
    {
        if (!debugLog) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 100));
        GUILayout.Label(GetStatusString());
        GUILayout.Label($"진행률: {NormalizedPhaseTime:P0} | 총 시간: {FormatTime(totalElapsedTime)}");
        if (isPaused)
            GUILayout.Label("<color=yellow> 일시정지</color>");
        GUILayout.EndArea();
    }

    /// <summary>
    /// 인스펙터창에서 실시간으로 상태를 확인하기 위한 로직
    /// </summary>
    private void UpdateInspectorStatus()
    {
        // [주기 1 - 낮] 형태의 문자열 생성
        currentStatusDisplay = $"주기 {cycleCount} - {(currentPhase == CyclePhase.Day ? "낮" : "밤")}";

        // MM:SS 포맷으로 남은 시간 계산
        remainingTimeDisplay = FormatTime(GetRemainingPhaseTime());

        // 진행바(Range) 업데이트
        inspectorProgress = NormalizedPhaseTime;
    }
}

/// <summary>
/// 주기 저장 데이터
/// </summary>
[Serializable]
public class CycleSaveData
{
    public int cycleCount;
    public CyclePhase currentPhase;
    public float phaseTimer;
    public float totalElapsedTime;
}