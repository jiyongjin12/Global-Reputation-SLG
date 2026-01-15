using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 작업 게시판 (Job Board 패턴)
/// 유닛에게 직접 할당하지 않고, 유닛이 Pull하는 방식
/// ★ 우선순위: 건설 > 워크스테이션 > 아이템 줍기 > 채집
/// </summary>
public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    private List<PostedTask> postedTasks = new();

    [Header("Settings")]
    [SerializeField] private bool showDebugLogs = true;

    [Header("Storage")]
    [SerializeField] private Building storageBuilding;

    // 이벤트
    public event Action<PostedTask> OnTaskPosted;
    public event Action<PostedTask> OnTaskTaken;
    public event Action<PostedTask> OnTaskCompleted;

    // Properties
    public Building StorageBuilding => storageBuilding;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(this);
    }

    private void Update()
    {
        CleanupTasks();
    }

    // ==================== 작업 게시 ====================

    public PostedTask PostTask(TaskData taskData, object owner)
    {
        var posted = new PostedTask(taskData, owner);
        postedTasks.Add(posted);
        postedTasks.Sort((a, b) => a.Data.Priority.CompareTo(b.Data.Priority));

        OnTaskPosted?.Invoke(posted);

        if (showDebugLogs)
            Debug.Log($"[TaskManager] 작업 게시: {taskData.Type}");

        return posted;
    }

    public PostedTask AddHarvestTask(ResourceNode node)
    {
        if (node == null || node.IsDepleted) return null;

        var existing = postedTasks.FirstOrDefault(t =>
            t.Owner == node &&
            t.Data.Type == TaskType.Harvest &&
            t.State != PostedTaskState.Completed &&
            t.State != PostedTaskState.Cancelled);

        if (existing != null) return existing;

        var taskData = new TaskData(TaskType.Harvest, node.transform.position, node.gameObject)
        {
            Priority = TaskPriority.Normal,
            MaxWorkers = 2,
            WorkRequired = node.Data?.MaxHP ?? 100f
        };

        var posted = PostTask(taskData, node);
        node.OnDepleted += HandleResourceDepleted;

        return posted;
    }

    public PostedTask AddPickupItemTask(DroppedItem item)
    {
        if (item == null) return null;

        if (!item.IsPublic)
        {
            if (showDebugLogs)
                Debug.Log($"[TaskManager] 개인 소유 아이템 - Task 등록 안함 (Owner: {item.Owner?.UnitName})");
            return null;
        }

        var existing = postedTasks.FirstOrDefault(t =>
            t.Owner == item &&
            t.Data.Type == TaskType.PickupItem &&
            t.State != PostedTaskState.Completed &&
            t.State != PostedTaskState.Cancelled);

        if (existing != null) return existing;

        var taskData = new TaskData(TaskType.PickupItem, item.transform.position, item.gameObject)
        {
            Priority = TaskPriority.Normal,
            MaxWorkers = 1,
            WorkRequired = 0f
        };

        var posted = PostTask(taskData, item);
        item.OnPickedUp += HandleItemPickedUp;

        Debug.Log($"[TaskManager] 공용 아이템 Task 등록: {item.Resource?.ResourceName}");

        return posted;
    }

    // ==================== 워크스테이션 작업 등록 ====================

    public PostedTask AddWorkstationTask(IWorkstation workstation)
    {
        if (workstation == null || !workstation.CanStartWork)
            return null;

        var wsComponent = workstation as MonoBehaviour;
        if (wsComponent == null) return null;

        var existing = postedTasks.FirstOrDefault(t =>
            t.Owner == workstation &&
            t.Data.Type == TaskType.Workstation &&
            t.State != PostedTaskState.Completed &&
            t.State != PostedTaskState.Cancelled);

        if (existing != null) return existing;

        var taskData = new TaskData(TaskType.Workstation, workstation.WorkPoint?.position ?? wsComponent.transform.position, wsComponent.gameObject)
        {
            Priority = TaskPriority.Normal,
            MaxWorkers = 1,
            WorkRequired = 0f
        };

        switch (workstation.TaskType)
        {
            case WorkTaskType.Cooking:
                taskData.Priority = TaskPriority.High;
                break;
            case WorkTaskType.Farming:
                taskData.Priority = TaskPriority.Normal;
                break;
            default:
                taskData.Priority = TaskPriority.Normal;
                break;
        }

        var posted = PostTask(taskData, workstation);

        if (showDebugLogs)
            Debug.Log($"[TaskManager] 워크스테이션 작업 등록: {workstation.TaskType}");

        return posted;
    }

    public void RemoveWorkstationTask(IWorkstation workstation)
    {
        var task = postedTasks.FirstOrDefault(t => t.Owner == workstation);
        if (task != null)
        {
            task.State = PostedTaskState.Cancelled;
            postedTasks.Remove(task);
        }
    }

    // ==================== 이벤트 핸들러 ====================

    private void HandleResourceDepleted(ResourceNode node)
    {
        var task = postedTasks.FirstOrDefault(t => t.Owner == node);
        if (task != null)
        {
            task.State = PostedTaskState.Completed;
            postedTasks.Remove(task);
        }
        node.OnDepleted -= HandleResourceDepleted;
    }

    private void HandleItemPickedUp(DroppedItem item)
    {
        var task = postedTasks.FirstOrDefault(t => t.Owner == item);
        if (task != null)
        {
            task.State = PostedTaskState.Completed;
            postedTasks.Remove(task);
        }
        item.OnPickedUp -= HandleItemPickedUp;
    }

    public void CancelTask(PostedTask task)
    {
        if (task == null) return;
        task.State = PostedTaskState.Cancelled;
        postedTasks.Remove(task);
    }

    // ==================== 작업 가져오기 (Pull) ====================

    public PostedTask FindSuitableTask(Unit unit, TaskType[] preferredTypes = null)
    {
        foreach (var posted in postedTasks)
        {
            if (!IsTaskAvailable(posted, unit, preferredTypes))
                continue;

            return posted;
        }

        return null;
    }

    /// <summary>
    /// 가장 가까운 적합한 작업 찾기 (작업 타입 우선순위 적용)
    /// ★ 우선순위: 건설 > 워크스테이션 > 아이템 줍기 > 채집
    /// </summary>
    public PostedTask FindNearestTask(Unit unit, TaskType[] preferredTypes = null)
    {
        List<PostedTask> constructUnassigned = new();
        List<PostedTask> constructCooperable = new();
        List<PostedTask> workstationUnassigned = new();
        List<PostedTask> itemUnassigned = new();
        List<PostedTask> harvestUnassigned = new();
        List<PostedTask> harvestCooperable = new();

        foreach (var posted in postedTasks)
        {
            var availability = GetTaskAvailability(posted, unit, preferredTypes);
            if (availability == TaskAvailability.NotAvailable)
                continue;

            switch (posted.Data.Type)
            {
                case TaskType.Construct:
                    if (availability == TaskAvailability.Unassigned)
                        constructUnassigned.Add(posted);
                    else
                        constructCooperable.Add(posted);
                    break;
                case TaskType.Workstation:
                    if (availability == TaskAvailability.Unassigned)
                        workstationUnassigned.Add(posted);
                    break;
                case TaskType.PickupItem:
                    if (availability == TaskAvailability.Unassigned)
                        itemUnassigned.Add(posted);
                    break;
                case TaskType.Harvest:
                    if (availability == TaskAvailability.Unassigned)
                        harvestUnassigned.Add(posted);
                    else
                        harvestCooperable.Add(posted);
                    break;
            }
        }

        // 우선순위별로 찾기
        PostedTask result = FindNearestInList(unit, constructUnassigned);
        if (result != null) return result;

        result = FindNearestInList(unit, constructCooperable);
        if (result != null) return result;

        result = FindNearestInList(unit, workstationUnassigned);
        if (result != null) return result;

        result = FindNearestInList(unit, itemUnassigned);
        if (result != null) return result;

        result = FindNearestInList(unit, harvestUnassigned);
        if (result != null) return result;

        result = FindNearestInList(unit, harvestCooperable);
        if (result != null) return result;

        return null;
    }

    /// <summary>
    /// ★ 플레이어 명령용 작업 찾기 (타입 제한 없음)
    /// </summary>
    public PostedTask FindTaskForPlayerCommand(Unit unit, TaskType targetType, GameObject targetObject)
    {
        foreach (var posted in postedTasks)
        {
            if (posted.State == PostedTaskState.Completed ||
                posted.State == PostedTaskState.Cancelled)
                continue;

            if (posted.Data.Type != targetType)
                continue;

            if (targetObject != null && posted.Data.TargetObject != targetObject)
                continue;

            if (posted.CurrentWorkers >= posted.Data.MaxWorkers)
                continue;

            return posted;
        }

        return null;
    }

    private PostedTask FindNearestInList(Unit unit, List<PostedTask> tasks)
    {
        if (tasks.Count == 0) return null;

        PostedTask nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var task in tasks)
        {
            float dist = Vector3.Distance(unit.transform.position, task.Data.TargetPosition);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = task;
            }
        }

        return nearest;
    }

    private enum TaskAvailability
    {
        NotAvailable,
        Unassigned,
        Cooperable
    }

    private TaskAvailability GetTaskAvailability(PostedTask posted, Unit unit, TaskType[] preferredTypes)
    {
        if (posted.State == PostedTaskState.Completed ||
            posted.State == PostedTaskState.Cancelled)
            return TaskAvailability.NotAvailable;

        if (!CanUnitDoTask(unit, posted.Data))
            return TaskAvailability.NotAvailable;

        if (preferredTypes != null && preferredTypes.Length > 0)
        {
            if (!preferredTypes.Contains(posted.Data.Type))
                return TaskAvailability.NotAvailable;
        }

        if (posted.CurrentWorkers >= posted.Data.MaxWorkers)
            return TaskAvailability.NotAvailable;

        if (posted.CurrentWorkers == 0)
            return TaskAvailability.Unassigned;

        return TaskAvailability.Cooperable;
    }

    private bool IsTaskAvailable(PostedTask posted, Unit unit, TaskType[] preferredTypes)
    {
        return GetTaskAvailability(posted, unit, preferredTypes) != TaskAvailability.NotAvailable;
    }

    /// <summary>
    /// 작업 수락
    /// </summary>
    /// <param name="task">수락할 작업</param>
    /// <param name="unit">작업을 수행할 유닛</param>
    /// <param name="isPlayerCommand">★ 플레이어 명령인지 여부 (true면 타입 제한 무시)</param>
    public bool TakeTask(PostedTask task, Unit unit, bool isPlayerCommand = false)
    {
        if (task == null || task.State == PostedTaskState.Cancelled)
            return false;

        if (task.CurrentWorkers >= task.Data.MaxWorkers)
            return false;

        // ★ 플레이어 명령이 아닐 때만 타입 체크
        if (!isPlayerCommand && !CanUnitDoTask(unit, task.Data))
            return false;

        task.AssignedUnits.Add(unit);
        task.CurrentWorkers++;

        if (task.CurrentWorkers >= task.Data.MaxWorkers)
            task.State = PostedTaskState.Full;
        else
            task.State = PostedTaskState.InProgress;

        OnTaskTaken?.Invoke(task);

        if (showDebugLogs)
        {
            string cmdStr = isPlayerCommand ? " (플레이어 명령)" : "";
            Debug.Log($"[TaskManager] {unit.UnitName} 작업 수락: {task.Data.Type}{cmdStr}");
        }

        return true;
    }

    public void CompleteTask(PostedTask task)
    {
        if (task == null) return;
        task.State = PostedTaskState.Completed;
        postedTasks.Remove(task);
        OnTaskCompleted?.Invoke(task);

        if (showDebugLogs)
            Debug.Log($"[TaskManager] 작업 완료: {task.Data.Type}");
    }

    public void LeaveTask(PostedTask task, Unit unit)
    {
        if (task == null) return;

        task.AssignedUnits.Remove(unit);
        task.CurrentWorkers = Mathf.Max(0, task.CurrentWorkers - 1);

        if (task.State != PostedTaskState.Completed && task.State != PostedTaskState.Cancelled)
        {
            task.State = task.CurrentWorkers < task.Data.MaxWorkers
                ? PostedTaskState.Available
                : PostedTaskState.Full;
        }
    }

    // ==================== 창고 관리 ====================

    public void SetStorageBuilding(Building storage)
    {
        storageBuilding = storage;
        Debug.Log($"[TaskManager] 창고 설정됨: {storage?.name}");
    }

    public void FindStorageBuilding()
    {
        if (storageBuilding != null && storageBuilding.CurrentState == BuildingState.Completed)
            return;

        var buildings = FindObjectsOfType<Building>();
        foreach (var b in buildings)
        {
            if (b.Data != null && b.Data.Type == BuildingType.Storage && b.CurrentState == BuildingState.Completed)
            {
                storageBuilding = b;
                Debug.Log($"[TaskManager] 창고 발견: {b.Data.Name}");
                break;
            }
        }
    }

    public Vector3? GetStoragePosition()
    {
        FindStorageBuilding();
        return storageBuilding?.transform.position;
    }

    // ==================== 유틸리티 ====================

    /// <summary>
    /// 자동 작업 시 유닛 타입 체크
    /// ★ 수정: PickupItem은 모든 유닛이 수행 가능 (음식 줍기 등)
    /// </summary>
    private bool CanUnitDoTask(Unit unit, TaskData data)
    {
        switch (data.Type)
        {
            case TaskType.Harvest:
            case TaskType.DeliverToStorage:
            case TaskType.Workstation:
                // Worker만 자동으로 이 작업들 수행
                return unit.Type == UnitType.Worker;

            case TaskType.PickupItem:
                // ★ 모든 유닛이 아이템 줍기 가능
                return true;

            case TaskType.Construct:
                // 건설은 모든 타입 가능
                return true;

            case TaskType.Attack:
                return unit.Type == UnitType.Fighter;

            default:
                return true;
        }
    }

    private void CleanupTasks()
    {
        postedTasks.RemoveAll(t =>
            t.State == PostedTaskState.Completed ||
            t.State == PostedTaskState.Cancelled ||
            t.Owner == null ||
            (t.Owner is UnityEngine.Object obj && obj == null));
    }

    public int GetAvailableTaskCount(TaskType? type = null)
    {
        return postedTasks.Count(t =>
            (t.State == PostedTaskState.Available || t.State == PostedTaskState.InProgress) &&
            t.CurrentWorkers < t.Data.MaxWorkers &&
            (type == null || t.Data.Type == type));
    }

    // ==================== 호환성 ====================

    public void RegisterUnit(Unit unit) { }
    public void UnregisterUnit(Unit unit) { }
    public void AddConstructionTask(Building building) { }

    // ==================== 디버그 ====================

    [ContextMenu("Print Status")]
    public void DebugPrintStatus()
    {
        Debug.Log($"[TaskManager] === 상태 ===");
        Debug.Log($"  대기 작업: {postedTasks.Count}");
        Debug.Log($"    - 건설: {GetAvailableTaskCount(TaskType.Construct)}");
        Debug.Log($"    - 워크스테이션: {GetAvailableTaskCount(TaskType.Workstation)}");
        Debug.Log($"    - 채집: {GetAvailableTaskCount(TaskType.Harvest)}");
        Debug.Log($"    - 아이템: {GetAvailableTaskCount(TaskType.PickupItem)}");
        Debug.Log($"  창고: {storageBuilding?.name ?? "없음"}");

        foreach (var task in postedTasks)
        {
            Debug.Log($"      [{task.Data.Type}] {task.Data.TargetObject?.name} - 작업자: {task.CurrentWorkers}/{task.Data.MaxWorkers}, 상태: {task.State}");
        }
    }

    [ContextMenu("Print Construction Status")]
    public void DebugPrintConstructionStatus()
    {
        var constructTasks = postedTasks.Where(t => t.Data.Type == TaskType.Construct).ToList();

        if (constructTasks.Count == 0)
        {
            Debug.Log($"[TaskManager] 건설 작업 없음");
            return;
        }

        foreach (var task in constructTasks)
        {
            Debug.Log($"[TaskManager] 건설: {task.Data.TargetObject?.name}");
            Debug.Log($"  - 상태: {task.State}");
            Debug.Log($"  - 작업자: {task.CurrentWorkers}/{task.Data.MaxWorkers}");
            Debug.Log($"  - 할당된 유닛: {string.Join(", ", task.AssignedUnits.Select(u => u.UnitName))}");
            Debug.Log($"  - 가용: {task.CurrentWorkers < task.Data.MaxWorkers}");
        }
    }

    [ContextMenu("Register All Resources")]
    public void RegisterAllResources()
    {
        var resources = FindObjectsOfType<ResourceNode>();
        int count = 0;
        foreach (var r in resources)
        {
            if (r != null && !r.IsDepleted)
            {
                AddHarvestTask(r);
                count++;
            }
        }
        Debug.Log($"[TaskManager] {count}개 자원 등록됨");
    }

    [ContextMenu("Register All Dropped Items")]
    public void RegisterAllDroppedItems()
    {
        var items = FindObjectsOfType<DroppedItem>();
        int count = 0;
        foreach (var item in items)
        {
            if (item != null && item.IsAvailable && item.IsPublic)
            {
                AddPickupItemTask(item);
                count++;
            }
        }
        Debug.Log($"[TaskManager] {count}개 공용 드롭 아이템 등록됨");
    }
}