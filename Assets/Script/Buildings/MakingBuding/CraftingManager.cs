using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 제작 건물 전체 관리
/// </summary>
public class CraftingManager : MonoBehaviour
{
    public static CraftingManager Instance { get; private set; }

    private readonly List<CraftingBuildingComponent> cookingBuildings = new();
    private readonly List<CraftingBuildingComponent> craftingBuildings = new();

    public event Action<CraftingBuildingComponent> OnBuildingRegistered;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    #region Registration

    public void RegisterBuilding(CraftingBuildingComponent building)
    {
        if (building == null) return;

        var list = GetList(building.BuildingType);
        if (list.Contains(building)) return;

        list.Add(building);
        OnBuildingRegistered?.Invoke(building);
    }

    public void UnregisterBuilding(CraftingBuildingComponent building)
    {
        if (building == null) return;
        cookingBuildings.Remove(building);
        craftingBuildings.Remove(building);
    }

    private List<CraftingBuildingComponent> GetList(string type)
        => type == "Cooking" ? cookingBuildings : craftingBuildings;

    #endregion

    #region Find Buildings

    /// <summary>
    /// 자율 AI용: 대기열 있고 작업자 없는 건물
    /// </summary>
    public CraftingBuildingComponent GetAvailableBuilding(string buildingType)
    {
        return GetList(buildingType)
            .Where(b => b.QueueCount > 0 && !b.HasAssignedUnit)
            .OrderByDescending(b => b.QueueCount)
            .FirstOrDefault();
    }

    /// <summary>
    /// 플레이어 명령으로 유닛 배치
    /// </summary>
    public CraftingBuildingComponent AssignUnitByPlayerCommand(Unit unit, string buildingType)
    {
        if (unit == null) return null;

        var withQueue = GetList(buildingType).Where(b => b.QueueCount > 0).ToList();
        if (withQueue.Count == 0) return null;

        // 1순위: 작업자 없는 건물
        var empty = withQueue
            .Where(b => !b.HasAssignedUnit)
            .OrderByDescending(b => b.QueueCount)
            .FirstOrDefault();

        if (empty != null)
        {
            empty.AssignByPlayerCommand(unit);
            return empty;
        }

        // 2순위: 가장 오래된 명령 건물 (밀어내기)
        var oldest = withQueue.OrderBy(b => b.CommandTimestamp).First();
        oldest.AssignByPlayerCommand(unit);
        return oldest;
    }

    /// <summary>
    /// 가장 가까운 작업 가능 건물 (자율 AI용)
    /// </summary>
    public CraftingBuildingComponent GetNearestAvailable(Vector3 pos, string buildingType)
    {
        return GetList(buildingType)
            .Where(b => b.QueueCount > 0 && !b.IsPlayerCommanded)
            .OrderBy(b => Vector3.Distance(pos, b.transform.position))
            .FirstOrDefault();
    }

    #endregion

    #region Getters

    public IReadOnlyList<CraftingBuildingComponent> GetCookingBuildings() => cookingBuildings;
    public IReadOnlyList<CraftingBuildingComponent> GetCraftingBuildings() => craftingBuildings;

    #endregion
}