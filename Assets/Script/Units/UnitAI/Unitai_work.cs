using UnityEngine;
using UnityEngine.AI;


public partial class UnitAI
{
    // ==================== Task Management ====================

    private bool TryPullTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task == null) return false;

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

        if (unit.HasArrivedAtDestination() || agent.velocity.magnitude < 0.1f)
        {
            // NavMesh 
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

    private void UpdateExecutingWorkstation()
    {
        if (currentWorkstation == null)
        {
            CompleteCurrentTask();
            return;
        }

        if (!isWorkstationWorkStarted && currentWorkstation.CanStartWork)
        {
            currentWorkstation.StartWork();
            isWorkstationWorkStarted = true;
        }

        taskContext.WorkTimer += Time.deltaTime;
        if (taskContext.WorkTimer >= 1f)
        {
            taskContext.WorkTimer = 0f;
            float workAmount = unit.DoWork();
            currentWorkstation.DoWork(workAmount);
        }

        var wsComponent = currentWorkstation as WorkstationComponent;
        if (wsComponent != null && !wsComponent.IsWorking && isWorkstationWorkStarted)
        {
            if (currentWorkstation.CanStartWork)
            {
                isWorkstationWorkStarted = false;
            }
            else
            {
                currentWorkstation.ReleaseWorker();
                TaskManager.Instance?.CompleteTask(taskContext.Task);
                CompleteCurrentTask();
            }
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

    private void PerformConstruction(PostedTask task)
    {
        var building = task.Owner as Building;
        if (building == null || building.CurrentState == BuildingState.Completed)
        {
            CompleteCurrentTask();
            return;
        }

        float work = unit.DoWork();
        if (building.DoConstructionWork(work))
        {
            TaskManager.Instance?.CompleteTask(task);
            CompleteCurrentTask();
        }
    }

    private void PerformHarvest(PostedTask task)
    {
        var node = task.Owner as ResourceNode;
        if (node == null || node.IsDepleted)
        {
            CompleteCurrentTask();
            TryPickupPersonalItems();
            return;
        }

        
        ResourceItemSO nodeResource = GetNodeResource(node);
        if (nodeResource != null && ShouldDepositInventory(nodeResource))
        {
            previousTask = task;
            TaskManager.Instance?.LeaveTask(task, unit);
            taskContext.Clear();
            bb.CurrentTask = null;
            StartDeliveryToStorage();
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
            previousTask = task;
            CompleteCurrentTask();
            TryPickupPersonalItems();
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

    // ==================== Task Completion ====================

    protected void CompleteCurrentTask()
    {
        // 워크스테이션 
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

        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);
    }

    protected void InterruptCurrentTask()
    {
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