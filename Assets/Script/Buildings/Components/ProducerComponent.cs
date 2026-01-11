using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 생산 건물 컴포넌트
/// - 레시피 기반 아이템 제작
/// - 작업 큐 관리
/// - 작업장, 주방 등에서 사용
/// </summary>
public class ProducerComponent : WorkstationComponent, IProducer, IInteractable
{
    [Header("=== 생산 설정 ===")]
    [SerializeField] private List<RecipeSO> availableRecipes = new List<RecipeSO>();
    [SerializeField] private int maxQueueSize = 5;

    [Header("=== 드롭 설정 ===")]
    [SerializeField] private GameObject droppedItemPrefab;
    [SerializeField] private bool dropOnGround = true;

    [Header("=== 현재 상태 ===")]
    [SerializeField] private RecipeSO currentRecipe;
    [SerializeField] private List<RecipeSO> recipeQueue = new List<RecipeSO>();

    // IProducer 구현
    public RecipeSO CurrentRecipe => currentRecipe;
    public float Progress => workProgress;
    public IReadOnlyList<RecipeSO> RecipeQueue => recipeQueue;
    public IReadOnlyList<RecipeSO> AvailableRecipes => availableRecipes;

    // IInteractable 구현
    public bool CanInteract => true;
    public BuildingUIType UIType => BuildingUIType.Recipe;

    // IProducer 이벤트
    public event Action<IProducer, RecipeSO> OnProductionComplete;
    public event Action<IProducer> OnQueueChanged;

    protected override void Awake()
    {
        base.Awake();
        taskType = WorkTaskType.Crafting;
    }

    // ==================== IProducer 구현 ====================

    public bool AddToQueue(RecipeSO recipe)
    {
        if (recipe == null)
            return false;

        if (recipeQueue.Count >= maxQueueSize)
        {
            Debug.LogWarning($"[Producer] 큐가 가득 참 (최대: {maxQueueSize})");
            return false;
        }

        if (!recipe.CanCraft())
        {
            Debug.LogWarning($"[Producer] 재료 부족: {recipe.RecipeName}");
            return false;
        }

        recipe.ConsumeIngredients();

        recipeQueue.Add(recipe);
        Debug.Log($"[Producer] 레시피 추가: {recipe.RecipeName} (큐: {recipeQueue.Count}/{maxQueueSize})");

        OnQueueChanged?.Invoke(this);

        if (!isWorking && recipeQueue.Count == 1)
        {
            NotifyWorkAvailable();
        }

        return true;
    }

    public bool RemoveFromQueue(int index)
    {
        if (index < 0 || index >= recipeQueue.Count)
            return false;

        var recipe = recipeQueue[index];
        RefundIngredients(recipe);

        recipeQueue.RemoveAt(index);
        OnQueueChanged?.Invoke(this);

        return true;
    }

    public void ClearQueue()
    {
        foreach (var recipe in recipeQueue)
        {
            RefundIngredients(recipe);
        }

        recipeQueue.Clear();
        OnQueueChanged?.Invoke(this);
    }

    // ==================== IInteractable 구현 ====================

    public void Interact()
    {
        BuildingInteractionManager.Instance?.OpenProducerUI(this);
    }

    // ==================== WorkstationComponent 오버라이드 ====================

    protected override bool HasPendingWork()
    {
        return recipeQueue.Count > 0;
    }

    protected override float GetWorkTime()
    {
        if (recipeQueue.Count > 0)
        {
            return recipeQueue[0].CraftingTime;
        }
        return baseWorkTime;
    }

    protected override void OnWorkStarted()
    {
        if (recipeQueue.Count > 0)
        {
            currentRecipe = recipeQueue[0];
            Debug.Log($"[Producer] 제작 시작: {currentRecipe.RecipeName}");
        }
    }

    protected override void OnWorkFinished()
    {
        if (currentRecipe == null)
            return;

        ProduceOutput();

        Debug.Log($"[Producer] 제작 완료: {currentRecipe.RecipeName}");

        OnProductionComplete?.Invoke(this, currentRecipe);

        if (recipeQueue.Count > 0)
        {
            recipeQueue.RemoveAt(0);
            OnQueueChanged?.Invoke(this);
        }

        currentRecipe = null;
    }

    protected override void OnWorkCancelled()
    {
        if (currentRecipe != null)
        {
            RefundIngredients(currentRecipe);
        }

        currentRecipe = null;
    }

    // ==================== 내부 메서드 ====================

    private void ProduceOutput()
    {
        if (currentRecipe == null)
            return;

        if (dropOnGround && droppedItemPrefab != null)
        {
            Vector3 dropPos = building?.DropPoint?.position ?? transform.position;
            currentRecipe.ProduceOutputs(dropPos, droppedItemPrefab);
        }
        else
        {
            currentRecipe.ProduceToInventory();
        }
    }

    private void RefundIngredients(RecipeSO recipe)
    {
        if (recipe == null || ResourceManager.Instance == null)
            return;

        foreach (var ingredient in recipe.Ingredients)
        {
            if (ingredient.Item != null)
            {
                ResourceManager.Instance.AddResource(ingredient.Item.ID, ingredient.Amount);
            }
        }

        Debug.Log($"[Producer] 재료 환불: {recipe.RecipeName}");
    }

    public void SetAvailableRecipes(List<RecipeSO> recipes)
    {
        availableRecipes = recipes ?? new List<RecipeSO>();
    }

    public void AddAvailableRecipe(RecipeSO recipe)
    {
        if (recipe != null && !availableRecipes.Contains(recipe))
        {
            availableRecipes.Add(recipe);
        }
    }

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        Transform dp = GetComponent<Building>()?.DropPoint;
        if (dp != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(dp.position, 0.3f);
        }
    }
#endif
}