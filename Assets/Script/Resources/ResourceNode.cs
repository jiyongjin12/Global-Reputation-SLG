using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 자원 노드 (나무, 돌 등)
/// 유닛이 채집할 수 있는 자연 오브젝트
/// </summary>
public class ResourceNode : MonoBehaviour
{
    [SerializeField] private ResourceNodeData data;

    private float currentHP;
    private bool isDepleted = false;

    // 이벤트
    public event Action<ResourceNode> OnDepleted;
    public event Action<ResourceNode, float> OnDamaged; // (node, remainingHP)

    // Properties
    public ResourceNodeData Data => data;
    public float CurrentHP => currentHP;
    public float HPPercent => data.MaxHP > 0 ? currentHP / data.MaxHP : 0f;
    public bool IsDepleted => isDepleted;
    public bool CanBeHarvested => !isDepleted && currentHP > 0;
    public Vector3Int GridPosition { get; private set; }

    private void Awake()
    {
        if (data != null)
        {
            currentHP = data.MaxHP;
        }
    }

    /// <summary>
    /// 노드 초기화
    /// </summary>
    public void Initialize(ResourceNodeData nodeData, Vector3Int gridPos)
    {
        data = nodeData;
        GridPosition = gridPos;
        currentHP = data.MaxHP;
        isDepleted = false;
    }

    /// <summary>
    /// 유닛이 채집 작업 수행
    /// </summary>
    /// <param name="harvestPower">채집력 (기본 1.0, 특성에 따라 증가)</param>
    /// <returns>드롭된 아이템 리스트 (아직 완전히 파괴되지 않으면 빈 리스트)</returns>
    public List<DroppedItem> Harvest(float harvestPower = 1f)
    {
        if (!CanBeHarvested)
            return new List<DroppedItem>();

        float damage = data.HarvestWorkPerHit * harvestPower;
        currentHP -= damage;

        OnDamaged?.Invoke(this, currentHP);

        // 시각적 피드백 (흔들림 등)
        // TODO: 애니메이션 또는 파티클

        if (currentHP <= 0)
        {
            return Deplete();
        }

        return new List<DroppedItem>();
    }

    /// <summary>
    /// 자원 고갈 처리
    /// </summary>
    private List<DroppedItem> Deplete()
    {
        isDepleted = true;
        currentHP = 0;

        // 드롭 아이템 생성
        List<DroppedItem> droppedItems = SpawnDrops();

        OnDepleted?.Invoke(this);

        // 리스폰 가능하면 일정 시간 후 리스폰, 아니면 제거
        if (data.CanRespawn)
        {
            StartCoroutine(RespawnCoroutine());
        }
        else
        {
            // GridData에서 제거 필요
            Destroy(gameObject);
        }

        return droppedItems;
    }

    /// <summary>
    /// ★ 드롭 아이템 생성 + 튀어나오는 애니메이션
    /// </summary>
    private List<DroppedItem> SpawnDrops()
    {
        List<DroppedItem> droppedItems = new List<DroppedItem>();

        foreach (var drop in data.Drops)
        {
            int amount = drop.GetRandomAmount();

            for (int i = 0; i < amount; i++)
            {
                if (drop.Resource.DropPrefab != null)
                {
                    // 스폰 위치는 자원 중심
                    Vector3 spawnPos = transform.position + Vector3.up * 0.5f;

                    GameObject dropObj = Instantiate(drop.Resource.DropPrefab, spawnPos, Quaternion.identity);
                    DroppedItem droppedItem = dropObj.GetComponent<DroppedItem>();

                    if (droppedItem == null)
                    {
                        droppedItem = dropObj.AddComponent<DroppedItem>();
                    }

                    droppedItem.Initialize(drop.Resource, 1);
                    droppedItems.Add(droppedItem);

                    // ★ 튀어나오는 애니메이션 시작!
                    droppedItem.PlayDropAnimation(spawnPos);

                    // TaskManager에 줍기 작업 등록 (자동 흡수 안 되면 수동으로 주움)
                    if (TaskManager.Instance != null)
                    {
                        TaskManager.Instance.AddPickupItemTask(droppedItem);
                    }
                }
            }
        }

        Debug.Log($"[ResourceNode] {data.Name} dropped {droppedItems.Count} items");
        return droppedItems;
    }

    /// <summary>
    /// 리스폰 코루틴
    /// </summary>
    private System.Collections.IEnumerator RespawnCoroutine()
    {
        // 비주얼 숨기기
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
        }

        yield return new WaitForSeconds(data.RespawnTime);

        // 리스폰
        currentHP = data.MaxHP;
        isDepleted = false;

        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = true;
        }

        // ★ TaskManager에 다시 채집 작업 등록
        if (TaskManager.Instance != null)
        {
            TaskManager.Instance.AddHarvestTask(this);
        }

        Debug.Log($"[ResourceNode] {data.Name} respawned");
    }
}