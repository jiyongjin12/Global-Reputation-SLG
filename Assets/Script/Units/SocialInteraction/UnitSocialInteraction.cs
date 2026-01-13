using System;
using System.Collections.Generic;
using UnityEngine;

public enum SocialInteractionResult { Good, Neutral, Bad }
public enum SocialInteractionType { Conversation, Greeting, Gossip, Argue, Comfort, Joke }

/// <summary>
/// 상호작용 결과 데이터
/// </summary>
public class SocialInteractionData
{
    public SocialInteractionType Type;
    public SocialInteractionResult Result;
    public Unit Initiator;
    public Unit Target;
    public float StressChange;
    public string Message;

    public SocialInteractionData(Unit initiator, Unit target)
    {
        Initiator = initiator;
        Target = target;
    }
}

/// <summary>
/// 상호작용 처리 인터페이스 (추후 AI API 연동용)
/// </summary>
public interface ISocialInteractionProcessor
{
    SocialInteractionData ProcessInteraction(Unit initiator, Unit target);
}

/// <summary>
/// 기본 랜덤 상호작용 처리기 (추후 AI API로 대체 가능)
/// </summary>
public class RandomSocialProcessor : ISocialInteractionProcessor
{
    private const float GoodChance = 0.4f;
    private const float NeutralChance = 0.4f;
    private const float GoodStressReduction = 10f;
    private const float BadStressIncrease = 8f;

    public SocialInteractionData ProcessInteraction(Unit initiator, Unit target)
    {
        var data = new SocialInteractionData(initiator, target);
        float roll = UnityEngine.Random.value;

        if (roll < GoodChance)
        {
            data.Result = SocialInteractionResult.Good;
            data.StressChange = -GoodStressReduction;
            data.Type = GetRandomGoodType();
            data.Message = GetGoodMessage(data.Type);
        }
        else if (roll < GoodChance + NeutralChance)
        {
            data.Result = SocialInteractionResult.Neutral;
            data.StressChange = 0f;
            data.Type = SocialInteractionType.Greeting;
            data.Message = "서로 인사를 나눴다.";
        }
        else
        {
            data.Result = SocialInteractionResult.Bad;
            data.StressChange = BadStressIncrease;
            data.Type = SocialInteractionType.Argue;
            data.Message = "말다툼이 있었다.";
        }

        return data;
    }

    private SocialInteractionType GetRandomGoodType()
    {
        var types = new[] { SocialInteractionType.Conversation, SocialInteractionType.Joke, SocialInteractionType.Comfort };
        return types[UnityEngine.Random.Range(0, types.Length)];
    }

    private string GetGoodMessage(SocialInteractionType type) => type switch
    {
        SocialInteractionType.Conversation => "즐거운 대화를 나눴다.",
        SocialInteractionType.Joke => "농담을 주고받으며 웃었다.",
        SocialInteractionType.Comfort => "서로를 위로했다.",
        _ => "좋은 시간을 보냈다."
    };
}

/// <summary>
/// 유닛 사회적 상호작용 관리자
/// </summary>
public class UnitSocialInteraction : MonoBehaviour
{
    [Header("=== 설정 ===")]
    [SerializeField] private float interactionRadius = 3f;
    [SerializeField] private float interactionDuration = 2f;
    [SerializeField] private float searchRadius = 10f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool isInteracting = false;
    [SerializeField] private float interactionTimer = 0f;

    private Unit unit;
    private UnitBlackboard bb;
    private ISocialInteractionProcessor processor = new RandomSocialProcessor();

    public event Action<SocialInteractionData> OnInteractionComplete;

    public bool IsInteracting => isInteracting;
    public float InteractionRadius => interactionRadius;

    private void Awake() => unit = GetComponent<Unit>();
    private void Start() => bb = unit.Blackboard;

    /// <summary>
    /// 상호작용 처리기 변경 (AI API 연동 시 사용)
    /// </summary>
    public void SetProcessor(ISocialInteractionProcessor newProcessor) =>
        processor = newProcessor ?? new RandomSocialProcessor();

    /// <summary>
    /// 근처 상호작용 가능한 유닛 찾기
    /// </summary>
    public Unit FindNearbyUnitForInteraction()
    {
        if (!bb.CanSocialize) return null;

        var colliders = Physics.OverlapSphere(transform.position, searchRadius);
        List<Unit> candidates = new();

        foreach (var col in colliders)
        {
            if (col.gameObject == gameObject) continue;

            var otherUnit = col.GetComponent<Unit>();
            if (otherUnit == null || !otherUnit.IsAlive) continue;

            var otherBB = otherUnit.Blackboard;
            if (otherBB == null || !otherBB.CanSocialize || !otherBB.IsIdle) continue;

            candidates.Add(otherUnit);
        }

        if (candidates.Count == 0) return null;

        // 가장 가까운 유닛
        Unit nearest = null;
        float nearestDist = float.MaxValue;
        foreach (var candidate in candidates)
        {
            float dist = Vector3.Distance(transform.position, candidate.transform.position);
            if (dist < nearestDist) { nearestDist = dist; nearest = candidate; }
        }
        return nearest;
    }

    /// <summary>
    /// 상호작용 시작
    /// </summary>
    public bool StartInteraction(Unit target)
    {
        if (target == null || isInteracting || !bb.CanSocialize) return false;

        float dist = Vector3.Distance(transform.position, target.transform.position);
        if (dist > interactionRadius) return false;

        isInteracting = true;
        interactionTimer = 0f;
        bb.StartSocialInteraction(target);

        // 상대방에게도 알림
        target.GetComponent<UnitSocialInteraction>()?.OnInteractionStartedByOther(unit);

        return true;
    }

    public void OnInteractionStartedByOther(Unit initiator)
    {
        isInteracting = true;
        interactionTimer = 0f;
        bb.StartSocialInteraction(initiator);
    }

    /// <summary>
    /// 상호작용 업데이트 (UnitAI에서 호출)
    /// </summary>
    public bool UpdateInteraction()
    {
        if (!isInteracting) return false;

        interactionTimer += Time.deltaTime;

        if (interactionTimer >= interactionDuration)
        {
            CompleteInteraction();
            return false;
        }

        return true;
    }

    private void CompleteInteraction()
    {
        if (bb.InteractionTarget == null) { EndInteraction(); return; }

        var result = processor.ProcessInteraction(unit, bb.InteractionTarget);
        ApplyInteractionResult(result);
        unit.GainExpFromAction(ExpGainAction.Social);
        OnInteractionComplete?.Invoke(result);

        EndInteraction();
    }

    private void ApplyInteractionResult(SocialInteractionData result)
    {
        if (result.StressChange != 0)
        {
            bb.ModifyStress(result.StressChange);
            result.Target?.Blackboard?.ModifyStress(result.StressChange);
        }
    }

    public void EndInteraction()
    {
        isInteracting = false;
        interactionTimer = 0f;
        bb.EndSocialInteraction();
    }

    public void InterruptInteraction()
    {
        if (isInteracting) EndInteraction();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 1, 0, 0.2f);
        Gizmos.DrawWireSphere(transform.position, searchRadius);
        Gizmos.color = new Color(0, 1, 1, 0.3f);
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
}