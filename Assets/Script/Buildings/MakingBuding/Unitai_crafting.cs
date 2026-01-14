using UnityEngine;

/// <summary>
/// 유닛 AI - 제작/요리 명령 처리
/// </summary>
public partial class UnitAI
{
    /// <summary>
    /// 제작 건물로 이동하여 작업 (플레이어 명령)
    /// </summary>
    public void AssignToCraftingBuilding(CraftingBuildingComponent building)
    {
        if (building == null) return;

        InterruptCurrentTask();
        ClearDeliveryState();
        bb.ClearPersistentCommand();

        building.AssignByPlayerCommand(unit);

        currentWorkstation = building;
        isWorkstationWorkStarted = false;

        Vector3 workPos = building.WorkPoint?.position ?? building.transform.position;
        unit.MoveTo(workPos);

        SetBehaviorAndPriority(AIBehaviorState.WorkingAtStation, TaskPriorityLevel.PlayerCommand);

        Debug.Log($"[UnitAI] {unit.UnitName}: 제작 건물 배정 - {building.name}");
    }

    /// <summary>
    /// 요리 명령 (UnitCommandUI에서 호출)
    /// </summary>
    public void ExecuteCookingCommand()
    {
        var building = CraftingManager.Instance?.AssignUnitByPlayerCommand(unit, "Cooking");
        if (building != null)
            AssignToCraftingBuilding(building);
        else
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 작업할 요리 건물 없음");
    }

    /// <summary>
    /// 제작 명령 (UnitCommandUI에서 호출)
    /// </summary>
    public void ExecuteCraftCommand()
    {
        var building = CraftingManager.Instance?.AssignUnitByPlayerCommand(unit, "Crafting");
        if (building != null)
            AssignToCraftingBuilding(building);
        else
            Debug.LogWarning($"[UnitAI] {unit.UnitName}: 작업할 제작 건물 없음");
    }
}