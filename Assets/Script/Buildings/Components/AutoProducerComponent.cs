using UnityEngine;
using System;

/// <summary>
/// 자동 생산 건물 컴포넌트
/// - 작업자가 있으면 일정 시간마다 자원 생산
/// - 채석장, 벌목장, 광산 등
/// </summary>
public class AutoProducerComponent : WorkstationComponent, IAutoProducer, IInteractable
{
    [Header("=== 자동 생산 설정 ===")]
    [SerializeField] private ResourceItemSO producedResource;
    [SerializeField] private int productionAmount = 1;
    [SerializeField] private float productionInterval = 10f;

    [Header("=== 드롭 설정 ===")]
    [SerializeField] private GameObject droppedItemPrefab;

    [Header("=== 상태 ===")]
    [SerializeField] private bool isOperating = false;
    [SerializeField] private float productionTimer = 0f;

    // IAutoProducer 구현
    public float ProductionInterval => productionInterval;
    public float TimeUntilNextProduction => Mathf.Max(0, productionInterval - productionTimer);
    public ResourceItemSO ProducedResource => producedResource;
    public int ProductionAmount => productionAmount;
    public bool IsOperating => isOperating;

    // IInteractable 구현
    public bool CanInteract => true;
    public BuildingUIType UIType => BuildingUIType.Production;

    // 이벤트
    public event Action<IAutoProducer, ResourceItemSO, int> OnAutoProduced;

    protected override void Awake()
    {
        base.Awake();
        taskType = WorkTaskType.Mining;
    }

    private void Update()
    {
        if (isOccupied && isWorking && isOperating)
        {
            UpdateAutoProduction();
        }
    }

    // ==================== 자동 생산 ====================

    private void UpdateAutoProduction()
    {
        productionTimer += Time.deltaTime;

        if (productionTimer >= productionInterval)
        {
            productionTimer = 0f;
            ProduceResource();
        }
    }

    private void ProduceResource()
    {
        if (producedResource == null)
            return;

        if (droppedItemPrefab != null)
        {
            Vector3 dropPos = building?.DropPoint?.position ?? transform.position;

            for (int i = 0; i < productionAmount; i++)
            {
                GameObject itemObj = Instantiate(droppedItemPrefab, dropPos, Quaternion.identity);
                DroppedItem droppedItem = itemObj.GetComponent<DroppedItem>();

                if (droppedItem != null)
                {
                    droppedItem.Initialize(producedResource, 1);
                    droppedItem.PlayDropAnimation(dropPos);
                    droppedItem.BecomePublic();
                }
            }
        }

        Debug.Log($"[AutoProducer] {producedResource.ResourceName} x{productionAmount} 생산됨");

        OnAutoProduced?.Invoke(this, producedResource, productionAmount);
    }

    // ==================== IInteractable 구현 ====================

    public void Interact()
    {
        BuildingInteractionManager.Instance?.OpenProductionUI(this);
    }

    // ==================== WorkstationComponent 오버라이드 ====================

    public override bool CanStartWork => !isOccupied;

    protected override bool HasPendingWork()
    {
        return producedResource != null;
    }

    protected override void OnWorkStarted()
    {
        isOperating = true;
        productionTimer = 0f;

        Debug.Log($"[AutoProducer] {building?.Data?.Name}: 가동 시작");
    }

    protected override void OnWorkFinished()
    {
        // 자동 생산은 CompleteWork가 아닌 지속적 생산
    }

    protected override void OnWorkCancelled()
    {
        isOperating = false;
        productionTimer = 0f;
    }

    public override float DoWork(float workAmount)
    {
        // 자동 생산은 DoWork 대신 Update에서 자동으로 처리
        return 0f;
    }

    public override void ReleaseWorker()
    {
        isOperating = false;
        productionTimer = 0f;
        base.ReleaseWorker();
    }

    // ==================== 외부 접근 ====================

    public void SetProducedResource(ResourceItemSO resource, int amount = 1)
    {
        producedResource = resource;
        productionAmount = amount;
    }

    public void SetProductionInterval(float interval)
    {
        productionInterval = Mathf.Max(0.1f, interval);
    }

    public float GetProductionProgress()
    {
        if (!isOperating || productionInterval <= 0)
            return 0f;

        return productionTimer / productionInterval;
    }

    public string GetStatusString()
    {
        if (!isOccupied)
            return "작업자 필요";

        if (!isOperating)
            return "대기 중";

        float progress = GetProductionProgress() * 100f;
        return $"생산 중: {progress:F0}%";
    }

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        if (isOperating)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position + Vector3.up * 2f, Vector3.one * 0.5f);
        }
    }
#endif
}