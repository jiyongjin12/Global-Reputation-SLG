using System;
using UnityEngine;

/// <summary>
/// 사회적 상호작용 결과
/// </summary>
public enum SocialInteractionResult
{
    Good,       // 좋은 결과: 정신력 +10
    Neutral,    // 보통: 변화 없음
    Bad         // 나쁜 결과: 정신력 -8
}

/// <summary>
/// 상호작용 유형
/// </summary>
public enum SocialInteractionType
{
    Greeting,       // 인사
    Conversation,   // 대화
    Joke,           // 농담
    Argue,          // 논쟁
    Comfort         // 위로
}

/// <summary>
/// 사회적 상호작용 처리 인터페이스
/// </summary>
public interface ISocialInteractionProcessor
{
    (SocialInteractionResult result, SocialInteractionType type) Process(Unit initiator, Unit target);
}

/// <summary>
/// 기본 랜덤 상호작용 처리기
/// </summary>
public class RandomSocialProcessor : ISocialInteractionProcessor
{
    public (SocialInteractionResult result, SocialInteractionType type) Process(Unit initiator, Unit target)
    {
        float roll = UnityEngine.Random.value;

        if (roll < 0.4f)
        {
            // 40% 좋은 결과
            SocialInteractionType type = UnityEngine.Random.value < 0.5f
                ? SocialInteractionType.Conversation
                : (UnityEngine.Random.value < 0.5f ? SocialInteractionType.Joke : SocialInteractionType.Comfort);
            return (SocialInteractionResult.Good, type);
        }
        else if (roll < 0.8f)
        {
            // 40% 보통
            return (SocialInteractionResult.Neutral, SocialInteractionType.Greeting);
        }
        else
        {
            // 20% 나쁜 결과
            return (SocialInteractionResult.Bad, SocialInteractionType.Argue);
        }
    }
}

/// <summary>
/// 유닛 사회적 상호작용 컴포넌트
/// </summary>
public class UnitSocialInteraction : MonoBehaviour
{
    [Header("=== 설정 ===")]
    [SerializeField] private float interactionRadius = 3f;
    [SerializeField] private float interactionDuration = 2f;
    [SerializeField] private float searchRadius = 10f;

    [Header("=== 정신력 변화량 ===")]
    [SerializeField] private float goodResultMentalBonus = 10f;
    [SerializeField] private float badResultMentalPenalty = 8f;

    // 상태
    private Unit owner;
    private Unit interactionTarget;
    private float interactionStartTime;
    private bool isInteracting;
    private ISocialInteractionProcessor processor;

    // 결과
    private SocialInteractionResult lastResult;
    private SocialInteractionType lastType;

    // 이벤트
    public event Action<Unit, Unit, SocialInteractionResult, SocialInteractionType> OnInteractionComplete;

    // Properties
    public bool IsInteracting => isInteracting;
    public Unit InteractionTarget => interactionTarget;
    public float InteractionRadius => interactionRadius;  // 추가

    private void Awake()
    {
        owner = GetComponent<Unit>();
        processor = new RandomSocialProcessor();
    }

    /// <summary>
    /// 상호작용 처리기 설정 (AI API 연동용)
    /// </summary>
    public void SetProcessor(ISocialInteractionProcessor newProcessor)
    {
        processor = newProcessor ?? new RandomSocialProcessor();
    }

    /// <summary>
    /// 근처 유닛 찾기
    /// </summary>
    public Unit FindNearbyUnitForInteraction()
    {
        var colliders = Physics.OverlapSphere(transform.position, searchRadius);
        Unit nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var col in colliders)
        {
            Unit other = col.GetComponent<Unit>();
            if (other == null || other == owner || !other.IsAlive) continue;

            // 상대도 Idle 상태여야 함
            if (!other.IsIdle) continue;

            // 상대방 상호작용 컴포넌트 확인
            var otherSocial = other.GetComponent<UnitSocialInteraction>();
            if (otherSocial != null && otherSocial.IsInteracting) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = other;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 상호작용 시작
    /// </summary>
    public bool StartInteraction(Unit target)
    {
        if (target == null || isInteracting) return false;

        float dist = Vector3.Distance(transform.position, target.transform.position);
        if (dist > interactionRadius)
        {
            Debug.Log($"[Social] {owner.UnitName}: {target.UnitName}에게 너무 멀어서 상호작용 불가");
            return false;
        }

        interactionTarget = target;
        interactionStartTime = Time.time;
        isInteracting = true;

        // Blackboard 업데이트
        owner.Blackboard?.StartSocialInteraction(target);

        // 상대방도 상호작용 상태로
        var targetSocial = target.GetComponent<UnitSocialInteraction>();
        if (targetSocial != null)
        {
            targetSocial.SetAsTarget(owner);
        }

        Debug.Log($"[Social] {owner.UnitName}이(가) {target.UnitName}와 상호작용 시작");
        return true;
    }

    /// <summary>
    /// 상대방으로 설정됨
    /// </summary>
    public void SetAsTarget(Unit initiator)
    {
        interactionTarget = initiator;
        interactionStartTime = Time.time;
        isInteracting = true;
        owner.Blackboard?.StartSocialInteraction(initiator);
    }

    /// <summary>
    /// 상호작용 업데이트 (UnitAI에서 호출)
    /// </summary>
    public bool UpdateInteraction()
    {
        if (!isInteracting) return false;

        // 대상이 사라졌으면 종료
        if (interactionTarget == null || !interactionTarget.IsAlive)
        {
            CancelInteraction();
            return false;
        }

        // 시간 경과 체크
        if (Time.time - interactionStartTime >= interactionDuration)
        {
            CompleteInteraction();
            return false;
        }

        return true;
    }

    /// <summary>
    /// 상호작용 완료
    /// </summary>
    private void CompleteInteraction()
    {
        if (!isInteracting || interactionTarget == null) return;

        // 결과 계산
        (lastResult, lastType) = processor.Process(owner, interactionTarget);

        // 정신력 변화 적용
        ApplyMentalHealthChange(owner, lastResult);
        ApplyMentalHealthChange(interactionTarget, lastResult);

        // 경험치 지급
        owner.GainExpFromAction(ExpGainAction.Social);

        Debug.Log($"[Social] {owner.UnitName} ↔ {interactionTarget.UnitName}: {lastType} ({lastResult})");

        // 이벤트 발생
        OnInteractionComplete?.Invoke(owner, interactionTarget, lastResult, lastType);

        // 상태 정리
        EndInteraction();
    }

    /// <summary>
    /// 정신력 변화 적용
    /// </summary>
    private void ApplyMentalHealthChange(Unit unit, SocialInteractionResult result)
    {
        switch (result)
        {
            case SocialInteractionResult.Good:
                unit.IncreaseMentalHealth(goodResultMentalBonus);
                break;
            case SocialInteractionResult.Bad:
                unit.DecreaseMentalHealth(badResultMentalPenalty);
                break;
                // Neutral은 변화 없음
        }
    }

    /// <summary>
    /// 상호작용 취소
    /// </summary>
    public void CancelInteraction()
    {
        if (!isInteracting) return;

        Debug.Log($"[Social] {owner.UnitName}: 상호작용 취소됨");
        EndInteraction();
    }

    /// <summary>
    /// 상호작용 중단 (호환성용)
    /// </summary>
    public void InterruptInteraction()
    {
        CancelInteraction();
    }

    /// <summary>
    /// 상호작용 종료 (공통)
    /// </summary>
    private void EndInteraction()
    {
        // 상대방도 종료
        if (interactionTarget != null)
        {
            var targetSocial = interactionTarget.GetComponent<UnitSocialInteraction>();
            if (targetSocial != null && targetSocial.interactionTarget == owner)
            {
                targetSocial.ForceEndInteraction();
            }
        }

        isInteracting = false;
        interactionTarget = null;
        owner.Blackboard?.EndSocialInteraction();
    }

    /// <summary>
    /// 강제 종료 (상대방에서 호출)
    /// </summary>
    public void ForceEndInteraction()
    {
        isInteracting = false;
        interactionTarget = null;
        owner.Blackboard?.EndSocialInteraction();
    }

    /// <summary>
    /// 마지막 상호작용 결과
    /// </summary>
    public (SocialInteractionResult result, SocialInteractionType type) GetLastInteractionResult()
    {
        return (lastResult, lastType);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // 상호작용 범위
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);

        // 탐색 범위
        Gizmos.color = new Color(0, 1, 1, 0.3f);
        Gizmos.DrawWireSphere(transform.position, searchRadius);

        // 현재 상호작용 대상
        if (isInteracting && interactionTarget != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, interactionTarget.transform.position);
        }
    }
#endif
}