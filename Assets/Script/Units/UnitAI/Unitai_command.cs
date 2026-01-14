using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 유닛 AI - 플레이어 명령 및 자유행동
/// ★ 지속 명령 = 완전한 루프 (채집 + 줍기 + 저장고 배달)
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

            // 워크스테이션 해제
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

            // ★ 지속 명령 해제
            bb.ClearPersistentCommand();

            // Idle 상태로
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.PlayerCommand);

            // 이동 멈춤
            unit.StopMoving();

            Debug.Log($"[UnitAI] {unit.UnitName}: 명령 대기 상태");
        }
        else
        {
            // 대기 해제 → 자유 행동 복귀
            if (!bb.HasPlayerCommand && !bb.HasPersistentCommand)
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
            bb.IncreaseMentalHealth(2f);
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
        InterruptCurrentTask();

        if (currentBehavior == AIBehaviorState.Socializing)
            CancelSocialInteraction();

        // ★ 지속 명령도 해제
        bb.ClearPersistentCommand();

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
                bb.ClearPersistentCommand();
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
    /// ★ 채집 명령 실행 (지속 명령 루프 시작)
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

        // ★ 지속 명령 설정 (같은 타입의 자원을 계속 채집)
        ResourceNodeType nodeType = node.Data?.NodeType ?? ResourceNodeType.Tree;
        bb.SetPersistentCommand(PersistentCommandType.Harvest, nodeType);

        ClearPlayerCommand();

        // ★ 지속 명령 루프 시작
        Debug.Log($"[UnitAI] {unit.UnitName}: 지속 채집 시작 - {nodeType}");
        ExecutePersistentHarvestLoop();
    }

    /// <summary>
    /// ★ 건설 명령 실행 (지속 명령으로 설정)
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

        // ★ 지속 명령 설정 (미완성 건물을 계속 건설)
        bb.SetPersistentCommand(PersistentCommandType.Construct);

        var task = TaskManager.Instance.FindTaskForPlayerCommand(unit, TaskType.Construct, cmd.TargetObject);
        if (task != null && TaskManager.Instance.TakeTask(task, unit, isPlayerCommand: true))
        {
            ClearPlayerCommand();
            AssignTask(task);
            Debug.Log($"[UnitAI] {unit.UnitName}: 지속 건설 명령 시작");
        }
        else
        {
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 건설 작업 할당 실패");
            ClearPlayerCommand();
            bb.ClearPersistentCommand();
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

        // TODO: 전투 시스템 연동 + 지속 명령
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

    // ==================== ★ 지속 명령 루프 ====================

    /// <summary>
    /// ★ 채집 지속 명령 루프 실행
    /// 순서: 인벤 체크 → 아이템 줍기 → 채집 → 반복
    /// </summary>
    public void ExecutePersistentHarvestLoop()
    {
        if (!bb.HasPersistentCommand || bb.PersistentCmd.Type != PersistentCommandType.Harvest)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        var cmd = bb.PersistentCmd;

        // 1. 인벤토리 가득 찼으면 저장고로
        if (unit.Inventory.IsFull)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: [루프] 인벤 가득 → 저장고로");
            StartDeliveryToStorage();
            return;
        }

        // 2. 줍을 아이템이 있으면 줍기
        CleanupItemLists();
        if (HasItemsToPickup())
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: [루프] 아이템 줍기");
            StartPickingUpItems();
            return;
        }

        // 3. 채집할 자원 찾기
        ResourceNode nearestNode = FindNearestResourceNode(cmd.TargetNodeType ?? ResourceNodeType.Tree, cmd.SearchRadius);
        if (nearestNode != null)
        {
            StartHarvestingNode(nearestNode);
            return;
        }

        // 4. 아무것도 없음 → 루프 종료
        Debug.Log($"[UnitAI] {unit.UnitName}: [루프] 채집 완료 (더 이상 자원 없음)");
        bb.ClearPersistentCommand();

        // 남은 인벤토리가 있으면 저장고로
        if (!unit.Inventory.IsEmpty)
        {
            StartDeliveryToStorage();
        }
        else
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        }
    }

    /// <summary>
    /// ★ 건설 지속 명령 계속
    /// </summary>
    public void ExecutePersistentConstructLoop()
    {
        if (!bb.HasPersistentCommand || bb.PersistentCmd.Type != PersistentCommandType.Construct)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        var cmd = bb.PersistentCmd;

        // 미완성 건물 검색
        Building nearestBuilding = FindNearestIncompleteBuilding(cmd.SearchRadius);

        if (nearestBuilding == null)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 건설할 건물 없음 → 지속 명령 종료");
            bb.ClearPersistentCommand();
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        // 건설 작업 찾기
        var task = TaskManager.Instance?.FindTaskForPlayerCommand(unit, TaskType.Construct, nearestBuilding.gameObject);
        if (task != null && TaskManager.Instance.TakeTask(task, unit, isPlayerCommand: true))
        {
            AssignTask(task);
            Debug.Log($"[UnitAI] {unit.UnitName}: 다음 건설: {nearestBuilding.name}");
        }
        else
        {
            bb.ClearPersistentCommand();
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
        }
    }

    /// <summary>
    /// ★ 지속 명령 루프 계속 (외부에서 호출)
    /// </summary>
    public void ContinuePersistentCommand()
    {
        if (!bb.HasPersistentCommand)
        {
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        switch (bb.PersistentCmd.Type)
        {
            case PersistentCommandType.Harvest:
                ExecutePersistentHarvestLoop();
                break;

            case PersistentCommandType.Construct:
                ExecutePersistentConstructLoop();
                break;

            default:
                bb.ClearPersistentCommand();
                SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
                break;
        }
    }

    // ==================== Helper Methods ====================

    /// <summary>
    /// 줍을 아이템이 있는지 확인
    /// </summary>
    private bool HasItemsToPickup()
    {
        // personalItems 체크
        personalItems.RemoveAll(item => item == null || !item || item.Owner != unit || item.IsBeingMagneted);
        if (personalItems.Count > 0) return true;

        // pendingMagnetItems 중 흡수 가능한 것 체크
        pendingMagnetItems.RemoveAll(item => item == null || !item);
        foreach (var item in pendingMagnetItems)
        {
            if (item != null && item.Resource != null)
            {
                if (unit.Inventory.GetAvailableSpaceFor(item.Resource) >= item.Amount)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 아이템 줍기 시작
    /// </summary>
    private void StartPickingUpItems()
    {
        // personalItems 우선
        if (personalItems.Count > 0)
        {
            currentPersonalItem = personalItems[0];
            personalItems.RemoveAt(0);

            if (currentPersonalItem != null && currentPersonalItem && !currentPersonalItem.IsBeingMagneted)
            {
                if (!currentPersonalItem.IsAnimating)
                    currentPersonalItem.Reserve(unit);

                unit.MoveTo(currentPersonalItem.transform.position);
                SetBehaviorAndPriority(AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
                pickupTimer = 0f;
                return;
            }
        }

        // pendingMagnetItems로 이동
        if (HasAbsorbablePendingItems())
        {
            MoveToNearestAbsorbableItem();
            return;
        }

        // 줍을 게 없으면 루프 계속
        ContinuePersistentCommand();
    }

    /// <summary>
    /// 채집 노드 작업 시작
    /// </summary>
    private void StartHarvestingNode(ResourceNode node)
    {
        if (TaskManager.Instance == null)
        {
            bb.ClearPersistentCommand();
            SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
            return;
        }

        var task = TaskManager.Instance.AddHarvestTask(node);
        if (task != null && TaskManager.Instance.TakeTask(task, unit, isPlayerCommand: true))
        {
            AssignTask(task);
            Debug.Log($"[UnitAI] {unit.UnitName}: [루프] 채집 시작: {node.name}");
        }
        else
        {
            // 작업 할당 실패 - 다음 노드 찾기
            Debug.Log($"[UnitAI] {unit.UnitName}: 작업 할당 실패, 다음 노드 탐색");
            ExecutePersistentHarvestLoop();
        }
    }

    /// <summary>
    /// 가장 가까운 ResourceNode 찾기
    /// </summary>
    private ResourceNode FindNearestResourceNode(ResourceNodeType nodeType, float searchRadius)
    {
        var colliders = Physics.OverlapSphere(transform.position, searchRadius);
        ResourceNode nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var col in colliders)
        {
            var node = col.GetComponent<ResourceNode>();
            if (node == null) continue;
            if (!node.CanBeHarvested) continue;
            if (node.Data?.NodeType != nodeType) continue;

            float dist = Vector3.Distance(transform.position, node.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = node;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 가장 가까운 미완성 건물 찾기
    /// </summary>
    private Building FindNearestIncompleteBuilding(float searchRadius)
    {
        var colliders = Physics.OverlapSphere(transform.position, searchRadius);
        Building nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var col in colliders)
        {
            var building = col.GetComponent<Building>();
            if (building == null) continue;
            if (building.CurrentState == BuildingState.Completed) continue;

            float dist = Vector3.Distance(transform.position, building.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = building;
            }
        }

        return nearest;
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