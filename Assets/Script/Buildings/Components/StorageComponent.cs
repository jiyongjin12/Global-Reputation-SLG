using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 저장소 건물 컴포넌트
/// - 아이템 보관
/// - 메인 저장소 (ResourceManager 연동)
/// - Unit이 아이템을 가져가거나 넣을 수 있음
/// - 범위 설정 가능 (Unit이 접근할 수 있는 영역)
/// </summary>
public class StorageComponent : MonoBehaviour, IStorage, IInteractable
{
    [Header("=== 저장소 설정 ===")]
    [Tooltip("-1이면 무제한")]
    [SerializeField] private int maxCapacity = -1;

    [Tooltip("메인 저장소 여부 (ResourceManager와 연동)")]
    [SerializeField] private bool isMainStorage = false;

    [Header("=== 접근 범위 설정 ===")]
    [Tooltip("저장소 접근 가능 영역 중심 (로컬 좌표)")]
    [SerializeField] private Vector3 accessAreaCenter = Vector3.zero;

    [Tooltip("저장소 접근 가능 영역 크기")]
    [SerializeField] private Vector3 accessAreaSize = new Vector3(2f, 2f, 2f);

    [Tooltip("Unit이 접근해야 하는 최소 거리")]
    [SerializeField] private float accessDistance = 1.5f;

    [Header("=== 드롭 위치 설정 ===")]
    [Tooltip("아이템 드롭 위치 (로컬 좌표)")]
    [SerializeField] private Vector3 dropPointOffset = new Vector3(0, 0.5f, 1f);

    [Header("=== 상태 ===")]
    [SerializeField] private List<StoredResource> storedItems = new List<StoredResource>();

    // 캐시
    private Building building;
    private Dictionary<int, StoredResource> itemDict = new Dictionary<int, StoredResource>();

    // Properties
    public IReadOnlyList<StoredResource> StoredItems => storedItems;
    public int MaxCapacity => maxCapacity;
    public int CurrentAmount => storedItems.Sum(s => s.Amount);
    public bool IsFull => maxCapacity > 0 && CurrentAmount >= maxCapacity;
    public bool IsMainStorage => isMainStorage;

    // 범위 Properties
    public Vector3 AccessAreaCenter => transform.TransformPoint(accessAreaCenter);
    public Vector3 AccessAreaSize => accessAreaSize;
    public float AccessDistance => accessDistance;
    public Vector3 DropPoint => transform.TransformPoint(dropPointOffset);

    // IInteractable 구현
    public bool CanInteract => true;
    public BuildingUIType UIType => BuildingUIType.Storage;

    // 이벤트
    public event Action<IStorage> OnStorageChanged;

    private void Awake()
    {
        building = GetComponent<Building>();
        RebuildDictionary();
    }

    private void Start()
    {
        if (isMainStorage && ResourceManager.Instance != null)
        {
            SyncWithResourceManager();
        }
    }

    // ==================== IStorage 구현 ====================

    public bool AddItem(ResourceItemSO item, int amount)
    {
        if (item == null || amount <= 0)
            return false;

        if (maxCapacity > 0)
        {
            int space = maxCapacity - CurrentAmount;
            if (space <= 0)
            {
                Debug.LogWarning($"[Storage] 저장소가 가득 찼습니다.");
                return false;
            }
            amount = Mathf.Min(amount, space);
        }

        if (itemDict.TryGetValue(item.ID, out var existing))
        {
            existing.Amount += amount;
        }
        else
        {
            var newStored = new StoredResource(item, amount);
            storedItems.Add(newStored);
            itemDict[item.ID] = newStored;
        }

        Debug.Log($"[Storage] {item.ResourceName} x{amount} 추가됨 (총: {GetItemCount(item)})");

        if (isMainStorage)
        {
            ResourceManager.Instance?.AddResource(item.ID, amount);
        }

        OnStorageChanged?.Invoke(this);
        return true;
    }

    public bool RemoveItem(ResourceItemSO item, int amount)
    {
        if (item == null || amount <= 0)
            return false;

        if (!itemDict.TryGetValue(item.ID, out var existing))
            return false;

        if (existing.Amount < amount)
            return false;

        existing.Amount -= amount;

        if (existing.Amount <= 0)
        {
            storedItems.Remove(existing);
            itemDict.Remove(item.ID);
        }

        Debug.Log($"[Storage] {item.ResourceName} x{amount} 제거됨 (남은: {GetItemCount(item)})");

        if (isMainStorage)
        {
            ResourceManager.Instance?.UseResource(item.ID, amount);
        }

        OnStorageChanged?.Invoke(this);
        return true;
    }

    public int GetItemCount(ResourceItemSO item)
    {
        if (item == null)
            return 0;

        return itemDict.TryGetValue(item.ID, out var stored) ? stored.Amount : 0;
    }

    public bool HasItem(ResourceItemSO item, int amount = 1)
    {
        return GetItemCount(item) >= amount;
    }

    // ==================== IInteractable 구현 ====================

    public void Interact()
    {
        BuildingInteractionManager.Instance?.OpenStorageUI(this);
    }

    // ==================== 추가 기능 ====================

    public List<StoredResource> TakeAllItems()
    {
        var items = new List<StoredResource>(storedItems);

        storedItems.Clear();
        itemDict.Clear();

        OnStorageChanged?.Invoke(this);
        return items;
    }

    public StoredResource GetFoodItem()
    {
        return storedItems.FirstOrDefault(s => s.Item != null && s.Item.IsFood);
    }

    private void SyncWithResourceManager()
    {
        // 필요시 구현
    }

    private void RebuildDictionary()
    {
        itemDict.Clear();
        foreach (var stored in storedItems)
        {
            if (stored.Item != null)
            {
                itemDict[stored.Item.ID] = stored;
            }
        }
    }

    public int GetFreeSpace()
    {
        if (maxCapacity < 0) return int.MaxValue;
        return Mathf.Max(0, maxCapacity - CurrentAmount);
    }

    // ==================== 범위 관련 ====================

    /// <summary>
    /// 특정 위치가 접근 범위 내인지 확인 (사각형)
    /// </summary>
    public bool IsInAccessArea(Vector3 worldPosition)
    {
        Vector3 localPos = transform.InverseTransformPoint(worldPosition);
        Vector3 halfSize = accessAreaSize * 0.5f;

        return Mathf.Abs(localPos.x - accessAreaCenter.x) <= halfSize.x &&
               Mathf.Abs(localPos.y - accessAreaCenter.y) <= halfSize.y &&
               Mathf.Abs(localPos.z - accessAreaCenter.z) <= halfSize.z;
    }

    /// <summary>
    /// Unit이 접근 가능한 거리인지 확인 (사각형 기준)
    /// </summary>
    public bool IsUnitInRange(Unit unit)
    {
        if (unit == null) return false;

        // 사각형 범위 내에 있는지 확인
        return IsInAccessArea(unit.transform.position);
    }

    /// <summary>
    /// 가장 가까운 접근 지점 가져오기 (사각형 테두리)
    /// </summary>
    public Vector3 GetNearestAccessPoint(Vector3 fromPosition)
    {
        Vector3 worldCenter = AccessAreaCenter;
        Vector3 halfSize = accessAreaSize * 0.5f;

        // 로컬 좌표로 변환
        Vector3 localFrom = transform.InverseTransformPoint(fromPosition);
        Vector3 localCenter = accessAreaCenter;

        // 방향 계산
        Vector3 direction = localFrom - localCenter;

        // 사각형 테두리의 가장 가까운 점 계산
        Vector3 accessPoint = localCenter;

        // X 방향이 더 지배적인지 Z 방향이 더 지배적인지 확인
        float ratioX = Mathf.Abs(direction.x) / halfSize.x;
        float ratioZ = Mathf.Abs(direction.z) / halfSize.z;

        if (ratioX > ratioZ)
        {
            // X 방향으로 접근
            accessPoint.x = localCenter.x + Mathf.Sign(direction.x) * (halfSize.x - 0.5f);
            accessPoint.z = Mathf.Clamp(localFrom.z, localCenter.z - halfSize.z + 0.5f, localCenter.z + halfSize.z - 0.5f);
        }
        else
        {
            // Z 방향으로 접근
            accessPoint.z = localCenter.z + Mathf.Sign(direction.z) * (halfSize.z - 0.5f);
            accessPoint.x = Mathf.Clamp(localFrom.x, localCenter.x - halfSize.x + 0.5f, localCenter.x + halfSize.x - 0.5f);
        }

        accessPoint.y = localCenter.y;

        // 월드 좌표로 변환
        Vector3 worldAccessPoint = transform.TransformPoint(accessPoint);

        // NavMesh 위의 유효한 점으로 보정
        if (UnityEngine.AI.NavMesh.SamplePosition(worldAccessPoint, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
        {
            return hit.position;
        }

        return worldAccessPoint;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // 저장소 타입 표시
        Gizmos.color = isMainStorage ? Color.magenta : Color.blue;
        Gizmos.DrawWireCube(transform.position + Vector3.up, Vector3.one * 0.5f);

        // ★ 접근 범위 표시 (사각형)
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(accessAreaCenter, accessAreaSize);

        // 반투명 채움
        Gizmos.color = new Color(0, 1, 0, 0.1f);
        Gizmos.DrawCube(accessAreaCenter, accessAreaSize);

        Gizmos.matrix = Matrix4x4.identity;

        // 드롭 위치 표시
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(DropPoint, 0.2f);
        Gizmos.DrawLine(transform.position, DropPoint);
    }
#endif
}