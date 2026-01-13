// GameInitializer.cs
using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    [Header("=== 초기 자원 ===")]
    public ResourceItemSO wood;
    public ResourceItemSO stone;
    public ResourceItemSO food;

    [Header("=== 유닛 스폰 위치 ===")]
    public Transform spawnPoint;

    void Start()
    {
        // 초기 자원 지급
        if (ResourceManager.Instance != null)
        {
            ResourceManager.Instance.AddResource(wood, 50);
            ResourceManager.Instance.AddResource(stone, 30);
            ResourceManager.Instance.AddResource(food, 15);
        }

        // 테스트 유닛 생성
        SpawnTestUnits();
    }

    private void SpawnTestUnits()
    {
        if (UnitManager.Instance == null)
        {
            Debug.LogError("[GameInitializer] UnitManager가 없습니다!");
            return;
        }

        Vector3 basePos = spawnPoint != null ? spawnPoint.position : Vector3.zero;

        // 워커 2명
        UnitManager.Instance.CreateUnit(basePos + new Vector3(0, 0, 0), UnitType.Worker, "일꾼1");
        UnitManager.Instance.CreateUnit(basePos + new Vector3(2, 0, 0), UnitType.Worker, "일꾼2");

        // 전투 유닛 1명
        UnitManager.Instance.CreateUnit(basePos + new Vector3(4, 0, 0), UnitType.Fighter, "전사1");

        Debug.Log("[GameInitializer] 테스트 유닛 생성 완료 (워커 2, 전투 1)");
    }
}