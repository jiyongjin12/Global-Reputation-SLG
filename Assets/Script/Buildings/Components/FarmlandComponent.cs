using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 농경지 컴포넌트
/// - 씨앗 심기 → 성장 → 수확 사이클
/// - Unit이 심고/수확하러 옴
/// </summary>
public class FarmlandComponent : WorkstationComponent, IHarvestable, IInteractable
{
    [Header("=== 농경 설정 ===")]
    [SerializeField] private float growthTime = 60f;
    [SerializeField] private float harvestWorkTime = 3f;
    [SerializeField] private float plantWorkTime = 2f;
    [SerializeField] private int minHarvestAmount = 2;
    [SerializeField] private int maxHarvestAmount = 5;

    [Header("=== 드롭 설정 ===")]
    [SerializeField] private GameObject droppedItemPrefab;

    [Header("=== 작물 비주얼 ===")]
    [SerializeField] private Transform cropVisualParent;
    [SerializeField] private GameObject[] growthStagePrefabs;

    [Header("=== 현재 상태 ===")]
    [SerializeField] private ResourceItemSO currentCrop;
    [SerializeField] private float growthProgress = 0f;
    [SerializeField] private bool isReadyToHarvest = false;
    [SerializeField] private FarmState farmState = FarmState.Empty;

    // 내부 상태
    private GameObject currentCropVisual;
    private ResourceItemSO pendingCrop;

    // IHarvestable 구현
    public bool IsReadyToHarvest => isReadyToHarvest;
    public float GrowthProgress => growthProgress;
    public ResourceItemSO CurrentCrop => currentCrop;

    // IInteractable 구현
    public bool CanInteract => true;
    public BuildingUIType UIType => BuildingUIType.Farming;

    // 이벤트
    public event Action<IHarvestable> OnReadyToHarvest;
    public event Action<IHarvestable> OnPlanted;
    public event Action<FarmlandComponent> OnHarvested;
    public event Action<FarmlandComponent> OnStateChanged;

    public FarmState State => farmState;

    protected override void Awake()
    {
        base.Awake();
        taskType = WorkTaskType.Farming;
    }

    private void Update()
    {
        if (farmState == FarmState.Growing && currentCrop != null)
        {
            UpdateGrowth();
        }
    }

    // ==================== IHarvestable 구현 ====================

    public bool Plant(ResourceItemSO seed)
    {
        if (seed == null)
            return false;

        if (farmState != FarmState.Empty)
        {
            Debug.LogWarning("[Farmland] 이미 작물이 심어져 있습니다.");
            return false;
        }

        if (ResourceManager.Instance != null && !ResourceManager.Instance.HasEnoughResource(seed.ID, 1))
        {
            Debug.LogWarning($"[Farmland] 씨앗 부족: {seed.ResourceName}");
            return false;
        }

        ResourceManager.Instance?.UseResource(seed.ID, 1);

        pendingCrop = seed;
        farmState = FarmState.WaitingForPlant;

        Debug.Log($"[Farmland] 심기 대기: {seed.ResourceName}");

        OnStateChanged?.Invoke(this);
        NotifyWorkAvailable();

        return true;
    }

    public List<ResourceItemSO> Harvest()
    {
        if (!isReadyToHarvest || currentCrop == null)
            return new List<ResourceItemSO>();

        var harvested = new List<ResourceItemSO> { currentCrop };

        int harvestAmount = UnityEngine.Random.Range(minHarvestAmount, maxHarvestAmount + 1);

        if (droppedItemPrefab != null)
        {
            Vector3 dropPos = building?.DropPoint?.position ?? transform.position;

            for (int i = 0; i < harvestAmount; i++)
            {
                GameObject itemObj = Instantiate(droppedItemPrefab, dropPos, Quaternion.identity);
                DroppedItem droppedItem = itemObj.GetComponent<DroppedItem>();

                if (droppedItem != null)
                {
                    droppedItem.Initialize(currentCrop, 1);
                    droppedItem.PlayDropAnimation(dropPos);
                }
            }
        }

        Debug.Log($"[Farmland] 수확 완료: {currentCrop.ResourceName} x{harvestAmount}");

        ResetFarmland();

        OnHarvested?.Invoke(this);

        return harvested;
    }

    // ==================== IInteractable 구현 ====================

    public void Interact()
    {
        BuildingInteractionManager.Instance?.OpenFarmingUI(this);
    }

    // ==================== WorkstationComponent 오버라이드 ====================

    public override bool CanStartWork => !isOccupied && HasPendingWork();

    protected override bool HasPendingWork()
    {
        return farmState == FarmState.WaitingForPlant || farmState == FarmState.ReadyToHarvest;
    }

    protected override float GetWorkTime()
    {
        return farmState == FarmState.WaitingForPlant ? plantWorkTime : harvestWorkTime;
    }

    protected override void OnWorkStarted()
    {
        if (farmState == FarmState.WaitingForPlant)
        {
            Debug.Log($"[Farmland] 심기 시작: {pendingCrop?.ResourceName}");
        }
        else if (farmState == FarmState.ReadyToHarvest)
        {
            Debug.Log($"[Farmland] 수확 시작: {currentCrop?.ResourceName}");
        }
    }

    protected override void OnWorkFinished()
    {
        if (farmState == FarmState.WaitingForPlant && pendingCrop != null)
        {
            currentCrop = pendingCrop;
            pendingCrop = null;
            growthProgress = 0f;
            farmState = FarmState.Growing;

            UpdateCropVisual();

            Debug.Log($"[Farmland] 심기 완료: {currentCrop.ResourceName}");

            OnPlanted?.Invoke(this);
        }
        else if (farmState == FarmState.ReadyToHarvest)
        {
            Harvest();
        }

        OnStateChanged?.Invoke(this);
    }

    // ==================== 성장 처리 ====================

    private void UpdateGrowth()
    {
        if (growthTime <= 0)
        {
            growthProgress = 1f;
        }
        else
        {
            growthProgress += Time.deltaTime / growthTime;
        }

        UpdateCropVisual();

        if (growthProgress >= 1f)
        {
            growthProgress = 1f;
            isReadyToHarvest = true;
            farmState = FarmState.ReadyToHarvest;

            Debug.Log($"[Farmland] 수확 가능: {currentCrop.ResourceName}");

            OnReadyToHarvest?.Invoke(this);
            OnStateChanged?.Invoke(this);
            NotifyWorkAvailable();
        }
    }

    private void UpdateCropVisual()
    {
        if (cropVisualParent == null || growthStagePrefabs == null || growthStagePrefabs.Length == 0)
            return;

        if (currentCropVisual != null)
        {
            Destroy(currentCropVisual);
        }

        if (currentCrop == null || farmState == FarmState.Empty)
            return;

        int stage = Mathf.FloorToInt(growthProgress * (growthStagePrefabs.Length - 1));
        stage = Mathf.Clamp(stage, 0, growthStagePrefabs.Length - 1);

        if (growthStagePrefabs[stage] != null)
        {
            currentCropVisual = Instantiate(growthStagePrefabs[stage], cropVisualParent);
            currentCropVisual.transform.localPosition = Vector3.zero;
        }
    }

    private void ResetFarmland()
    {
        currentCrop = null;
        pendingCrop = null;
        growthProgress = 0f;
        isReadyToHarvest = false;
        farmState = FarmState.Empty;

        if (currentCropVisual != null)
        {
            Destroy(currentCropVisual);
            currentCropVisual = null;
        }

        OnStateChanged?.Invoke(this);
    }

    // ==================== 외부 접근 ====================

    public void RequestHarvest()
    {
        if (farmState != FarmState.ReadyToHarvest)
        {
            Debug.LogWarning("[Farmland] 수확할 수 없는 상태입니다.");
            return;
        }

        NotifyWorkAvailable();
    }

    public string GetStateString()
    {
        return farmState switch
        {
            FarmState.Empty => "비어있음",
            FarmState.WaitingForPlant => $"심기 대기: {pendingCrop?.ResourceName}",
            FarmState.Growing => $"성장 중: {Mathf.FloorToInt(growthProgress * 100)}%",
            FarmState.ReadyToHarvest => "수확 가능!",
            _ => "알 수 없음"
        };
    }

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        Gizmos.color = farmState switch
        {
            FarmState.Empty => Color.gray,
            FarmState.WaitingForPlant => Color.cyan,
            FarmState.Growing => Color.green,
            FarmState.ReadyToHarvest => Color.yellow,
            _ => Color.white
        };

        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, new Vector3(1, 0.1f, 1));
    }
#endif
}

public enum FarmState
{
    Empty,
    WaitingForPlant,
    Growing,
    ReadyToHarvest
}