using System;
using UnityEngine;

public class Building : MonoBehaviour
{
    [SerializeField] private ObjectData data;
    [SerializeField] private BuildingState currentState = BuildingState.Blueprint;

    [Header("=== 작업 지점 ===")]
    [Tooltip("Unit이 작업할 때 서 있을 위치 (없으면 자동 생성)")]
    [SerializeField] private Transform workPoint;

    [Tooltip("생산된 아이템이 드롭될 위치 (없으면 자동 생성)")]
    [SerializeField] private Transform dropPoint;

    private float currentConstructionWork = 0f;
    private GameObject currentVisual;
    private PostedTask constructionTask;
    private bool isInitialized = false;

    // 캐시된 컴포넌트
    private IWorkstation _workstation;
    private IStorage _storage;
    private IHarvestable _harvestable;
    private IInteractable _interactable;
    private CraftingBuildingComponent _craftingBuilding;

    public event Action<Building> OnConstructionComplete;
    public event Action<Building, float> OnConstructionProgress;

    // 기존 Properties
    public ObjectData Data => data;
    public BuildingState CurrentState => currentState;
    public Vector3Int GridPosition { get; private set; }
    public bool NeedsConstruction => currentState == BuildingState.Blueprint || currentState == BuildingState.UnderConstruction;
    public bool IsInitialized => isInitialized;
    public float ConstructionProgress => data != null && data.ConstructionWorkRequired > 0
        ? currentConstructionWork / data.ConstructionWorkRequired : 1f;

    // Transform Properties
    public Transform WorkPoint => workPoint;
    public Transform DropPoint => dropPoint;

    // 컴포넌트 접근
    public IWorkstation Workstation => _workstation;
    public IStorage Storage => _storage;
    public IHarvestable Harvestable => _harvestable;
    public IInteractable Interactable => _interactable;
    public CraftingBuildingComponent CraftingBuilding => _craftingBuilding;

    // 기능 체크
    public bool HasWorkstation => _workstation != null;
    public bool HasStorage => _storage != null;
    public bool HasHarvestable => _harvestable != null;
    public bool IsInteractable => _interactable != null;
    public bool HasCraftingBuilding => _craftingBuilding != null;

    private void Awake()
    {
        CacheComponents();
        EnsureRequiredPoints();
    }

    private void CacheComponents()
    {
        _workstation = GetComponent<IWorkstation>();
        _storage = GetComponent<IStorage>();
        _harvestable = GetComponent<IHarvestable>();
        _interactable = GetComponent<IInteractable>();
        _craftingBuilding = GetComponent<CraftingBuildingComponent>();
    }

    private void EnsureRequiredPoints()
    {
        if (workPoint == null)
        {
            GameObject wp = new GameObject("WorkPoint");
            wp.transform.SetParent(transform);
            wp.transform.localPosition = new Vector3(0, 0, -1f);
            workPoint = wp.transform;
        }

        if (dropPoint == null)
        {
            GameObject dp = new GameObject("DropPoint");
            dp.transform.SetParent(transform);
            dp.transform.localPosition = new Vector3(0, 0.5f, 0);
            dropPoint = dp.transform;
        }
    }

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

        if (_workstation == null)
            CacheComponents();

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

    public void UpdateGridPosition(Vector3Int newGridPosition)
    {
        GridPosition = newGridPosition;
        Debug.Log($"[Building] {data?.Name} GridPosition 업데이트: {newGridPosition}");
    }

    public void UpdateTaskLocation(Vector3 newWorldPosition)
    {
        if (constructionTask == null) return;

        constructionTask.Data.TargetPosition = newWorldPosition;
        Vector2Int size = data?.Size ?? Vector2Int.one;

        foreach (var unit in constructionTask.AssignedUnits)
        {
            if (unit != null && unit.IsAlive)
            {
                var unitAI = unit.GetComponent<UnitAI>();
                if (unitAI != null)
                {
                    unitAI.UpdateAssignedWorkPosition(newWorldPosition, size);
                }
                else
                {
                    unit.MoveTo(newWorldPosition);
                }
            }
        }

        Debug.Log($"[Building] {data?.Name} Task 위치 업데이트: {newWorldPosition}, 유닛 {constructionTask.AssignedUnits.Count}명");
    }

    public PostedTask GetConstructionTask() => constructionTask;

    private void RegisterConstructionTask()
    {
        if (TaskManager.Instance == null) return;

        var taskData = new TaskData(TaskType.Construct, transform.position, gameObject)
        {
            Priority = TaskPriority.Normal,
            MaxWorkers = Mathf.Clamp(data.Size.x * data.Size.y + 1, 2, 4),
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

        BuildingManager.Instance?.RegisterBuilding(this);

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

        BuildingManager.Instance?.UnregisterBuilding(this);

        GridDataManager.Instance?.RemoveObject(GridPosition);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (constructionTask != null)
            TaskManager.Instance?.CancelTask(constructionTask);

        BuildingManager.Instance?.UnregisterBuilding(this);
    }

    // 유틸리티 메서드
    public T GetBuildingComponent<T>() where T : class
    {
        return GetComponent<T>();
    }

    public Vector3 GetWorldCenter()
    {
        if (data != null)
        {
            return new Vector3(
                GridPosition.x + data.Size.x * 0.5f,
                0,
                GridPosition.z + data.Size.y * 0.5f
            );
        }
        return transform.position;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (workPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(workPoint.position, 0.3f);
            Gizmos.DrawLine(transform.position, workPoint.position);
        }

        if (dropPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(dropPoint.position, 0.2f);
        }
    }
#endif
}