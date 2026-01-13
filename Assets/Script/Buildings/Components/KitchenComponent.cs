using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 주방 컴포넌트
/// - 음식 레시피 제작
/// - 완성된 음식은 바닥에 드롭
/// - 굶주린 Unit이 우선적으로 가져감
/// </summary>
public class KitchenComponent : ProducerComponent
{
    [Header("=== 주방 설정 ===")]
    [Tooltip("음식 우선순위: 굶주린 Unit에게 알림")]
    [SerializeField] private bool notifyHungryUnits = true;
    [SerializeField] private float foodNotifyRadius = 50f;

    // 이벤트
    public event Action<KitchenComponent, ResourceItemSO> OnFoodProduced;

    protected override void Awake()
    {
        base.Awake();
        taskType = WorkTaskType.Cooking;
    }

    protected override void OnWorkFinished()
    {
        if (CurrentRecipe == null)
        {
            base.OnWorkFinished();
            return;
        }

        bool isFood = false;
        foreach (var output in CurrentRecipe.Outputs)
        {
            if (output.Item != null && output.Item.IsFood)
            {
                isFood = true;
                break;
            }
        }

        var recipe = CurrentRecipe;
        base.OnWorkFinished();

        if (isFood && notifyHungryUnits)
        {
            NotifyHungryUnits(recipe);
        }
    }

    private void NotifyHungryUnits(RecipeSO recipe)
    {
        var colliders = Physics.OverlapSphere(transform.position, foodNotifyRadius);

        List<Unit> hungryUnits = new List<Unit>();

        foreach (var col in colliders)
        {
            Unit unit = col.GetComponent<Unit>();
            if (unit == null || !unit.IsAlive) continue;

            var bb = unit.Blackboard;
            if (bb != null && bb.Hunger <= 50f)
            {
                hungryUnits.Add(unit);
            }
        }

        if (hungryUnits.Count > 0)
        {
            hungryUnits = hungryUnits.OrderBy(u => u.Blackboard?.Hunger ?? 100f).ToList();

            Unit mostHungry = hungryUnits[0];
            var ai = mostHungry.GetComponent<UnitAI>();  // ★ 수정: UnitAi → UnitAI
            ai?.SetFoodTarget(building.DropPoint.position);

            Debug.Log($"[Kitchen] 음식 완성! 굶주린 {mostHungry.UnitName}에게 알림");
        }

        foreach (var output in recipe.Outputs)
        {
            if (output.Item != null && output.Item.IsFood)
            {
                OnFoodProduced?.Invoke(this, output.Item);
            }
        }
    }

    public List<RecipeSO> GetFoodRecipes()
    {
        return AvailableRecipes
            .Where(r => r.Category == RecipeCategory.Cooking)
            .ToList();
    }

    public List<RecipeSO> GetCraftableFoodRecipes()
    {
        return GetFoodRecipes()
            .Where(r => r.CanCraft())
            .ToList();
    }

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        if (notifyHungryUnits)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, foodNotifyRadius);
        }
    }
#endif
}