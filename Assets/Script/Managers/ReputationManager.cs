using UnityEngine;
using System;

public class ReputationManager : MonoBehaviour
{
    public static ReputationManager Instance { get; private set; }

    [Header("=== Reputation Settings ===")]
    [SerializeField] private int currentReputation = 50; // 최종 평판 수치 (0 ~ 100)
    [SerializeField] private float subPoints = 0f;      // 서브 포인트 바 (-10 ~ 10)

    private const int MinReputation = 0;
    private const int MaxReputation = 100;
    private const float Threshold = 10f;               // 바가 차는 기준치

    public event Action<int, float> OnReputationChanged; // (전체 평판, 서브 포인트) 전달

    public int CurrentReputation => currentReputation;
    public float SubPoints => subPoints;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// 평판 포인트 추가 (채팅 등에서 호출)
    /// points가 +10이 되면 전체 평판 +1, -10이 되면 전체 평판 -1
    /// </summary>
    public void AddReputationPoints(float amount)
    {
        subPoints += amount;

        // 1. 포인트가 +10 이상인 경우 (평판 상승)
        while (subPoints >= Threshold)
        {
            if (currentReputation < MaxReputation)
            {
                currentReputation++;
                subPoints -= Threshold; // 10을 깎고 남은 포인트 유지 (연속 상승 처리)
            }
            else
            {
                subPoints = 0; // 최대치면 초기화
                break;
            }
        }

        // 2. 포인트가 -10 이하인 경우 (평판 하락)
        while (subPoints <= -Threshold)
        {
            if (currentReputation > MinReputation)
            {
                currentReputation--;
                subPoints += Threshold; // -10을 더하고 남은 포인트 유지
            }
            else
            {
                subPoints = 0; // 최소치면 초기화
                break;
            }
        }

        OnReputationChanged?.Invoke(currentReputation, subPoints);

        Debug.Log($"<color=yellow>[Reputation]</color> 전체 평판: {currentReputation} | 서브바: {subPoints:F1}/10");
    }

    public ReputationTier GetCurrentTier()
    {
        if (currentReputation >= 80) return ReputationTier.Heroic;
        if (currentReputation >= 60) return ReputationTier.Friendly;
        if (currentReputation >= 40) return ReputationTier.Neutral;
        if (currentReputation >= 20) return ReputationTier.Distrusted;
        return ReputationTier.Hated;
    }
}

public enum ReputationTier
{
    Hated, Distrusted, Neutral, Friendly, Heroic
}