using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 유닛 AI - 작업 관련
/// ★ 작업 완료 시 ContinuePersistentCommand() 호출하여 루프 계속
/// </summary>
public partial class UnitAI
{
    // ==================== Task Management ====================

    private bool TryPullTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task == null) return false;

        // 채집 작업 시 인벤토리 체크
        if (task.Data.Type == TaskType.Harvest && !CanAcceptHarvestTask(task))
            return false;

        if (!TaskManager.Instance.TakeTask(task, unit))
            return false;

        AssignTask(task);
        return true;
    }

    protected bool TryPullConstructionTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task == null || task.Data.Type != TaskType.Construct) return false;

        if (!TaskManager.Instance.TakeTask(task, unit)) return false;

        ClearDeliveryState();
        AssignTask(task);
        return true;
    }

    private bool CanAcceptHarvestTask(PostedTask task)
    {
        if (unit.Inventory.IsFull) return false;

        var node = task.Owner as ResourceNode;
        if (node?.Data?.Drops == null) return true;

        foreach (var drop in node.Data.Drops)
        {
            if (drop.Resource != null && !unit.Inventory.CanAddAny(drop.Resource))
                return false;
        }
        return true;
    }

    protected void AssignTask(PostedTask task)
    {
        taskContext.Clear();
        taskContext.Task = task;
        bb.CurrentTask = task;
        bb.TargetObject = task.Data.TargetObject;

        switch (task.Data.Type)
        {
            case TaskType.Construct:
                AssignConstructionTask(task);
                break;
            case TaskType.Harvest:
                AssignHarvestTask(task);
                break;
            case TaskType.PickupItem:
                AssignPickupTask(task);
                break;
            case TaskType.Workstation:
                AssignWorkstationTask(task);
                break;
            default:
                AssignGenericTask(task);
                break;
        }
    }

    private void AssignConstructionTask(PostedTask task)
    {
        var building = task.Owner as Building;
        Vector2Int size = building?.Data?.Size ?? Vector2Int.one;
        taskContext.TargetSize = size;

        Vector3 workPos = CalculateDistributedWorkPosition(task.Data.TargetPosition, size);
        MoveToTaskPosition(workPos, AIBehaviorState.Working, TaskPriorityLevel.Construction);
    }

    private void AssignHarvestTask(PostedTask task)
    {
        MoveToTaskPosition(task.Data.TargetPosition, AIBehaviorState.Working, TaskPriorityLevel.Harvest);
    }

    private void AssignPickupTask(PostedTask task)
    {
        MoveToTaskPosition(task.Data.TargetPosition, AIBehaviorState.PickingUpItem, TaskPriorityLevel.ItemPickup);
    }

    private void AssignWorkstationTask(PostedTask task)
    {
        currentWorkstation = task.Owner as IWorkstation;
        isWorkstationWorkStarted = false;

        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        Vector3 workPos = currentWorkstation.WorkPoint?.position ?? task.Data.TargetPosition;
        MoveToTaskPosition(workPos, AIBehaviorState.WorkingAtStation, TaskPriorityLevel.Workstation);
    }

    private void AssignGenericTask(PostedTask task)
    {
        MoveToTaskPosition(task.Data.TargetPosition, AIBehaviorState.Working, TaskPriorityLevel.FreeWill);
    }

    /// <summary>
    /// 공통 이동 로직
    /// </summary>
    private void MoveToTaskPosition(Vector3 position, AIBehaviorState behavior, TaskPriorityLevel priority)
    {
        taskContext.SetMoving(position);
        bb.TargetPosition = position;
        unit.MoveTo(position);
        SetBehaviorAndPriority(behavior, priority);
    }

    private Vector3 CalculateDistributedWorkPosition(Vector3 origin, Vector2Int size)
    {
        Vector3 center = origin + new Vector3(size.x / 2f, 0, size.y / 2f);
        float halfX = size.x / 2f + 0.3f;
        float halfZ = size.y / 2f + 0.3f;

        debugBuildingCenter = center;
        debugBoxHalfX = halfX;
        debugBoxHalfZ = halfZ;

        Vector3 targetPos = center + new Vector3(
            Random.Range(-halfX, halfX), 0,
            Random.Range(-halfZ, halfZ)
        );

        return NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)
            ? hit.position : center;
    }

    public void UpdateAssignedWorkPosition(Vector3 newOrigin, Vector2Int size)
    {
        if (!taskContext.HasTask) return;

        Vector3 newWorkPos = CalculateDistributedWorkPosition(newOrigin, size);
        taskContext.SetRelocating(newWorkPos);
        taskContext.TargetSize = size;
        bb.TargetPosition = newWorkPos;
        unit.MoveTo(newWorkPos);
    }

    // ==================== Working ====================

    private void UpdateWorking()
    {
        if (!taskContext.HasTask || !ValidateTaskTarget(taskContext.Task))
        {
            CompleteCurrentTask();
            return;
        }

        switch (taskContext.Phase)
        {
            case TaskPhase.MovingToWork:
            case TaskPhase.Relocating:
                UpdateMovingToWork();
                break;
            case TaskPhase.Working:
                UpdateExecutingWork();
                break;
        }
    }

    private void UpdateWorkingAtStation()
    {
        // ★ null 체크 먼저
        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        switch (taskContext.Phase)
        {
            case TaskPhase.MovingToWork:
                UpdateMovingToWorkstation();
                break;
            case TaskPhase.Working:
                UpdateExecutingWorkstation();
                break;
        }
    }

    private void UpdateMovingToWork()
    {
        float dist = Vector3.Distance(transform.position, taskContext.WorkPosition);

        if (dist <= workRadius)
        {
            taskContext.SetWorking();
            return;
        }

        // 도착했는데 거리가 멀면 다시 이동 시도
        if (unit.HasArrivedAtDestination() || agent.velocity.magnitude < 0.1f)
        {
            if (NavMesh.SamplePosition(taskContext.WorkPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                unit.MoveTo(hit.position);
            }
            else
            {
                unit.MoveTo(taskContext.WorkPosition);
            }
        }
    }

    private void UpdateMovingToWorkstation()
    {
        // ★ null 체크
        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        float dist = Vector3.Distance(transform.position, taskContext.WorkPosition);

        if (dist <= workRadius)
        {
            if (!currentWorkstation.AssignWorker(unit))
            {
                CompleteCurrentTask();
                return;
            }
            taskContext.SetWorking();
            return;
        }

        if (unit.HasArrivedAtDestination() || agent.velocity.magnitude < 0.1f)
        {
            unit.MoveTo(taskContext.WorkPosition);
        }
    }

    private void UpdateExecutingWork()
    {
        taskContext.WorkTimer += Time.deltaTime;

        if (taskContext.WorkTimer >= 1f)
        {
            taskContext.WorkTimer = 0f;
            PerformWork(taskContext.Task);
        }
    }

    /// <summary>
    /// ★ Workstation 작업 실행 - 안전한 null 체크 추가
    /// </summary>
    private void UpdateExecutingWorkstation()
    {
        // ★ 매번 null 체크
        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        // 작업 시작
        if (!isWorkstationWorkStarted && currentWorkstation.CanStartWork)
        {
            currentWorkstation.StartWork();
            isWorkstationWorkStarted = true;
        }

        // 작업 수행
        taskContext.WorkTimer += Time.deltaTime;
        if (taskContext.WorkTimer >= 1f)
        {
            taskContext.WorkTimer = 0f;
            float workAmount = unit.DoWork();

            // ★ DoWork 전에 참조 저장 (DoWork 내부에서 상태 변경될 수 있음)
            var workstation = currentWorkstation;
            if (workstation != null)
            {
                workstation.DoWork(workAmount);
            }
        }

        // ★ DoWork 후 다시 null 체크 (DoWork 내부에서 완료되어 해제될 수 있음)
        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        // 워커가 해제되었다면 완료 (외부에서 해제된 경우)
        if (!currentWorkstation.IsOccupied && isWorkstationWorkStarted)
        {
            TaskManager.Instance?.CompleteTask(taskContext.Task);
            CompleteCurrentTask();
            return;
        }

        // 완료 체크: 더 이상 할 일이 없고 작업이 시작되었다면
        if (isWorkstationWorkStarted && !currentWorkstation.CanStartWork)
        {
            // 완전히 완료
            currentWorkstation.ReleaseWorker();
            TaskManager.Instance?.CompleteTask(taskContext.Task);
            CompleteCurrentTask();
        }
    }

    private bool ValidateTaskTarget(PostedTask task)
    {
        return task.Data.Type switch
        {
            TaskType.Construct => task.Owner is Building b && b.CurrentState != BuildingState.Completed,
            TaskType.Harvest => task.Owner is ResourceNode n && !n.IsDepleted,
            TaskType.Workstation => task.Owner is IWorkstation,
            _ => task.Owner != null
        };
    }

    private void PerformWork(PostedTask task)
    {
        switch (task.Data.Type)
        {
            case TaskType.Construct:
                PerformConstruction(task);
                break;
            case TaskType.Harvest:
                PerformHarvest(task);
                break;
        }
    }

    /// <summary>
    /// 건설 수행 - 완료 시 지속 명령 처리
    /// </summary>
    private void PerformConstruction(PostedTask task)
    {
        var building = task.Owner as Building;
        if (building == null || building.CurrentState == BuildingState.Completed)
        {
            OnWorkComplete(task);
            return;
        }

        float work = unit.DoWork();
        if (building.DoConstructionWork(work))
        {
            TaskManager.Instance?.CompleteTask(task);
            OnWorkComplete(task);
        }
    }

    /// <summary>
    /// ★ 채집 수행
    /// </summary>
    private void PerformHarvest(PostedTask task)
    {
        var node = task.Owner as ResourceNode;
        if (node == null || node.IsDepleted)
        {
            // 노드 고갈 → 작업 완료
            OnWorkComplete(task);
            return;
        }

        // 인벤토리 공간 체크 (채집 전)
        ResourceItemSO nodeResource = GetNodeResource(node);
        if (unit.Inventory.IsFull || (nodeResource != null && !unit.Inventory.CanAddAny(nodeResource)))
        {
            // 인벤 가득 → 작업 중단하고 루프로
            Debug.Log($"[UnitAI] {unit.UnitName}: 채집 중 인벤 가득 → 루프로");
            TaskManager.Instance?.LeaveTask(task, unit);
            taskContext.Clear();
            bb.CurrentTask = null;
            ContinuePersistentCommand();
            return;
        }

        float gather = unit.DoGather(node.Data?.NodeType);
        var drops = node.Harvest(gather);

        foreach (var drop in drops)
        {
            AddPersonalItem(drop);
        }

        if (node.IsDepleted)
        {
            OnWorkComplete(task);
        }
    }

    private ResourceItemSO GetNodeResource(ResourceNode node)
    {
        if (node?.Data?.Drops == null) return null;
        foreach (var drop in node.Data.Drops)
        {
            if (drop.Resource != null) return drop.Resource;
        }
        return null;
    }

    // ==================== ★ 작업 완료 처리 ====================

    /// <summary>
    /// ★ 작업 완료 시 지속 명령 루프로 복귀
    /// </summary>
    private void OnWorkComplete(PostedTask task)
    {
        // 워크스테이션 정리
        if (currentWorkstation != null)
        {
            currentWorkstation.ReleaseWorker();
            currentWorkstation = null;
            isWorkstationWorkStarted = false;
        }

        if (task != null)
        {
            TaskManager.Instance?.LeaveTask(task, unit);
        }

        taskContext.Clear();
        bb.CurrentTask = null;
        bb.TargetObject = null;
        bb.TargetPosition = null;
        pickupTimer = 0f;

        // ★ 지속 명령이 있으면 루프 계속
        if (bb.HasPersistentCommand)
        {
            Debug.Log($"[UnitAI] {unit.UnitName}: 작업 완료 → 루프 계속");
            ContinuePersistentCommand();
            return;
        }

        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    // ==================== Task Completion ====================

    protected void CompleteCurrentTask()
    {
        // 워크스테이션 정리
        if (currentWorkstation != null)
        {
            currentWorkstation.ReleaseWorker();
            currentWorkstation = null;
            isWorkstationWorkStarted = false;
        }

        if (taskContext.Task != null)
        {
            TaskManager.Instance?.LeaveTask(taskContext.Task, unit);
        }

        taskContext.Clear();
        bb.CurrentTask = null;
        bb.TargetObject = null;
        bb.TargetPosition = null;
        pickupTimer = 0f;

        // ★ 지속 명령이 있으면 루프 계속
        if (bb.HasPersistentCommand)
        {
            ContinuePersistentCommand();
            return;
        }

        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    protected void InterruptCurrentTask()
    {
        // ★ 음식 찾기 취소
        CancelSeekingFood();

        if (currentWorkstation != null)
        {
            currentWorkstation.CancelWork();
            currentWorkstation.ReleaseWorker();
            currentWorkstation = null;
            isWorkstationWorkStarted = false;
        }

        if (taskContext.Task != null)
        {
            TaskManager.Instance?.LeaveTask(taskContext.Task, unit);
        }
        taskContext.Clear();
        bb.CurrentTask = null;
    }
}