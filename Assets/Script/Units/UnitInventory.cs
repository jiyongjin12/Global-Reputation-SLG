using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 유닛 인벤토리
/// 유닛이 운반 중인 아이템 관리
/// - 기본 5칸, 아이템당 최대 5개 스택
/// - 레벨업 시 슬롯 확장 가능
/// </summary>
[Serializable]
public class UnitInventory
{
    [Header("=== 인벤토리 설정 ===")]
    [SerializeField] private int maxSlots = 5;           // 최대 슬롯 수
    [SerializeField] private int maxStackPerSlot = 5;    // 슬롯당 최대 스택
    [SerializeField] private int baseSlots = 5;          // 기본 슬롯 (레벨업 계산용)

    [Header("=== 현재 상태 (읽기 전용) ===")]
    [SerializeField] private List<InventorySlot> slots = new List<InventorySlot>();

    // 이벤트
    public event Action OnInventoryChanged;
    public event Action<int> OnSlotsExpanded;  // 슬롯 확장 시

    // Properties
    public int MaxSlots => maxSlots;
    public int MaxStackPerSlot => maxStackPerSlot;
    public int UsedSlots => GetUsedSlotCount();
    public int FreeSlots => maxSlots - UsedSlots;
    public bool IsFull => CheckIsFull();
    public bool IsEmpty => slots.Count == 0 || slots.TrueForAll(s => s.Amount <= 0);
    public IReadOnlyList<InventorySlot> Slots => slots;

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(int slotCount = 5, int stackSize = 5)
    {
        baseSlots = slotCount;
        maxSlots = slotCount;
        maxStackPerSlot = stackSize;
        slots.Clear();

        // 빈 슬롯 미리 생성 (인스펙터 표시용)
        for (int i = 0; i < maxSlots; i++)
        {
            slots.Add(new InventorySlot());
        }
    }

    /// <summary>
    /// 슬롯 확장 (레벨업 등)
    /// </summary>
    public void ExpandSlots(int additionalSlots)
    {
        int oldMax = maxSlots;
        maxSlots += additionalSlots;

        // 새 빈 슬롯 추가
        for (int i = oldMax; i < maxSlots; i++)
        {
            slots.Add(new InventorySlot());
        }

        OnSlotsExpanded?.Invoke(maxSlots);
        OnInventoryChanged?.Invoke();

        Debug.Log($"[Inventory] 슬롯 확장: {oldMax} → {maxSlots}");
    }

    /// <summary>
    /// 레벨에 따른 슬롯 수 설정
    /// </summary>
    public void SetSlotsByLevel(int level, int slotsPerLevel = 1)
    {
        int targetSlots = baseSlots + (level - 1) * slotsPerLevel;

        if (targetSlots > maxSlots)
        {
            ExpandSlots(targetSlots - maxSlots);
        }
    }

    /// <summary>
    /// 아이템 추가
    /// </summary>
    /// <returns>실제로 추가된 수량</returns>
    public int AddItem(ResourceItemSO resource, int amount)
    {
        if (resource == null || amount <= 0) return 0;

        int remaining = amount;

        // 1. 기존 슬롯에 스택 가능한지 확인 (같은 아이템)
        foreach (var slot in slots)
        {
            if (slot.Resource != null && slot.Resource.ID == resource.ID && slot.Amount < maxStackPerSlot)
            {
                int canAdd = Mathf.Min(remaining, maxStackPerSlot - slot.Amount);
                slot.Amount += canAdd;
                remaining -= canAdd;

                if (remaining <= 0) break;
            }
        }

        // 2. 빈 슬롯에 추가
        if (remaining > 0)
        {
            foreach (var slot in slots)
            {
                if (slot.IsEmpty)
                {
                    int toAdd = Mathf.Min(remaining, maxStackPerSlot);
                    slot.Resource = resource;
                    slot.Amount = toAdd;
                    remaining -= toAdd;

                    if (remaining <= 0) break;
                }
            }
        }

        int added = amount - remaining;
        if (added > 0)
        {
            OnInventoryChanged?.Invoke();
            Debug.Log($"[Inventory] +{added} {resource.ResourceName} (남은 공간 부족: {remaining})");
        }

        return added;
    }

    /// <summary>
    /// 아이템 제거
    /// </summary>
    /// <returns>실제로 제거된 수량</returns>
    public int RemoveItem(ResourceItemSO resource, int amount)
    {
        if (resource == null || amount <= 0) return 0;

        int remaining = amount;

        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (slots[i].Resource != null && slots[i].Resource.ID == resource.ID)
            {
                int toRemove = Mathf.Min(remaining, slots[i].Amount);
                slots[i].Amount -= toRemove;
                remaining -= toRemove;

                // 슬롯 비우기 (삭제하지 않고 빈 상태로)
                if (slots[i].Amount <= 0)
                {
                    slots[i].Clear();
                }
            }
        }

        int removed = amount - remaining;
        if (removed > 0)
        {
            OnInventoryChanged?.Invoke();
        }

        return removed;
    }

    /// <summary>
    /// 특정 자원 보유량 확인
    /// </summary>
    public int GetItemCount(ResourceItemSO resource)
    {
        if (resource == null) return 0;

        int total = 0;
        foreach (var slot in slots)
        {
            if (slot.Resource != null && slot.Resource.ID == resource.ID)
            {
                total += slot.Amount;
            }
        }
        return total;
    }

    /// <summary>
    /// 특정 아이템 보유 여부
    /// </summary>
    public bool HasItem(ResourceItemSO resource, int amount = 1)
    {
        return GetItemCount(resource) >= amount;
    }

    /// <summary>
    /// 전체 아이템 개수
    /// </summary>
    public int GetTotalItemCount()
    {
        int total = 0;
        foreach (var slot in slots)
        {
            total += slot.Amount;
        }
        return total;
    }

    /// <summary>
    /// 사용 중인 슬롯 수
    /// </summary>
    private int GetUsedSlotCount()
    {
        int count = 0;
        foreach (var slot in slots)
        {
            if (!slot.IsEmpty) count++;
        }
        return count;
    }

    /// <summary>
    /// 인벤토리가 꽉 찼는지 확인
    /// </summary>
    private bool CheckIsFull()
    {
        foreach (var slot in slots)
        {
            // 빈 슬롯이 있으면 not full
            if (slot.IsEmpty) return false;

            // 스택 가능한 공간이 있으면 not full
            if (slot.Amount < maxStackPerSlot) return false;
        }
        return true;
    }

    /// <summary>
    /// 특정 아이템을 추가할 수 있는지 확인
    /// </summary>
    public bool CanAddItem(ResourceItemSO resource, int amount = 1)
    {
        if (resource == null) return false;

        int canAdd = 0;

        // 기존 슬롯에서 스택 가능한 양
        foreach (var slot in slots)
        {
            if (slot.Resource != null && slot.Resource.ID == resource.ID)
            {
                canAdd += maxStackPerSlot - slot.Amount;
            }
            else if (slot.IsEmpty)
            {
                canAdd += maxStackPerSlot;
            }

            if (canAdd >= amount) return true;
        }

        return canAdd >= amount;
    }

    /// <summary>
    /// 특정 아이템을 위한 남은 공간 계산
    /// </summary>
    public int GetAvailableSpaceFor(ResourceItemSO resource)
    {
        if (resource == null) return 0;

        int availableSpace = 0;

        foreach (var slot in slots)
        {
            if (slot.Resource != null && slot.Resource.ID == resource.ID)
            {
                // 같은 아이템 슬롯에서 스택 가능한 양
                availableSpace += maxStackPerSlot - slot.Amount;
            }
            else if (slot.IsEmpty)
            {
                // 빈 슬롯
                availableSpace += maxStackPerSlot;
            }
        }

        return availableSpace;
    }

    /// <summary>
    /// 아이템을 1개라도 추가할 수 있는지 확인
    /// </summary>
    public bool CanAddAny(ResourceItemSO resource)
    {
        return GetAvailableSpaceFor(resource) > 0;
    }

    /// <summary>
    /// 모든 아이템 드롭 (사망 시 등)
    /// </summary>
    public List<(ResourceItemSO resource, int amount)> DropAllItems()
    {
        var dropped = new List<(ResourceItemSO, int)>();

        foreach (var slot in slots)
        {
            if (!slot.IsEmpty)
            {
                dropped.Add((slot.Resource, slot.Amount));
                slot.Clear();
            }
        }

        OnInventoryChanged?.Invoke();

        return dropped;
    }

    /// <summary>
    /// 인벤토리 비우기 (드롭 없이)
    /// </summary>
    public void Clear()
    {
        foreach (var slot in slots)
        {
            slot.Clear();
        }
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// 창고에 모든 아이템 저장
    /// </summary>
    public void DepositToStorage()
    {
        foreach (var slot in slots)
        {
            if (!slot.IsEmpty)
            {
                ResourceManager.Instance?.AddResource(slot.Resource, slot.Amount);
                Debug.Log($"[Inventory] 저장: {slot.Resource.ResourceName} x{slot.Amount}");
                slot.Clear();
            }
        }
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// 인벤토리 상태 문자열 (디버그용)
    /// </summary>
    public string GetStatusString()
    {
        return $"슬롯: {UsedSlots}/{maxSlots}, 아이템: {GetTotalItemCount()}개";
    }
}

/// <summary>
/// 인벤토리 슬롯
/// </summary>
[Serializable]
public class InventorySlot
{
    [SerializeField] private ResourceItemSO resource;
    [SerializeField] private int amount;

    public ResourceItemSO Resource
    {
        get => resource;
        set => resource = value;
    }

    public int Amount
    {
        get => amount;
        set => amount = value;
    }

    public bool IsEmpty => resource == null || amount <= 0;

    public InventorySlot()
    {
        resource = null;
        amount = 0;
    }

    public InventorySlot(ResourceItemSO res, int amt)
    {
        resource = res;
        amount = amt;
    }

    public void Clear()
    {
        resource = null;
        amount = 0;
    }

    public override string ToString()
    {
        if (IsEmpty) return "[빈 슬롯]";
        return $"{resource.ResourceName} x{amount}";
    }
}