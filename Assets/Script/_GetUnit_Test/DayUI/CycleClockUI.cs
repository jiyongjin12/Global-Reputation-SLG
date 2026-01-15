using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 주기 시계 + 평판 바 UI
/// - 시계 바늘이 회전하며 시간 표시
/// - 낮/밤에 따라 배경색 변경
/// - 주기 텍스트 표시
/// - 평판 Fill Bar 표시
/// </summary>
public class CycleClockUI : MonoBehaviour
{
    [Header("=== 시계 설정 ===")]
    [Tooltip("회전할 GameObject (막대기의 부모)")]
    [SerializeField] private RectTransform clockHand;

    [Tooltip("시계 배경 이미지")]
    [SerializeField] private Image clockBackground;

    [Header("=== 주기 텍스트 ===")]
    [Tooltip("주기 표시 텍스트")]
    [SerializeField] private TextMeshProUGUI cycleText;

    [Tooltip("시간 표시 텍스트 (선택)")]
    [SerializeField] private TextMeshProUGUI timeText;

    [Header("=== 시계 색상 설정 ===")]
    [SerializeField] private Color dayColor = new Color(1f, 0.9f, 0.6f, 1f);      // 밝은 노란색
    [SerializeField] private Color nightColor = new Color(0.2f, 0.2f, 0.4f, 1f);  // 어두운 파란색
    [SerializeField] private Color handDayColor = Color.black;
    [SerializeField] private Color handNightColor = Color.white;

    [Header("=== 회전 설정 ===")]
    [Tooltip("시계 방향으로 회전 (true) / 반시계 방향 (false)")]
    [SerializeField] private bool clockwise = true;

    [Tooltip("시작 각도 (12시 = 0, 3시 = -90)")]
    [SerializeField] private float startAngle = 0f;

    [Header("=== 평판 바 설정 ===")]
    [Tooltip("평판 바 Fill 이미지 (Image Type: Filled)")]
    [SerializeField] private Image reputationFillBar;

    [Tooltip("평판 수치 텍스트 (선택)")]
    [SerializeField] private TextMeshProUGUI reputationText;

    [Header("=== 평판 색상 (등급별) ===")]
    [SerializeField] private Color hatedColor = new Color(0.8f, 0.2f, 0.2f, 1f);       // 빨강
    [SerializeField] private Color distrustedColor = new Color(0.9f, 0.5f, 0.2f, 1f);  // 주황
    [SerializeField] private Color neutralColor = new Color(0.9f, 0.9f, 0.3f, 1f);     // 노랑
    [SerializeField] private Color friendlyColor = new Color(0.4f, 0.8f, 0.4f, 1f);    // 초록
    [SerializeField] private Color heroicColor = new Color(0.3f, 0.6f, 1f, 1f);        // 파랑

    // 바늘 이미지 (색상 변경용)
    private Image handImage;

    private void Start()
    {
        // 바늘 이미지 캐시
        if (clockHand != null)
        {
            handImage = clockHand.GetComponentInChildren<Image>();
        }

        // CycleManager 이벤트 구독
        if (CycleManager.Instance != null)
        {
            CycleManager.Instance.OnTimeUpdated += OnTimeUpdated;
            CycleManager.Instance.OnPhaseChanged += OnPhaseChanged;

            // 초기 상태 설정
            UpdateClockVisual(CycleManager.Instance.CurrentEventData);
            UpdateColors(CycleManager.Instance.IsDay);
        }

        // ReputationManager 이벤트 구독
        if (ReputationManager.Instance != null)
        {
            ReputationManager.Instance.OnReputationChanged += OnReputationChanged;

            // 초기 평판 표시
            UpdateReputationBar(ReputationManager.Instance.CurrentReputation);
        }
    }

    private void OnDestroy()
    {
        if (CycleManager.Instance != null)
        {
            CycleManager.Instance.OnTimeUpdated -= OnTimeUpdated;
            CycleManager.Instance.OnPhaseChanged -= OnPhaseChanged;
        }

        if (ReputationManager.Instance != null)
        {
            ReputationManager.Instance.OnReputationChanged -= OnReputationChanged;
        }
    }

    // ==================== 시계 관련 ====================

    /// <summary>
    /// 매 프레임 시간 업데이트
    /// </summary>
    private void OnTimeUpdated(CycleEventData data)
    {
        UpdateClockVisual(data);
    }

    /// <summary>
    /// 페이즈 변경 시 (낮↔밤)
    /// </summary>
    private void OnPhaseChanged(CycleEventData data)
    {
        UpdateColors(data.Phase == CyclePhase.Day);
    }

    /// <summary>
    /// 시계 시각 업데이트
    /// </summary>
    private void UpdateClockVisual(CycleEventData data)
    {
        // 바늘 회전 (0~1 → 0~360도)
        if (clockHand != null)
        {
            float progress = CycleManager.Instance.NormalizedCycleTime;
            float angle = progress * 360f;

            if (clockwise)
                angle = -angle;  // 시계 방향

            clockHand.localRotation = Quaternion.Euler(0, 0, startAngle + angle);
        }

        // 주기 텍스트
        if (cycleText != null)
        {
            string phaseText = data.Phase == CyclePhase.Day ? "낮" : "밤";
            cycleText.text = $"주기 {data.CycleCount} - {phaseText}";
        }

        // 시간 텍스트 (선택)
        if (timeText != null)
        {
            float remaining = CycleManager.Instance.GetRemainingPhaseTime();
            timeText.text = CycleManager.FormatTime(remaining);
        }
    }

    /// <summary>
    /// 낮/밤 색상 변경
    /// </summary>
    private void UpdateColors(bool isDay)
    {
        if (clockBackground != null)
        {
            clockBackground.color = isDay ? dayColor : nightColor;
        }

        if (handImage != null)
        {
            handImage.color = isDay ? handDayColor : handNightColor;
        }
    }

    // ==================== 평판 관련 ====================

    /// <summary>
    /// 평판 변경 시 호출
    /// </summary>
    private void OnReputationChanged(int reputation, float subPoints)
    {
        UpdateReputationBar(reputation);
    }

    /// <summary>
    /// 평판 바 업데이트
    /// </summary>
    private void UpdateReputationBar(int reputation)
    {
        // Fill Amount 업데이트 (0~100 → 0~1)
        if (reputationFillBar != null)
        {
            reputationFillBar.fillAmount = reputation / 100f;

            // 등급별 색상 변경
            reputationFillBar.color = GetReputationColor(reputation);
        }

        // 텍스트 업데이트 (선택)
        if (reputationText != null)
        {
            ReputationTier tier = GetTierFromReputation(reputation);
            reputationText.text = $"{reputation} ({GetTierName(tier)})";
        }
    }

    /// <summary>
    /// 평판 수치에 따른 색상 반환
    /// </summary>
    private Color GetReputationColor(int reputation)
    {
        if (reputation >= 80) return heroicColor;
        if (reputation >= 60) return friendlyColor;
        if (reputation >= 40) return neutralColor;
        if (reputation >= 20) return distrustedColor;
        return hatedColor;
    }

    /// <summary>
    /// 평판 수치로 등급 계산
    /// </summary>
    private ReputationTier GetTierFromReputation(int reputation)
    {
        if (reputation >= 80) return ReputationTier.Heroic;
        if (reputation >= 60) return ReputationTier.Friendly;
        if (reputation >= 40) return ReputationTier.Neutral;
        if (reputation >= 20) return ReputationTier.Distrusted;
        return ReputationTier.Hated;
    }

    /// <summary>
    /// 등급 이름 반환
    /// </summary>
    private string GetTierName(ReputationTier tier)
    {
        return tier switch
        {
            ReputationTier.Heroic => "영웅",
            ReputationTier.Friendly => "우호",
            ReputationTier.Neutral => "중립",
            ReputationTier.Distrusted => "의심",
            ReputationTier.Hated => "적대",
            _ => "???"
        };
    }

    // ==================== 에디터 테스트 ====================

#if UNITY_EDITOR
    [Header("=== 에디터 테스트 ===")]
    [Range(0f, 1f)]
    [SerializeField] private float testClockProgress = 0f;

    [Range(0, 100)]
    [SerializeField] private int testReputation = 50;

    private void OnValidate()
    {
        // 시계 테스트
        if (!Application.isPlaying && clockHand != null)
        {
            float angle = testClockProgress * 360f;
            if (clockwise) angle = -angle;
            clockHand.localRotation = Quaternion.Euler(0, 0, startAngle + angle);
        }

        // 평판 바 테스트
        if (!Application.isPlaying && reputationFillBar != null)
        {
            reputationFillBar.fillAmount = testReputation / 100f;
            reputationFillBar.color = GetReputationColor(testReputation);
        }

        // 평판 텍스트 테스트
        if (!Application.isPlaying && reputationText != null)
        {
            ReputationTier tier = GetTierFromReputation(testReputation);
            reputationText.text = $"{testReputation} ({GetTierName(tier)})";
        }
    }
#endif
}