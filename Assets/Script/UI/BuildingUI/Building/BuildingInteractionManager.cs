using UnityEngine;
using System;

/// <summary>
/// 건물 상호작용 관리자
/// - 건물 클릭 시 적절한 UI 열기
/// </summary>
public class BuildingInteractionManager : MonoBehaviour
{
    public static BuildingInteractionManager Instance { get; private set; }

    [Header("=== UI 참조 (선택적) ===")]
    [SerializeField] private GameObject storageUI;
    [SerializeField] private GameObject farmingUI;
    [SerializeField] private GameObject buildingInfoUI;

    [Header("=== 현재 상태 ===")]
    [SerializeField] private Building currentBuilding;
    [SerializeField] private bool isUIOpen = false;

    // 이벤트
    public event Action<Building> OnBuildingSelected;
    public event Action OnBuildingDeselected;
    public event Action<CraftingBuildingComponent> OnCraftingUIRequested;
    public event Action<IStorage> OnStorageUIRequested;
    public event Action<FarmlandComponent> OnFarmingUIRequested;

    public Building CurrentBuilding => currentBuilding;
    public bool IsUIOpen => isUIOpen;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(this);
            return;
        }
    }

    /// <summary>건물 선택</summary>
    public void SelectBuilding(Building building)
    {
        if (building == null)
            return;

        if (isUIOpen)
        {
            CloseCurrentUI();
        }

        currentBuilding = building;

        if (building.Interactable != null)
        {
            building.Interactable.Interact();
        }
        else
        {
            OpenBuildingInfoUI(building);
        }

        OnBuildingSelected?.Invoke(building);
    }

    /// <summary>선택 해제</summary>
    public void DeselectBuilding()
    {
        CloseCurrentUI();
        currentBuilding = null;
        OnBuildingDeselected?.Invoke();
    }

    // ==================== UI 열기 ====================

    /// <summary>제작/요리 UI 열기</summary>
    public void OpenCraftingUI(CraftingBuildingComponent craftingBuilding)
    {
        if (craftingBuilding == null) return;

        isUIOpen = true;
        OnCraftingUIRequested?.Invoke(craftingBuilding);

        // CraftingUI 사용
        CraftingUI.Instance?.Open(craftingBuilding);

        Debug.Log($"[BuildingInteraction] Crafting UI 열림 (타입: {craftingBuilding.BuildingType})");
    }

    public void OpenStorageUI(IStorage storage)
    {
        isUIOpen = true;
        OnStorageUIRequested?.Invoke(storage);

        if (storageUI != null)
        {
            storageUI.SetActive(true);
        }

        Debug.Log($"[BuildingInteraction] Storage UI 열림 (아이템 {storage.StoredItems?.Count ?? 0}종류)");
    }

    public void OpenFarmingUI(FarmlandComponent farmland)
    {
        isUIOpen = true;
        OnFarmingUIRequested?.Invoke(farmland);

        if (farmingUI != null)
        {
            farmingUI.SetActive(true);
        }

        Debug.Log($"[BuildingInteraction] Farming UI 열림 (상태: {farmland.GetStateString()})");
    }

    public void OpenBuildingInfoUI(Building building)
    {
        isUIOpen = true;

        if (buildingInfoUI != null)
        {
            buildingInfoUI.SetActive(true);
        }

        Debug.Log($"[BuildingInteraction] Info UI 열림: {building.Data?.Name}");
    }

    /// <summary>현재 UI 닫기</summary>
    public void CloseCurrentUI()
    {
        if (storageUI != null) storageUI.SetActive(false);
        if (farmingUI != null) farmingUI.SetActive(false);
        if (buildingInfoUI != null) buildingInfoUI.SetActive(false);

        // CraftingUI도 닫기
        CraftingUI.Instance?.Close();

        isUIOpen = false;
    }
}