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
    /// TaskManager
    /// </summary>
    private void RegisterConstructionTask()
    {
        if (TaskManager.Instance == null) return;

        var taskData = new TaskData(TaskType.Construct, transform.position, gameObject)
        {
            Priority = TaskPriority.Normal,
            MaxWorkers = Mathf.Max(2, data.Size.x * data.Size.y), // ÃÖ¼Ò 2¸í
            WorkRequired = data.ConstructionWorkRequired
        };

        constructionTask = TaskManager.Instance.PostTask(taskData, this);
        Debug.Log($"[Building] {data.Name}");
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
        Debug.Log($"[Building] {data?.Name}");
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