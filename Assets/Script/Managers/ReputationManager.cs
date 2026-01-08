using UnityEngine;
using System;

/// <summary>
/// 게임의 전체 평판을 관리하는 시스템
/// - 물품 판매, 유닛 불만, 채팅 독성 등에 영향을 받음
/// </summary>
public class ReputationManager : MonoBehaviour
{
    public static ReputationManager Instance { get; private set; }

    [Header("=== Reputation Settings ===")]
    [SerializeField] private float currentReputation = 50f; // 0 ~ 100 기준 (50이 보통)
    [SerializeField] private float minReputation = 0f;
    [SerializeField] private float maxReputation = 100f;

    // 평판 변경 시 호출될 이벤트 (UI 등에서 구독)
    public event Action<float> OnReputationChanged;

    public float CurrentReputation => currentReputation;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// 평판 추가/차감 (각종 시스템에서 호출)
    /// </summary>
    public void AddReputation(float amount)
    {
        float previousValue = currentReputation;
        currentReputation = Mathf.Clamp(currentReputation + amount, minReputation, maxReputation);

        if (!Mathf.Approximately(previousValue, currentReputation))
        {
            OnReputationChanged?.Invoke(currentReputation);
            Debug.Log($"<color=yellow>[Reputation]</color> 평판 변경: {previousValue:F1} -> {currentReputation:F1} (변화량: {amount:F1})");
        }
    }

    // 평판 등급 반환 (상점 할인율이나 침입 난이도 산정용)
    public ReputationTier GetCurrentTier()
    {
        if (currentReputation >= 80f) return ReputationTier.Heroic;
        if (currentReputation >= 60f) return ReputationTier.Friendly;
        if (currentReputation >= 40f) return ReputationTier.Neutral;
        if (currentReputation >= 20f) return ReputationTier.Distrusted;
        return ReputationTier.Hated;
    }
}

public enum ReputationTier
{
    Hated,       // 매우 나쁨 (적군 침입 잦음, 가격 비쌈)
    Distrusted,  // 나쁨
    Neutral,     // 보통
    Friendly,    // 좋음
    Heroic       // 매우 좋음 (상점 할인, 유닛 작업 속도 증가)
}