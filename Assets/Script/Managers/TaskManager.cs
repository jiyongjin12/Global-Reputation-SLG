using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

/// <summary>
/// 작업 게시판 (Job Board 패턴)
/// 유닛에게 직접 할당하지 않고, 유닛이 Pull하는 방식
/// </summary>
public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    private List<PostedTask> postedTasks = new();

    [Header("Settings")]
    [SerializeField] private bool showDebugLogs = true;

    // 이벤트
    public event Action<PostedTask> OnTaskPosted;
    public event Action<PostedTask> OnTaskTaken;
    public event Action<PostedTask> OnTaskCompleted;

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
            if (posted.State != PostedTaskState.Available && posted.State != PostedTaskState.InProgress)
                continue;

            if (posted.CurrentWorkers >= posted.Data.MaxWorkers)
                continue;

            if (preferredTypes != null && preferredTypes.Length > 0)
            {
                if (!preferredTypes.Contains(posted.Data.Type))
                    continue;
            }

            if (!CanUnitDoTask(unit, posted.Data))
                continue;

            return posted;
        }

        return null;
    }

    /// <summary>
    /// 가장 가까운 적합한 작업 찾기
    /// </summary>
    public PostedTask FindNearestTask(Unit unit, TaskType[] preferredTypes = null)
    {
        PostedTask nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var posted in postedTasks)
        {
            if (posted.State != PostedTaskState.Available && posted.State != PostedTaskState.InProgress)
                continue;

            if (posted.CurrentWorkers >= posted.Data.MaxWorkers)
                continue;

            if (preferredTypes != null && preferredTypes.Length > 0)
            {
                if (!preferredTypes.Contains(posted.Data.Type))
                    continue;
            }

            if (!CanUnitDoTask(unit, posted.Data))
                continue;

            float dist = Vector3.Distance(unit.transform.position, posted.Data.TargetPosition);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = posted;
            }
        }

        return nearest;
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

    public int GetAvailableTaskCount(TaskType? type = null)
    {
        return postedTasks.Count(t =>
            (t.State == PostedTaskState.Available || t.State == PostedTaskState.InProgress) &&
            (type == null || t.Data.Type == type));
    }

    // ==================== 호환성: 기존 메서드 유지 ====================

    /// <summary>
    /// 유닛 등록 (호환성 - 이제 필요 없지만 에러 방지)
    /// </summary>
    public void RegisterUnit(Unit unit) { }

    /// <summary>
    /// 유닛 해제 (호환성)
    /// </summary>
    public void UnregisterUnit(Unit unit) { }

    /// <summary>
    /// 건설 작업 추가 (호환성 - Building이 직접 등록하므로 비워둠)
    /// </summary>
    public void AddConstructionTask(Building building) { }
}