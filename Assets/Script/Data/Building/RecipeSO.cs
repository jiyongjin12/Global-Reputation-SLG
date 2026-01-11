using UnityEngine;
using System;

/// <summary>
/// 레시피 ScriptableObject
/// - 제작에 필요한 재료와 결과물 정의
/// - 작업장, 주방 등에서 사용
/// </summary>
[CreateAssetMenu(fileName = "New Recipe", menuName = "Game/Recipe")]
public class RecipeSO : ScriptableObject
{
    [Header("=== 기본 정보 ===")]
    [SerializeField] private int id;
    [SerializeField] private string recipeName;
    [SerializeField, TextArea(2, 4)] private string description;
    [SerializeField] private Sprite icon;

    [Header("=== 제작 조건 ===")]
    [SerializeField] private RecipeIngredient[] ingredients;
    [SerializeField] private float craftingTime = 5f;
    [SerializeField] private WorkTaskType requiredTaskType = WorkTaskType.Crafting;

    [Header("=== 결과물 ===")]
    [SerializeField] private RecipeOutput[] outputs;

    [Header("=== 분류 ===")]
    [SerializeField] private RecipeCategory category = RecipeCategory.Crafting;
    [SerializeField] private int sortOrder = 0;

    // Properties
    public int ID => id;
    public string RecipeName => recipeName;
    public string Description => description;
    public Sprite Icon => icon;
    public RecipeIngredient[] Ingredients => ingredients;
    public float CraftingTime => craftingTime;
    public WorkTaskType RequiredTaskType => requiredTaskType;
    public RecipeOutput[] Outputs => outputs;
    public RecipeCategory Category => category;
    public int SortOrder => sortOrder;

    /// <summary>재료가 충분한지 확인 (ResourceManager 기준)</summary>
    public bool CanCraft()
    {
        if (ResourceManager.Instance == null) return false;

        foreach (var ingredient in ingredients)
        {
            if (ingredient.Item == null) continue;
            if (!ResourceManager.Instance.HasEnoughResource(ingredient.Item.ID, ingredient.Amount))
                return false;
        }
        return true;
    }

    /// <summary>특정 저장소 기준으로 재료 확인</summary>
    public bool CanCraftFrom(IStorage storage)
    {
        if (storage == null) return false;

        foreach (var ingredient in ingredients)
        {
            if (ingredient.Item == null) continue;
            if (!storage.HasItem(ingredient.Item, ingredient.Amount))
                return false;
        }
        return true;
    }

    /// <summary>재료 소비 (ResourceManager)</summary>
    public bool ConsumeIngredients()
    {
        if (!CanCraft()) return false;

        foreach (var ingredient in ingredients)
        {
            if (ingredient.Item == null) continue;
            ResourceManager.Instance.UseResource(ingredient.Item.ID, ingredient.Amount);
        }
        return true;
    }

    /// <summary>특정 저장소에서 재료 소비</summary>
    public bool ConsumeIngredientsFrom(IStorage storage)
    {
        if (!CanCraftFrom(storage)) return false;

        foreach (var ingredient in ingredients)
        {
            if (ingredient.Item == null) continue;
            storage.RemoveItem(ingredient.Item, ingredient.Amount);
        }
        return true;
    }

    /// <summary>결과물 생성 (드롭 아이템으로)</summary>
    public void ProduceOutputs(Vector3 dropPosition, GameObject droppedItemPrefab)
    {
        if (droppedItemPrefab == null) return;

        foreach (var output in outputs)
        {
            if (output.Item == null) continue;

            int amount = output.GetAmount();

            for (int i = 0; i < amount; i++)
            {
                GameObject itemObj = Instantiate(droppedItemPrefab, dropPosition, Quaternion.identity);
                DroppedItem droppedItem = itemObj.GetComponent<DroppedItem>();

                if (droppedItem != null)
                {
                    droppedItem.Initialize(output.Item, 1);
                    droppedItem.PlayDropAnimation(dropPosition);
                }
            }
        }
    }

    /// <summary>결과물 직접 인벤토리에 추가</summary>
    public void ProduceToInventory()
    {
        if (ResourceManager.Instance == null) return;

        foreach (var output in outputs)
        {
            if (output.Item == null) continue;
            int amount = output.GetAmount();
            ResourceManager.Instance.AddResource(output.Item.ID, amount);
        }
    }
}

/// <summary>
/// 레시피 재료
/// </summary>
[Serializable]
public class RecipeIngredient
{
    public ResourceItemSO Item;
    public int Amount = 1;
}

/// <summary>
/// 레시피 결과물
/// </summary>
[Serializable]
public class RecipeOutput
{
    public ResourceItemSO Item;
    public int MinAmount = 1;
    public int MaxAmount = 1;

    public int GetAmount()
    {
        return UnityEngine.Random.Range(MinAmount, MaxAmount + 1);
    }
}

/// <summary>
/// 레시피 분류
/// </summary>
public enum RecipeCategory
{
    Crafting,
    Cooking,
    Smelting,
    Alchemy,
    Building,
}