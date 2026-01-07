// GameInitializer.cs
using UnityEngine;

public class GameInitializer : MonoBehaviour
{
    public ResourceItemSO wood;
    public ResourceItemSO stone;
    public ResourceItemSO food;

    void Start()
    {
        // 초기 자원 지급
        ResourceManager.Instance.AddResource(wood, 50);
        ResourceManager.Instance.AddResource(stone, 30);
        ResourceManager.Instance.AddResource(food, 15);

        // 테스트 유닛 생성
        UnitManager.Instance.SpawnWorker(Vector3.zero, "일꾼1");
        UnitManager.Instance.SpawnWorker(new Vector3(2, 0, 0), "일꾼2");
    }
}