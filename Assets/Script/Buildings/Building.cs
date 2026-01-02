using System;
using UnityEngine;

public class Building : MonoBehaviour
{
    [SerializeField] private ObjectData data;
    [SerializeField] private BuildingState currentState = BuildingState.Blueprint;

    private float currentConstructionWork = 0f;
    private GameObject currentVisual;
    private PostedTask constructionTask;
    private bool isInitialized = false;

    public event Action<Building> OnConstructionComplete;
    public event Action<Building, float> OnConstructionProgress;

    public ObjectData Data => data;
    public BuildingState CurrentState => currentState;
    public Vector3Int GridPosition { get; private set; }
    public bool NeedsConstruction => currentState == BuildingState.Blueprint || currentState == BuildingState.UnderConstruction;
    public bool IsInitialized => isInitialized;
    public float ConstructionProgress => data != null && data.ConstructionWorkRequired > 0
        ? currentConstructionWork / data.ConstructionWorkRequired : 1f;

    public void Initialize(ObjectData objectData, Vector3Int gridPos, bool instantBuild = false)
    {
        if (objectData == null)
        {
            Debug.LogError("[Building] Initialize failed!");
            return;
        }

        data = objectData;
        GridPosition = gridPos;
        isInitialized = true;

        if (instantBuild || data.ConstructionWorkRequired <= 0)
        {
            CompleteConstruction();
        }
        else
        {
            SetState(BuildingState.Blueprint);
            UpdateVisual();
            RegisterConstructionTask();
        }
    }

    /// <summary>
    /// ★ Grid 위치 업데이트 (이동 시 사용)
    /// </summary>
    public void UpdateGridPosition(Vector3Int newGridPosition)
    {
        GridPosition = newGridPosition;
        Debug.Log($"[Building] {data?.Name} GridPosition 업데이트: {newGridPosition}");
    }

    /// <summary>
    /// ★ Task 위치 업데이트 (건물 이동 시 호출)
    /// - 건설 작업의 목표 위치를 새 위치로 변경
    /// - 작업 중인 유닛들에게 새 위치로 이동하라고 알림
    /// </summary>
    public void UpdateTaskLocation(Vector3 newWorldPosition)
    {
        if (constructionTask == null) return;

        // 1. TaskData의 위치 업데이트
        constructionTask.Data.TargetPosition = newWorldPosition;

        // 2. 작업 중인 유닛들에게 새 위치로 이동하라고 알림
        foreach (var unit in constructionTask.AssignedUnits)
        {
            if (unit != null && unit.IsAlive)
            {
                // 유닛을 새 위치로 이동시킴
                unit.MoveTo(newWorldPosition);
                Debug.Log($"[Building] {unit.UnitName}을(를) 새 위치로 이동시킴: {newWorldPosition}");
            }
        }

        Debug.Log($"[Building] {data?.Name} Task 위치 업데이트: {newWorldPosition}");
    }

    /// <summary>
    /// ★ 건설 Task 가져오기 (외부에서 확인용)
    /// </summary>
    public PostedTask GetConstructionTask() => constructionTask;

    /// <summary>
    /// 스스로 TaskManager에 등록
    /// </summary>
    private void RegisterConstructionTask()
    {
        if (TaskManager.Instance == null) return;

        var taskData = new TaskData(TaskType.Construct, transform.position, gameObject)
        {
            Priority = TaskPriority.Normal,
            MaxWorkers = Mathf.Clamp(data.Size.x * data.Size.y, 1, 3),
            WorkRequired = data.ConstructionWorkRequired
        };

        constructionTask = TaskManager.Instance.PostTask(taskData, this);
        Debug.Log($"[Building] {data.Name} 건설 작업 등록");
    }

    public bool DoConstructionWork(float workAmount)
    {
        if (currentState == BuildingState.Completed) return true;

        if (currentState == BuildingState.Blueprint)
            SetState(BuildingState.UnderConstruction);

        currentConstructionWork += workAmount;
        OnConstructionProgress?.Invoke(this, ConstructionProgress);

        if (constructionTask != null)
            constructionTask.CurrentProgress = ConstructionProgress;

        Debug.Log($"[Building] {data.Name}: {ConstructionProgress * 100:F0}%");

        if (currentConstructionWork >= data.ConstructionWorkRequired)
        {
            CompleteConstruction();
            return true;
        }
        return false;
    }

    private void CompleteConstruction()
    {
        currentConstructionWork = data?.ConstructionWorkRequired ?? 0;
        SetState(BuildingState.Completed);
        UpdateVisual();

        if (constructionTask != null)
        {
            TaskManager.Instance?.CompleteTask(constructionTask);
            constructionTask = null;
        }

        OnConstructionComplete?.Invoke(this);
        Debug.Log($"[Building] {data?.Name} 건설 완료!");
    }

    private void SetState(BuildingState newState) => currentState = newState;

    private void UpdateVisual()
    {
        if (currentVisual != null) Destroy(currentVisual);

        GameObject prefab = currentState == BuildingState.Completed
            ? data.Prefab : (data.BlueprintPrefab ?? data.Prefab);

        if (prefab != null)
        {
            currentVisual = Instantiate(prefab, transform);
            currentVisual.transform.localPosition = Vector3.zero;
        }
    }

    public void Demolish()
    {
        if (constructionTask != null)
            TaskManager.Instance?.CancelTask(constructionTask);

        GridDataManager.Instance?.RemoveObject(GridPosition);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (constructionTask != null)
            TaskManager.Instance?.CancelTask(constructionTask);
    }
}