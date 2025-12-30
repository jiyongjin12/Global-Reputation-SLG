using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 작업 게시판 (Job Board 패턴)
/// 유닛에게 직접 할당하지 않고, 유닛이 Pull하는 방식
/// 우선순위: 건설 > 채집 > 아이템 줍기 > 기타
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
            Destroy(gameObject);
    }

    private void Update()
    {
        CleanupTasks();
    }

    // ==================== 작업 게시 ====================

    /// <summary>
    /// 작업 게시 (Building, ResourceNode 등이 호출)
    /// </summary>
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

    /// <summary>
    /// 채집 작업 등록 (ResourceNode/MapGenerator에서 호출)
    /// </summary>
    public PostedTask AddHarvestTask(ResourceNode node)
    {
        if (node == null || node.IsDepleted) return null;

        // 이미 등록된 작업인지 확인
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

    /// <summary>
    /// 아이템 줍기 작업 등록 (드랍 아이템)
    /// 애니메이션 중이어도 등록 (실제 줍기 시 체크)
    /// </summary>
    public PostedTask AddPickupItemTask(DroppedItem item)
    {
        // null 체크만 (IsAvailable은 줍기 시 체크)
        if (item == null) return null;

        // 이미 등록된 작업인지 확인
        var existing = postedTasks.FirstOrDefault(t =>
            t.Owner == item &&
            t.Data.Type == TaskType.PickupItem &&
            t.State != PostedTaskState.Completed &&
            t.State != PostedTaskState.Cancelled);

        if (existing != null) return existing;

        var taskData = new TaskData(TaskType.PickupItem, item.transform.position, item.gameObject)
        {
            Priority = TaskPriority.Normal, // 채집보다 높은 우선순위
            MaxWorkers = 1,
            WorkRequired = 0f
        };

        var posted = PostTask(taskData, item);
        item.OnPickedUp += HandleItemPickedUp;

        return posted;
    }

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

    /// <summary>
    /// 작업 취소
    /// </summary>
    public void CancelTask(PostedTask task)
    {
        if (task == null) return;
        task.State = PostedTaskState.Cancelled;
        postedTasks.Remove(task);
    }

    // ==================== 작업 가져오기 (Pull) ====================

    /// <summary>
    /// 적합한 작업 찾기 (UnitAI가 호출)
    /// </summary>
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
    /// ★ 가장 가까운 적합한 작업 찾기 (작업 타입 우선순위 적용)
    /// 
    /// ★ 우선순위 (절대적): 건설 > 아이템 > 채집
    /// ★ 선호도 (같은 우선순위 내에서): 미할당 > 협력 가능
    /// 
    /// 즉: 건설(협력) > 아이템(미할당) > 채집(미할당)
    /// </summary>
    public PostedTask FindNearestTask(Unit unit, TaskType[] preferredTypes = null)
    {
        // 1단계: 우선순위별로 작업 분류 (미할당 / 협력가능)
        List<PostedTask> constructUnassigned = new();
        List<PostedTask> constructCooperable = new();
        List<PostedTask> pickupUnassigned = new();
        List<PostedTask> pickupCooperable = new();
        List<PostedTask> harvestUnassigned = new();
        List<PostedTask> harvestCooperable = new();

        foreach (var posted in postedTasks)
        {
            var availability = GetTaskAvailability(posted, unit, preferredTypes);
            if (availability == TaskAvailability.NotAvailable)
                continue;

            bool isUnassigned = (availability == TaskAvailability.Unassigned);

            switch (posted.Data.Type)
            {
                case TaskType.Construct:
                    if (isUnassigned) constructUnassigned.Add(posted);
                    else constructCooperable.Add(posted);
                    break;
                case TaskType.PickupItem:
                    if (isUnassigned) pickupUnassigned.Add(posted);
                    else pickupCooperable.Add(posted);
                    break;
                case TaskType.Harvest:
                    if (isUnassigned) harvestUnassigned.Add(posted);
                    else harvestCooperable.Add(posted);
                    break;
            }
        }

        if (showDebugLogs)
        {
            Debug.Log($"[TaskManager] {unit.UnitName} FindNearestTask:");
            Debug.Log($"  건설: 미할당={constructUnassigned.Count}, 협력={constructCooperable.Count}");
            Debug.Log($"  아이템: 미할당={pickupUnassigned.Count}, 협력={pickupCooperable.Count}");
            Debug.Log($"  채집: 미할당={harvestUnassigned.Count}, 협력={harvestCooperable.Count}");
        }

        // 2단계: 우선순위 순서대로 가장 가까운 작업 찾기
        // ★ 우선순위가 절대적! 건설이 있으면 무조건 건설 먼저 (협력이라도)

        // ★ 1순위: 건설 (미할당 먼저, 없으면 협력)
        PostedTask result = FindNearestInList(unit, constructUnassigned);
        if (result != null)
        {
            if (showDebugLogs)
                Debug.Log($"[TaskManager] → 건설(미할당) 선택: {result.Data.TargetObject?.name}");
            return result;
        }
        result = FindNearestInList(unit, constructCooperable);
        if (result != null)
        {
            if (showDebugLogs)
                Debug.Log($"[TaskManager] → 건설(협력) 선택: {result.Data.TargetObject?.name}");
            return result;
        }

        // ★ 2순위: 아이템 줍기 (미할당 먼저, 없으면 협력)
        result = FindNearestInList(unit, pickupUnassigned);
        if (result != null)
        {
            if (showDebugLogs)
                Debug.Log($"[TaskManager] → 아이템(미할당) 선택");
            return result;
        }
        result = FindNearestInList(unit, pickupCooperable);
        if (result != null)
        {
            if (showDebugLogs)
                Debug.Log($"[TaskManager] → 아이템(협력) 선택");
            return result;
        }

        // ★ 3순위: 자원 채집 (미할당 먼저, 없으면 협력)
        result = FindNearestInList(unit, harvestUnassigned);
        if (result != null)
        {
            if (showDebugLogs)
                Debug.Log($"[TaskManager] → 채집(미할당) 선택");
            return result;
        }
        result = FindNearestInList(unit, harvestCooperable);
        if (result != null)
        {
            if (showDebugLogs)
                Debug.Log($"[TaskManager] → 채집(협력) 선택");
            return result;
        }

        if (showDebugLogs)
            Debug.Log($"[TaskManager] → 작업 없음");
        return null;
    }

    /// <summary>
    /// 작업 가용성 (미할당 / 협력가능 / 불가)
    /// </summary>
    private enum TaskAvailability
    {
        NotAvailable,   // 작업 불가
        Unassigned,     // 미할당 (아무도 안 함)
        Cooperable      // 협력 가능 (다른 유닛이 하고 있지만 참여 가능)
    }

    /// <summary>
    /// 작업 가용성 체크 (미할당/협력가능/불가)
    /// </summary>
    private TaskAvailability GetTaskAvailability(PostedTask posted, Unit unit, TaskType[] preferredTypes)
    {
        // 상태 체크
        if (posted.State == PostedTaskState.Completed ||
            posted.State == PostedTaskState.Cancelled)
            return TaskAvailability.NotAvailable;

        // 이미 이 유닛이 할당됨
        if (posted.AssignedUnits.Contains(unit))
            return TaskAvailability.NotAvailable;

        // MaxWorkers 초과
        if (posted.CurrentWorkers >= posted.Data.MaxWorkers)
            return TaskAvailability.NotAvailable;

        // 타입 필터
        if (preferredTypes != null && preferredTypes.Length > 0)
        {
            if (!preferredTypes.Contains(posted.Data.Type))
                return TaskAvailability.NotAvailable;
        }

        // 유닛 능력 체크
        if (!CanUnitDoTask(unit, posted.Data))
            return TaskAvailability.NotAvailable;

        // 미할당 vs 협력가능
        if (posted.CurrentWorkers == 0)
            return TaskAvailability.Unassigned;
        else
            return TaskAvailability.Cooperable;
    }

    /// <summary>
    /// 리스트에서 가장 가까운 작업 찾기
    /// </summary>
    private PostedTask FindNearestInList(Unit unit, List<PostedTask> tasks)
    {
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

    /// <summary>
    /// 작업 가용성 체크
    /// Full 상태가 아니면 협력 작업 가능
    /// </summary>
    private bool IsTaskAvailable(PostedTask posted, Unit unit, TaskType[] preferredTypes)
    {
        return GetTaskAvailability(posted, unit, preferredTypes) != TaskAvailability.NotAvailable;
    }

    /// <summary>
    /// 작업 수락
    /// </summary>
    public bool TakeTask(PostedTask task, Unit unit)
    {
        if (task == null || task.State == PostedTaskState.Cancelled)
            return false;

        if (task.CurrentWorkers >= task.Data.MaxWorkers)
            return false;

        task.AssignedUnits.Add(unit);
        task.CurrentWorkers++;

        if (task.CurrentWorkers >= task.Data.MaxWorkers)
            task.State = PostedTaskState.Full;
        else
            task.State = PostedTaskState.InProgress;

        OnTaskTaken?.Invoke(task);

        if (showDebugLogs)
            Debug.Log($"[TaskManager] {unit.UnitName} 작업 수락: {task.Data.Type}");

        return true;
    }

    /// <summary>
    /// 작업 완료
    /// </summary>
    public void CompleteTask(PostedTask task)
    {
        if (task == null) return;
        task.State = PostedTaskState.Completed;
        postedTasks.Remove(task);
        OnTaskCompleted?.Invoke(task);

        if (showDebugLogs)
            Debug.Log($"[TaskManager] 작업 완료: {task.Data.Type}");
    }

    /// <summary>
    /// 작업 이탈
    /// </summary>
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

    /// <summary>
    /// 창고 설정
    /// </summary>
    public void SetStorageBuilding(Building storage)
    {
        storageBuilding = storage;
        Debug.Log($"[TaskManager] 창고 설정됨: {storage?.name}");
    }

    /// <summary>
    /// 창고 자동 찾기
    /// </summary>
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

    /// <summary>
    /// 창고 위치 반환
    /// </summary>
    public Vector3? GetStoragePosition()
    {
        FindStorageBuilding();
        return storageBuilding?.transform.position;
    }

    // ==================== 유틸리티 ====================

    private bool CanUnitDoTask(Unit unit, TaskData data)
    {
        switch (data.Type)
        {
            case TaskType.Construct:
            case TaskType.Harvest:
            case TaskType.PickupItem:
            case TaskType.DeliverToStorage:
                return unit.Type == UnitType.Worker;
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

    /// <summary>
    /// 실제로 참여 가능한 작업 수 (MaxWorkers 체크 포함)
    /// </summary>
    public int GetAvailableTaskCount(TaskType? type = null)
    {
        return postedTasks.Count(t =>
            (t.State == PostedTaskState.Available || t.State == PostedTaskState.InProgress) &&
            t.CurrentWorkers < t.Data.MaxWorkers && // ★ 작업자 수 체크
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
        Debug.Log($"    - 채집: {GetAvailableTaskCount(TaskType.Harvest)}");
        Debug.Log($"    - 아이템: {GetAvailableTaskCount(TaskType.PickupItem)}");
        Debug.Log($"  창고: {storageBuilding?.name ?? "없음"}");

        foreach (var task in postedTasks)
        {
            Debug.Log($"      [{task.Data.Type}] {task.Data.TargetObject?.name} - 작업자: {task.CurrentWorkers}/{task.Data.MaxWorkers}, 상태: {task.State}");
        }
    }

    /// <summary>
    /// 건설 작업 상태 디버그 (UnitAI에서 호출)
    /// </summary>
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
            if (item != null && item.IsAvailable)
            {
                AddPickupItemTask(item);
                count++;
            }
        }
        Debug.Log($"[TaskManager] {count}개 드랍 아이템 등록됨");
    }
}