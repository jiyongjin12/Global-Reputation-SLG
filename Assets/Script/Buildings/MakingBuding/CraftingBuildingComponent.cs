using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 대기열 아이템
/// </summary>
[Serializable]
public class CraftingQueueItem
{
    public RecipeSO Recipe;
    public float Progress;
    public bool IsProcessing;

    public CraftingQueueItem(RecipeSO recipe)
    {
        Recipe = recipe;
        Progress = 0f;
        IsProcessing = false;
    }
}

/// <summary>
/// 제작 건물 컴포넌트
/// </summary>
public class CraftingBuildingComponent : MonoBehaviour, IWorkstation
{
    [Header("=== 건물 설정 ===")]
    [SerializeField] private string buildingType = "Cooking";  // "Cooking" or "Crafting"
    [SerializeField] private int maxQueueSize = 6;
    [SerializeField] private Transform workPoint;
    [SerializeField] private Transform dropPoint;              // 아이템 드롭 위치

    [Header("=== 드롭 아이템 ===")]
    [SerializeField] private GameObject droppedItemPrefab;     // DroppedItem 프리팹

    [Header("=== 레시피 목록 ===")]
    [SerializeField] private List<RecipeSO> availableRecipes = new();

    [Header("=== 상태 (디버그용) ===")]
    [SerializeField] private List<CraftingQueueItem> craftingQueue = new();
    [SerializeField] private Unit currentWorker;
    [SerializeField] private float commandTimestamp;

    // 캐시
    private Building building;
    private WorkTaskType taskType;

    #region Properties

    // IWorkstation
    public bool IsOccupied => currentWorker != null;
    public Transform WorkPoint => workPoint;
    public Unit CurrentWorker => currentWorker;
    public bool CanStartWork => craftingQueue.Count > 0;
    public WorkTaskType TaskType => taskType;

    // 자체
    public string BuildingType => buildingType;
    public int QueueCount => craftingQueue.Count;
    public bool HasQueueSpace => craftingQueue.Count < maxQueueSize;
    public IReadOnlyList<RecipeSO> AvailableRecipes => availableRecipes;
    public IReadOnlyList<CraftingQueueItem> Queue => craftingQueue;
    public float CommandTimestamp => commandTimestamp;
    public bool HasAssignedUnit => currentWorker != null;
    public bool IsPlayerCommanded => commandTimestamp > 0;

    #endregion

    #region Events

    public event Action<IWorkstation> OnWorkCompleted;
    public event Action<IWorkstation> OnWorkAvailable;
    public event Action OnQueueChanged;
    public event Action<CraftingQueueItem> OnCraftingProgress;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        building = GetComponent<Building>();
        if (workPoint == null && building != null)
            workPoint = building.WorkPoint;

        // DropPoint 찾기 (없으면 WorkPoint 사용)
        if (dropPoint == null)
        {
            dropPoint = transform.Find("DropPoint");
            if (dropPoint == null)
                dropPoint = workPoint;
        }

        taskType = buildingType == "Cooking" ? WorkTaskType.Cooking : WorkTaskType.Crafting;
    }

    private void Start()
    {
        CraftingManager.Instance?.RegisterBuilding(this);
    }

    private void OnDestroy()
    {
        CraftingManager.Instance?.UnregisterBuilding(this);
    }

    #endregion

    #region Queue Management

    public bool AddToQueue(RecipeSO recipe)
    {
        if (recipe == null || !HasQueueSpace) return false;

        if (!recipe.CanCraft())
        {
            Debug.LogWarning($"[CraftingBuilding] 재료 부족: {recipe.RecipeName}");
            return false;
        }

        if (!recipe.ConsumeIngredients())
            return false;

        craftingQueue.Add(new CraftingQueueItem(recipe));
        Debug.Log($"[CraftingBuilding] 대기열 추가: {recipe.RecipeName}");

        OnQueueChanged?.Invoke();
        OnWorkAvailable?.Invoke(this);

        // 작업자가 없으면 Task 등록
        if (currentWorker == null)
            TaskManager.Instance?.AddWorkstationTask(this);

        return true;
    }

    public bool RemoveFromQueue(int index)
    {
        if (index < 0 || index >= craftingQueue.Count) return false;
        if (craftingQueue[index].IsProcessing) return false;

        RefundIngredients(craftingQueue[index].Recipe);
        craftingQueue.RemoveAt(index);
        OnQueueChanged?.Invoke();
        return true;
    }

    private void RefundIngredients(RecipeSO recipe)
    {
        if (recipe?.Ingredients == null) return;
        foreach (var ing in recipe.Ingredients)
        {
            if (ing.Item != null)
                ResourceManager.Instance?.AddResource(ing.Item.ID, ing.Amount);
        }
    }

    #endregion

    #region IWorkstation

    public bool AssignWorker(Unit worker)
    {
        if (worker == null) return false;
        if (currentWorker == worker) return true;
        if (currentWorker != null) ReleaseWorker();

        currentWorker = worker;
        return true;
    }

    public void ReleaseWorker()
    {
        currentWorker = null;
        commandTimestamp = 0f;
    }

    public void StartWork()
    {
        if (craftingQueue.Count == 0) return;
        if (!craftingQueue[0].IsProcessing)
            craftingQueue[0].IsProcessing = true;
    }

    public float DoWork(float workAmount)
    {
        if (craftingQueue.Count == 0) return 0f;

        var item = craftingQueue[0];
        if (!item.IsProcessing) StartWork();

        item.Progress += workAmount / item.Recipe.CraftingTime;
        OnCraftingProgress?.Invoke(item);

        if (item.Progress >= 1f)
            CompleteCrafting(item);

        return workAmount;
    }

    public void CompleteWork()
    {
        if (craftingQueue.Count > 0 && craftingQueue[0].IsProcessing)
            CompleteCrafting(craftingQueue[0]);
    }

    public void CancelWork()
    {
        if (craftingQueue.Count > 0)
            craftingQueue[0].IsProcessing = false;
    }

    private void CompleteCrafting(CraftingQueueItem item)
    {
        if (item?.Recipe == null) return;

        // DroppedItem으로 결과물 드롭
        SpawnDroppedItems(item.Recipe);

        Debug.Log($"[CraftingBuilding] 제작 완료: {item.Recipe.RecipeName}");

        craftingQueue.Remove(item);
        OnQueueChanged?.Invoke();
        OnWorkCompleted?.Invoke(this);

        if (craftingQueue.Count > 0)
            StartWork();
        else
            ReleaseWorker();
    }

    /// <summary>
    /// 제작 결과물을 DroppedItem으로 드롭
    /// </summary>
    private void SpawnDroppedItems(RecipeSO recipe)
    {
        if (recipe.Outputs == null || recipe.Outputs.Length == 0) return;
        if (droppedItemPrefab == null)
        {
            Debug.LogWarning("[CraftingBuilding] DroppedItem 프리팹이 설정되지 않음!");
            // 프리팹 없으면 기존 방식으로 인벤토리에 추가
            recipe.ProduceToInventory();
            return;
        }

        Vector3 spawnPos = dropPoint != null ? dropPoint.position : transform.position;

        foreach (var output in recipe.Outputs)
        {
            if (output.Item == null) continue;

            // 드롭할 수량 계산 (MinAmount ~ MaxAmount 사이 랜덤)
            int amount = UnityEngine.Random.Range(output.MinAmount, output.MaxAmount + 1);
            if (amount <= 0) continue;

            // DroppedItem 생성
            GameObject droppedObj = Instantiate(droppedItemPrefab, spawnPos, Quaternion.identity);
            DroppedItem droppedItem = droppedObj.GetComponent<DroppedItem>();

            if (droppedItem != null)
            {
                droppedItem.Initialize(output.Item, amount);
                droppedItem.PlayDropAnimation(spawnPos);

                Debug.Log($"[CraftingBuilding] 아이템 드롭: {output.Item.ResourceName} x{amount}");
            }
        }
    }

    #endregion

    #region Player Command

    public void AssignByPlayerCommand(Unit unit)
    {
        if (unit == null) return;
        if (currentWorker != null && currentWorker != unit)
            ReleaseWorker();

        currentWorker = unit;
        commandTimestamp = Time.time;
    }

    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (workPoint != null)
        {
            Gizmos.color = buildingType == "Cooking" ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(workPoint.position, 0.4f);
        }
    }
#endif
}