using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 제작/요리 건물 상호작용
/// - 클릭 시 CraftingUI 열기
/// </summary>
public class CraftingBuildingInteraction : MonoBehaviour, IPointerClickHandler
{
    [Header("=== References ===")]
    [SerializeField] private CraftingBuildingComponent craftingBuilding;
    [SerializeField] private Building building;

    [Header("=== 설정 ===")]
    [SerializeField] private bool requireCompleted = true;

    private void Awake()
    {
        if (craftingBuilding == null)
            craftingBuilding = GetComponent<CraftingBuildingComponent>();

        if (building == null)
            building = GetComponent<Building>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 건설 완료 체크
        if (requireCompleted && building != null)
        {
            if (building.CurrentState != BuildingState.Completed)
            {
                Debug.Log("[CraftingBuildingInteraction] 건물이 아직 완성되지 않았습니다.");
                return;
            }
        }

        // UI 열기
        if (CraftingUI.Instance != null && craftingBuilding != null)
        {
            CraftingUI.Instance.Toggle(craftingBuilding);
        }
    }

    /// <summary>
    /// 외부에서 UI 열기
    /// </summary>
    public void OpenUI()
    {
        if (CraftingUI.Instance != null && craftingBuilding != null)
        {
            CraftingUI.Instance.Open(craftingBuilding);
        }
    }
}