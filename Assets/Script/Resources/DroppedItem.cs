using System;
using System.Resources;
using UnityEngine;

/// <summary>
/// 바닥에 드롭된 아이템
/// 유닛이 주워서 창고로 운반해야 함
/// </summary>
public class DroppedItem : MonoBehaviour
{
    [SerializeField] private ResourceItemSO resource;
    [SerializeField] private int amount = 1;

    private bool isBeingCarried = false;
    private bool isReserved = false; // 유닛이 가져가려고 예약함
    private Unit reservedBy = null;

    // Properties
    public ResourceItemSO Resource => resource;
    public int Amount => amount;
    public bool IsAvailable => !isBeingCarried && !isReserved;
    public bool IsReserved => isReserved;
    public Unit ReservedBy => reservedBy;

    // 이벤트
    public event Action<DroppedItem> OnPickedUp;

    public void Initialize(ResourceItemSO resourceItem, int itemAmount)
    {
        resource = resourceItem;
        amount = itemAmount;
    }

    /// <summary>
    /// 유닛이 이 아이템을 가져가겠다고 예약
    /// </summary>
    public bool Reserve(Unit unit)
    {
        if (!IsAvailable)
            return false;

        isReserved = true;
        reservedBy = unit;
        return true;
    }

    /// <summary>
    /// 예약 취소
    /// </summary>
    public void CancelReservation()
    {
        isReserved = false;
        reservedBy = null;
    }

    /// <summary>
    /// 유닛이 아이템 줍기
    /// </summary>
    public bool PickUp(Unit unit)
    {
        if (isBeingCarried)
            return false;

        // 예약한 유닛이 아니면 실패 (다른 유닛이 예약했을 경우)
        if (isReserved && reservedBy != unit)
            return false;

        isBeingCarried = true;
        OnPickedUp?.Invoke(this);

        Debug.Log($"[DroppedItem] {resource.ResourceName} x{amount} picked up by {unit.name}");

        // 아이템 제거 (유닛 인벤토리로 이동됨)
        Destroy(gameObject);
        return true;
    }

    /// <summary>
    /// 일정 시간 후 자동 수거 (옵션)
    /// </summary>
    public void AutoCollectAfter(float delay)
    {
        Invoke(nameof(AutoCollect), delay);
    }

    private void AutoCollect()
    {
        if (!isBeingCarried && !isReserved)
        {
            // 직접 창고에 추가
            ResourceManager.Instance?.AddResource(resource, amount);
            Debug.Log($"[DroppedItem] {resource.ResourceName} x{amount} auto-collected");
            Destroy(gameObject);
        }
    }
}