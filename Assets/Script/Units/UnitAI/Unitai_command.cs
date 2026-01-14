using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 유닛 AI - 플레이어 명령 및 자유행동
/// </summary>
public partial class UnitAI
{
    // ==================== 명령 대기 상태 ====================

    /// <summary>
    /// 명령 대기 상태 설정 (UnitSelectionManager에서 호출)
    /// </summary>
    public void SetWaitingForCommand(bool waiting)
    {
        isWaitingForCommand = waiting;

        if (waiting)
        {
            // 현재 작업 중단
            InterruptCurrentTask();

            // 워크스테이션 해제 (★ ReleaseWorker 사용)
            if (currentWorkstation != null)
            {
                currentWorkstation.CancelWork();
                currentWorkstation.ReleaseWorker();
                currentWorkstation = null;
                isWorkstationWorkStarted = false;
            }

            // 배달 상태 초기화
            ClearDeliveryState();

            // 상호작용 취소
            if (currentBehavior == AIBehaviorState.Socializing)
                CancelSocialInteraction();

            // 아이템 포기
            GiveUpRemainingPersonalItems();

            // Idle 상태로
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.PlayerCommand);

            // 이동 멈춤
            unit.StopMoving();

            Debug.Log($"[UnitAI] {unit.UnitName}: 명령 대기 상태");
        }
        else
        {
            // 대기 해제 → 자유 행동 복귀
            if (!bb.HasPlayerCommand)
            {
                SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            }
            Debug.Log($"[UnitAI] {unit.UnitName}: 자유 행동 복귀");
        }
    }

    // ==================== Player Command ====================

    /// <summary>
    /// 플레이어 명령 받기 (충성도에 따라 무시 가능)
    /// </summary>
    public void GiveCommand(UnitCommand command)
    {
        // 충성도 체크 - 명령 무시 확률
        if (bb.ShouldIgnoreCommand())
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 명령 무시! (충성도: {bb.Loyalty:F0}, 확률: {bb.CommandIgnoreChance * 100:F0}%)");
            bb.IncreaseMentalHealth(2f);  // 반항의 쾌감
            return;
        }

        bb.HasPlayerCommand = true;
        bb.PlayerCommand = command;

        // 명령 대기 상태 해제
        isWaitingForCommand = false;

        // 상호작용 중이면 취소
        if (currentBehavior == AIBehaviorState.Socializing)
            CancelSocialInteraction();

        InterruptCurrentTask();
        ExecutePlayerCommand();
    }

    /// <summary>
    /// 현재 작업 취소 (명령 대기 상태로 전환 시)
    /// </summary>
    public void CancelCurrentTask()
    {
        // 현재 작업 중단
        InterruptCurrentTask();

        // 상호작용 중단
        if (currentBehavior == AIBehaviorState.Socializing)
            CancelSocialInteraction();

        // 상태 초기화
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        bb.CurrentTask = null;
        bb.HasPlayerCommand = false;
        bb.PlayerCommand = null;

        Debug.Log($"[UnitAI] {unit.UnitName}: 현재 작업 취소됨");
    }

    /// <summary>
    /// 플레이어 명령 실행
    /// </summary>
    protected void ExecutePlayerCommand()
    {
        var cmd = bb.PlayerCommand;
        if (cmd == null)
        {
            ClearPlayerCommand();
            return;
        }

        Debug.Log($"[UnitAI] {unit.UnitName}: 플레이어 명령 실행 - {cmd.Type}");

        switch (cmd.Type)
        {
            case UnitCommandType.MoveTo:
                ExecuteMoveCommand(cmd);
                break;

            case UnitCommandType.Stop:
                unit.StopMoving();
                ClearPlayerCommand();
                SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
                break;

            case UnitCommandType.Harvest:
                ExecuteHarvestCommand(cmd);
                break;

            case UnitCommandType.Construct:
                ExecuteConstructCommand(cmd);
                break;

            case UnitCommandType.Attack:
                ExecuteAttackCommand(cmd);
                break;

            default:
                Debug.LogWarning($"[UnitAI] 알 수 없는 명령 타입: {cmd.Type}");
                ClearPlayerCommand();
                break;
        }
    }

    /// <summary>
    /// 이동 명령 실행
    /// </summary>
    private void ExecuteMoveCommand(UnitCommand cmd)
    {
        if (cmd.TargetPosition.HasValue)
        {
            SetBehaviorAndPriority(AIBehaviorState.ExecutingCommand, TaskPriorityLevel.PlayerCommand);
            unit.MoveTo(cmd.TargetPosition.Value);
        }
        else
        {
            ClearPlayerCommand();
        }
    }

    /// <summary>
    /// 채집 명령 실행 (★ Fighter도 가능!)
    /// </summary>
    private void ExecuteHarvestCommand(UnitCommand cmd)
    {
        if (cmd.TargetObject == null)
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 채집 대상 없음");
            ClearPlayerCommand();
            return;
        }

        var node = cmd.TargetObject.GetComponent<ResourceNode>();
        if (node == null || !node.CanBeHarvested)
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 채집 불가능한 대상");
            ClearPlayerCommand();
            return;
        }

        // TaskManager에서 해당 노드의 작업 찾기 또는 생성
        if (TaskManager.Instance == null)
        {
            Debug.LogError("[UnitAI] TaskManager가 없습니다!");
            ClearPlayerCommand();
            return;
        }

        var task = TaskManager.Instance.AddHarvestTask(node);
        if (task != null && TaskManager.Instance.TakeTask(task, unit, isPlayerCommand: true))
        {
            // 명령 클리어 후 작업 시작
            ClearPlayerCommand();
            AssignTask(task);
            Debug.Log($"[UnitAI] {unit.UnitName}: 플레이어 명령으로 {node.name} 채집 시작");
        }
        else
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 채집 작업 할당 실패");
            ClearPlayerCommand();
        }
    }

    /// <summary>
    /// 건설 명령 실행
    /// </summary>
    private void ExecuteConstructCommand(UnitCommand cmd)
    {
        if (cmd.TargetObject == null)
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 건설 대상 없음");
            ClearPlayerCommand();
            return;
        }

        var building = cmd.TargetObject.GetComponent<Building>();
        if (building == null || building.CurrentState == BuildingState.Completed)
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 건설 불가능한 대상");
            ClearPlayerCommand();
            return;
        }

        if (TaskManager.Instance == null)
        {
            ClearPlayerCommand();
            return;
        }

        // 건설 작업 찾기
        var task = TaskManager.Instance.FindTaskForPlayerCommand(unit, TaskType.Construct, cmd.TargetObject);
        if (task != null && TaskManager.Instance.TakeTask(task, unit, isPlayerCommand: true))
        {
            ClearPlayerCommand();
            AssignTask(task);
            Debug.Log($"[UnitAI] {unit.UnitName}: 플레이어 명령으로 {building.name} 건설 시작");
        }
        else
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 건설 작업 할당 실패");
            ClearPlayerCommand();
        }
    }

    /// <summary>
    /// 공격 명령 실행
    /// </summary>
    private void ExecuteAttackCommand(UnitCommand cmd)
    {
        if (cmd.TargetObject == null && !cmd.TargetPosition.HasValue)
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 공격 대상 없음");
            ClearPlayerCommand();
            return;
        }

        // TODO: 전투 시스템 연동
        // 일단 대상 위치로 이동
        if (cmd.TargetObject != null)
        {
            SetBehaviorAndPriority(AIBehaviorState.ExecutingCommand, TaskPriorityLevel.PlayerCommand);
            unit.MoveTo(cmd.TargetObject.transform.position);
        }
        else if (cmd.TargetPosition.HasValue)
        {
            SetBehaviorAndPriority(AIBehaviorState.ExecutingCommand, TaskPriorityLevel.PlayerCommand);
            unit.MoveTo(cmd.TargetPosition.Value);
        }

        Debug.Log($"[UnitAI] {unit.UnitName}: 공격 명령 실행");
    }

    protected void UpdatePlayerCommand()
    {
        if (!bb.HasPlayerCommand || bb.PlayerCommand == null)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        if (bb.PlayerCommand.Type == UnitCommandType.MoveTo && unit.HasArrivedAtDestination())
        {
            ClearPlayerCommand();
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        }
    }

    protected void ClearPlayerCommand()
    {
        bb.HasPlayerCommand = false;
        bb.PlayerCommand = null;
    }

    // ==================== Free Will & Social ====================

    /// <summary>
    /// 자유 행동 - 상호작용 우선, 없으면 배회
    /// </summary>
    protected void PerformFreeWill()
    {
        // 상호작용 시도
        if (socialInteraction != null && bb.CanSocialize)
        {
            if (Random.value < socialInteractionChance && TryStartSocialInteraction())
                return;
        }

        // 배회
        if (Random.value < 0.3f)
            StartWandering();
    }

    private bool TryStartSocialInteraction()
    {
        var target = FindNearbyIdleUnit();
        if (target == null) return false;

        socialTarget = target;
        float dist = Vector3.Distance(transform.position, target.transform.position);

        if (dist <= socialInteraction.InteractionRadius)
        {
            StartSocialInteraction(target);
            return true;
        }

        // 접근 필요
        isApproachingForSocial = true;
        unit.MoveTo(target.transform.position);
        SetBehaviorAndPriority(AIBehaviorState.Socializing, TaskPriorityLevel.FreeWill);
        return true;
    }

    private Unit FindNearbyIdleUnit()
    {
        var colliders = Physics.OverlapSphere(transform.position, socialSearchRadius);
        List<Unit> candidates = new();

        foreach (var col in colliders)
        {
            if (col.gameObject == gameObject) continue;

            var otherUnit = col.GetComponent<Unit>();
            if (otherUnit == null || !otherUnit.IsAlive) continue;

            var otherBB = otherUnit.Blackboard;
            if (otherBB == null || !otherBB.IsIdle || !otherBB.CanSocialize) continue;

            var otherSocial = otherUnit.GetComponent<UnitSocialInteraction>();
            if (otherSocial != null && otherSocial.IsInteracting) continue;

            candidates.Add(otherUnit);
        }

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    private void StartSocialInteraction(Unit target)
    {
        isApproachingForSocial = false;

        if (socialInteraction != null && socialInteraction.StartInteraction(target))
        {
            SetBehaviorAndPriority(AIBehaviorState.Socializing, TaskPriorityLevel.FreeWill);
        }
        else
        {
            socialTarget = null;
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        }
    }

    protected void UpdateSocializing()
    {
        // 접근 중
        if (isApproachingForSocial)
        {
            if (socialTarget == null || !socialTarget.IsAlive)
            {
                CancelSocialInteraction();
                return;
            }

            float dist = Vector3.Distance(transform.position, socialTarget.transform.position);
            if (dist <= socialInteraction.InteractionRadius || unit.HasArrivedAtDestination())
            {
                unit.StopMoving();
                StartSocialInteraction(socialTarget);
            }
            return;
        }

        // 상호작용 진행
        if (socialInteraction != null && !socialInteraction.UpdateInteraction())
        {
            OnSocialInteractionComplete();
        }
    }

    private void OnSocialInteractionComplete()
    {
        socialTarget = null;
        isApproachingForSocial = false;
        unit.GainExpFromAction(ExpGainAction.Social);
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    protected void CancelSocialInteraction()
    {
        socialInteraction?.InterruptInteraction();
        socialTarget = null;
        isApproachingForSocial = false;
        unit.StopMoving();
        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    protected void StartWandering()
    {
        Vector3 randomPoint = transform.position + Random.insideUnitSphere * wanderRadius;
        randomPoint.y = transform.position.y;

        if (NavMesh.SamplePosition(randomPoint, out var hit, wanderRadius, NavMesh.AllAreas))
        {
            // 느긋한 산책 스타일
            if (movement != null)
                movement.StrollTo(hit.position);
            else
                unit.MoveTo(hit.position);

            SetBehaviorAndPriority(AIBehaviorState.Wandering, TaskPriorityLevel.FreeWill);
        }
    }

    protected void UpdateWandering()
    {
        if (unit.HasArrivedAtDestination())
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }
}