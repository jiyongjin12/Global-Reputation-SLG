using System;
using System.Collections.Generic;
using System.Resources;
using UnityEngine;

/// <summary>
/// 유닛 인벤토리
/// 유닛이 운반 중인 아이템 관리
/// </summary>
[Serializable]
public class UnitInventory
{
    [SerializeField] private int maxSlots = 1;        // 최대 슬롯 수
    [SerializeField] private int maxStackPerSlot = 10; // 슬롯당 최대 스택

    private List<InventorySlot> slots = new List<InventorySlot>();

    // 이벤트
    public event Action OnInventoryChanged;

    // Properties
    public int MaxSlots => maxSlots;
    public int UsedSlots => slots.Count;
    public bool IsFull => slots.Count >= maxSlots && slots.TrueForAll(s => s.Amount >= maxStackPerSlot);
    public bool IsEmpty => slots.Count == 0;
    public List<InventorySlot> Slots => slots;

    public void Initialize(int slots = 1, int stackSize = 10)
    {
        maxSlots = slots;
        maxStackPerSlot = stackSize;
        this.slots.Clear();
    }

    /// <summary>
    /// 아이템 추가
    /// </summary>
    /// <returns>실제로 추가된 수량</returns>
    public int AddItem(ResourceItemSO resource, int amount)
    {
        int remaining = amount;

        // 기존 슬롯에 스택 가능한지 확인
        foreach (var slot in slots)
        {
            if (slot.Resource.ID == resource.ID && slot.Amount < maxStackPerSlot)
            {
                int canAdd = Mathf.Min(remaining, maxStackPerSlot - slot.Amount);
                slot.Amount += canAdd;
                remaining -= canAdd;

                if (remaining <= 0) break;
            }
        }

        // 새 슬롯 생성
        while (remaining > 0 && slots.Count < maxSlots)
        {
            int toAdd = Mathf.Min(remaining, maxStackPerSlot);
            slots.Add(new InventorySlot(resource, toAdd));
            remaining -= toAdd;
        }

        int added = amount - remaining;
        if (added > 0)
        {
            OnInventoryChanged?.Invoke();
        }

        return added;
    }

    /// <summary>
    /// 아이템 제거
    /// </summary>
    /// <returns>실제로 제거된 수량</returns>
    public int RemoveItem(ResourceItemSO resource, int amount)
    {
        int remaining = amount;

        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (slots[i].Resource.ID == resource.ID)
            {
                int toRemove = Mathf.Min(remaining, slots[i].Amount);
                slots[i].Amount -= toRemove;
                remaining -= toRemove;

                if (slots[i].Amount <= 0)
                {
                    slots.RemoveAt(i);
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
        int total = 0;
        foreach (var slot in slots)
        {
            if (slot.Resource.ID == resource.ID)
            {
                total += slot.Amount;
            }
        }
        return total;
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
    /// 모든 아이템 드롭 (사망 시 등)
    /// </summary>
    public List<(ResourceItemSO resource, int amount)> DropAllItems()
    {
        var dropped = new List<(ResourceItemSO, int)>();

        foreach (var slot in slots)
        {
            dropped.Add((slot.Resource, slot.Amount));
        }

        slots.Clear();
        OnInventoryChanged?.Invoke();

        return dropped;
    }

    /// <summary>
    /// 창고에 모든 아이템 저장
    /// </summary>
    public void DepositToStorage()
    {
        foreach (var slot in slots)
        {
            ResourceManager.Instance?.AddResource(slot.Resource, slot.Amount);
        }
        slots.Clear();
        OnInventoryChanged?.Invoke();
    }
}

/// <summary>
/// 인벤토리 슬롯
/// </summary>
[Serializable]
public class InventorySlot
{
    public ResourceItemSO Resource;
    public int Amount;

    public InventorySlot(ResourceItemSO resource, int amount)
    {
        Resource = resource;
        Amount = amount;
    }
}