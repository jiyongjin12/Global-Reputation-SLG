using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 작업 정의 데이터
/// </summary>
[Serializable]
public class TaskData
{
    public TaskType Type;
    public TaskPriority Priority;
    public Vector3 TargetPosition;
    public GameObject TargetObject;
    public int MaxWorkers = 1;
    public float WorkRequired = 10f;

    public TaskData(TaskType type, Vector3 position, GameObject target = null)
    {
        Type = type;
        TargetPosition = position;
        TargetObject = target;
        Priority = TaskPriority.Normal;
        MaxWorkers = 1;
    }
}

/// <summary>
/// 게시된 작업 (TaskManager에서 관리)
/// </summary>
public class PostedTask
{
    public TaskData Data { get; private set; }
    public object Owner { get; private set; }
    public PostedTaskState State { get; set; } = PostedTaskState.Available;
    public int CurrentWorkers { get; set; } = 0;
    public List<Unit> AssignedUnits { get; private set; } = new();
    public float CurrentProgress { get; set; } = 0f;

    public PostedTask(TaskData data, object owner)
    {
        Data = data;
        Owner = owner;
    }
}

public enum PostedTaskState
{
    Available,
    InProgress,
    Full,
    Completed,
    Cancelled
}