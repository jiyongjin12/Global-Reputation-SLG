using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// AI 행동 상태
/// </summary>
public enum AIBehaviorState
{
    Idle,               // 대기 중
    Wandering,          // 서성이기
    Socializing,        // 다른 유닛과 대화
    SeekingFood,        // 음식 찾는 중
    Eating,             // 먹는 중
    Working,            // 작업 중 (건설, 채집 등)
    ExecutingCommand,   // 플레이어 명령 수행 중
    Fleeing             // 도망
}

/// <summary>
/// 유닛 AI 관리자
/// 컬트 오브 더 램 스타일의 자율 행동 + 명령 시스템
/// </summary>
public class UnitAI : MonoBehaviour
{
    [Header("AI Settings")]
    [SerializeField] private float decisionInterval = 1f;           // AI 결정 주기
    [SerializeField] private float wanderRadius = 10f;              // 서성이기 반경
    [SerializeField] private float socialDistance = 3f;             // 대화 거리
    [SerializeField] private float foodSearchRadius = 20f;          // 음식 탐색 반경
    [SerializeField] private float idleMinTime = 2f;                // 최소 대기 시간
    [SerializeField] private float idleMaxTime = 5f;                // 최대 대기 시간
    [SerializeField] private float wanderMinTime = 3f;              // 최소 서성이기 시간
    [SerializeField] private float wanderMaxTime = 8f;              // 최대 서성이기 시간

    [Header("Hunger Settings")]
    [SerializeField] private float hungerDecreasePerMinute = 5f;    // 분당 허기 감소량
    [SerializeField] private float hungerSeekThreshold = 50f;       // 이 이하면 음식 찾기 시작
    [SerializeField] private float hungerCriticalThreshold = 20f;   // 위험 수준

    [Header("Work Settings")]
    [SerializeField] private int maxWorkersPerBuilding = 3;         // 건물당 최대 작업자
    [SerializeField] private int maxWorkersPerResource = 2;         // 자원당 최대 작업자

    [Header("Debug")]
    [SerializeField] private AIBehaviorState currentBehavior = AIBehaviorState.Idle;
    [SerializeField] private string currentBehaviorDetail = "";

    // 컴포넌트 참조
    private Unit unit;
    private NavMeshAgent agent;

    // 상태 관리
    private float lastDecisionTime;
    private float currentBehaviorEndTime;
    private Vector3 homePosition;           // 기본 위치 (서성이기 중심)
    private Unit socialTarget;              // 대화 상대

    // 플레이어 명령 큐
    private Queue<UnitTask> playerCommandQueue = new Queue<UnitTask>();
    private UnitTask currentPlayerCommand;

    // 이벤트
    public event Action<AIBehaviorState> OnBehaviorChanged;

    // Properties
    public AIBehaviorState CurrentBehavior => currentBehavior;
    public bool HasPlayerCommand => currentPlayerCommand != null || playerCommandQueue.Count > 0;
    public bool IsWorking => currentBehavior == AIBehaviorState.Working;
    public bool IsBusy => currentBehavior != AIBehaviorState.Idle && currentBehavior != AIBehaviorState.Wandering;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        agent = GetComponent<NavMeshAgent>();
        homePosition = transform.position;
    }

    private void Start()
    {
        if (unit != null)
        {
            // 허기 이벤트 연결
            unit.Stats.OnHungerCritical += OnHungerCritical;
        }

        // 첫 결정까지 약간의 딜레이
        lastDecisionTime = Time.time + UnityEngine.Random.Range(0f, decisionInterval);
    }

    private void Update()
    {
        if (unit == null || !unit.Stats.IsAlive) return;

        // 허기 감소 (분당 → 초당 변환)
        float hungerDecrease = (hungerDecreasePerMinute / 60f) * Time.deltaTime;
        unit.Stats.DecreaseHunger(hungerDecrease);

        // AI 결정 주기
        if (Time.time - lastDecisionTime >= decisionInterval)
        {
            MakeDecision();
            lastDecisionTime = Time.time;
        }

        // 현재 행동 업데이트
        UpdateCurrentBehavior();
    }

    /// <summary>
    /// AI 결정 로직 - 우선순위 기반
    /// </summary>
    private void MakeDecision()
    {
        // 이미 중요한 작업 중이면 스킵
        if (currentBehavior == AIBehaviorState.Eating ||
            currentBehavior == AIBehaviorState.ExecutingCommand)
        {
            return;
        }

        // ===== 우선순위 1: 생존 (허기) =====
        if (unit.Stats.Hunger <= hungerCriticalThreshold)
        {
            // 위험 수준 - 즉시 음식 찾기
            if (TryFindFood())
            {
                SetBehavior(AIBehaviorState.SeekingFood, "배고픔 위험!");
                return;
            }
        }
        else if (unit.Stats.Hunger <= hungerSeekThreshold && currentBehavior != AIBehaviorState.Working)
        {
            // 허기 시작 - 작업 중이 아니면 음식 찾기
            if (TryFindFood())
            {
                SetBehavior(AIBehaviorState.SeekingFood, "배고픔");
                return;
            }
        }

        // ===== 우선순위 2: 플레이어 명령 =====
        if (HasPlayerCommand)
        {
            ExecutePlayerCommand();
            return;
        }

        // ===== 우선순위 3: 자동 작업 (TaskManager에서 할당) =====
        // TaskManager가 자동으로 건설/채집 작업을 할당함
        // 작업이 할당되면 unit.IsIdle이 false가 됨
        if (!unit.IsIdle)
        {
            SetBehavior(AIBehaviorState.Working, "작업 수행 중");
            return;
        }

        // ===== 우선순위 4: 자유 행동 =====
        PerformFreeWill();
    }

    /// <summary>
    /// 자유 행동 - 서성이기, 대기, 대화
    /// </summary>
    private void PerformFreeWill()
    {
        // 현재 행동이 아직 진행 중이면 계속
        if (Time.time < currentBehaviorEndTime)
        {
            return;
        }

        // 랜덤하게 행동 선택
        float roll = UnityEngine.Random.value;

        if (roll < 0.4f)
        {
            // 40% - 서성이기
            StartWandering();
        }
        else if (roll < 0.6f)
        {
            // 20% - 다른 유닛과 대화 시도
            if (!TryStartSocializing())
            {
                // 대화 상대 없으면 서성이기
                StartWandering();
            }
        }
        else
        {
            // 40% - 제자리 대기
            StartIdling();
        }
    }

    /// <summary>
    /// 서성이기 시작
    /// </summary>
    private void StartWandering()
    {
        Vector3 randomPoint = GetRandomPointNearHome();

        if (randomPoint != Vector3.zero)
        {
            unit.AssignTask(new MoveToTask(randomPoint, TaskPriority.Low));
            float duration = UnityEngine.Random.Range(wanderMinTime, wanderMaxTime);
            currentBehaviorEndTime = Time.time + duration;
            SetBehavior(AIBehaviorState.Wandering, "서성이기");
        }
        else
        {
            StartIdling();
        }
    }

    /// <summary>
    /// 대기 시작
    /// </summary>
    private void StartIdling()
    {
        float duration = UnityEngine.Random.Range(idleMinTime, idleMaxTime);
        currentBehaviorEndTime = Time.time + duration;
        SetBehavior(AIBehaviorState.Idle, "대기 중");
    }

    /// <summary>
    /// 다른 유닛과 대화 시도
    /// </summary>
    private bool TryStartSocializing()
    {
        // 주변의 Idle/Wandering 상태 유닛 찾기
        var nearbyUnits = FindNearbyIdleUnits();

        if (nearbyUnits.Count > 0)
        {
            socialTarget = nearbyUnits[UnityEngine.Random.Range(0, nearbyUnits.Count)];

            // 상대방에게 다가가기
            Vector3 meetPoint = (transform.position + socialTarget.transform.position) / 2f;
            unit.AssignTask(new MoveToTask(meetPoint, TaskPriority.Low));

            float duration = UnityEngine.Random.Range(3f, 6f);
            currentBehaviorEndTime = Time.time + duration;
            SetBehavior(AIBehaviorState.Socializing, $"대화 중: {socialTarget.UnitName}");

            // TODO: 상대방 유닛에게도 대화 상태 알리기
            return true;
        }

        return false;
    }

    /// <summary>
    /// 음식 찾기 시도
    /// </summary>
    private bool TryFindFood()
    {
        // 바닥에 떨어진 음식 찾기
        DroppedItem foodItem = FindNearestFood();

        if (foodItem != null)
        {
            // 음식 줍기 작업 할당
            unit.AssignTaskImmediate(new MoveToTask(foodItem.transform.position, TaskPriority.Critical));
            unit.AssignTask(new EatFoodTask(foodItem, TaskPriority.Critical));
            return true;
        }

        // TODO: 음식 저장소에서 가져오기

        return false;
    }

    /// <summary>
    /// 가장 가까운 음식 찾기
    /// </summary>
    private DroppedItem FindNearestFood()
    {
        DroppedItem[] allItems = FindObjectsOfType<DroppedItem>();
        DroppedItem nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var item in allItems)
        {
            if (item == null || !item.IsAvailable) continue;

            // 음식인지 확인
            if (item.Resource != null && item.Resource.IsFood)
            {
                float dist = Vector3.Distance(transform.position, item.transform.position);
                if (dist < foodSearchRadius && dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = item;
                }
            }
        }

        return nearest;
    }

    /// <summary>
    /// 주변의 Idle 상태 유닛 찾기
    /// </summary>
    private List<Unit> FindNearbyIdleUnits()
    {
        var result = new List<Unit>();
        var allUnits = FindObjectsOfType<Unit>();

        foreach (var otherUnit in allUnits)
        {
            if (otherUnit == unit) continue;
            if (!otherUnit.Stats.IsAlive) continue;

            float dist = Vector3.Distance(transform.position, otherUnit.transform.position);
            if (dist > socialDistance * 2) continue;

            // 상대방도 자유로운 상태인지 확인
            var otherAI = otherUnit.GetComponent<UnitAI>();
            if (otherAI != null && (otherAI.CurrentBehavior == AIBehaviorState.Idle ||
                                     otherAI.CurrentBehavior == AIBehaviorState.Wandering))
            {
                result.Add(otherUnit);
            }
        }

        return result;
    }

    /// <summary>
    /// 홈 근처 랜덤 위치 가져오기
    /// </summary>
    private Vector3 GetRandomPointNearHome()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 randomDir = UnityEngine.Random.insideUnitSphere * wanderRadius;
            randomDir.y = 0;
            Vector3 targetPos = homePosition + randomDir;

            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        return Vector3.zero;
    }

    /// <summary>
    /// 현재 행동 업데이트
    /// </summary>
    private void UpdateCurrentBehavior()
    {
        switch (currentBehavior)
        {
            case AIBehaviorState.Socializing:
                UpdateSocializing();
                break;

            case AIBehaviorState.SeekingFood:
                // 음식을 찾으면 Eating으로 전환 (Task에서 처리)
                break;
        }
    }

    private void UpdateSocializing()
    {
        // 대화 상대가 멀어지거나 없어지면 종료
        if (socialTarget == null ||
            Vector3.Distance(transform.position, socialTarget.transform.position) > socialDistance * 3)
        {
            socialTarget = null;
            StartIdling();
        }
    }

    // ===== 플레이어 명령 시스템 =====

    /// <summary>
    /// 플레이어 명령 추가 (최우선 순위)
    /// </summary>
    public void AddPlayerCommand(UnitTask task)
    {
        playerCommandQueue.Enqueue(task);
        Debug.Log($"[UnitAI] {unit.UnitName}: 플레이어 명령 추가 - {task.Type}");

        // 현재 자유 행동 중이면 즉시 명령 수행
        if (!IsBusy)
        {
            ExecutePlayerCommand();
        }
    }

    /// <summary>
    /// 플레이어 명령 즉시 실행 (현재 작업 중단)
    /// </summary>
    public void AddPlayerCommandImmediate(UnitTask task)
    {
        // 현재 명령 취소하고 새 명령을 최우선으로
        currentPlayerCommand?.Cancel();
        currentPlayerCommand = null;

        // 큐 앞에 추가하는 대신 바로 실행
        unit.AssignTaskImmediate(task);
        currentPlayerCommand = task;
        SetBehavior(AIBehaviorState.ExecutingCommand, $"명령 수행: {task.Type}");

        Debug.Log($"[UnitAI] {unit.UnitName}: 플레이어 긴급 명령 - {task.Type}");
    }

    /// <summary>
    /// 플레이어 명령 실행
    /// </summary>
    private void ExecutePlayerCommand()
    {
        if (currentPlayerCommand == null && playerCommandQueue.Count > 0)
        {
            currentPlayerCommand = playerCommandQueue.Dequeue();
        }

        if (currentPlayerCommand != null)
        {
            unit.AssignTaskImmediate(currentPlayerCommand);
            SetBehavior(AIBehaviorState.ExecutingCommand, $"명령 수행: {currentPlayerCommand.Type}");
        }
    }

    /// <summary>
    /// 현재 명령 완료 알림 (Unit에서 호출)
    /// </summary>
    public void OnTaskCompleted(UnitTask task)
    {
        if (task == currentPlayerCommand)
        {
            currentPlayerCommand = null;

            // 다음 플레이어 명령이 있으면 실행
            if (playerCommandQueue.Count > 0)
            {
                ExecutePlayerCommand();
            }
            else
            {
                SetBehavior(AIBehaviorState.Idle, "명령 완료");
            }
        }
    }

    /// <summary>
    /// 모든 플레이어 명령 취소
    /// </summary>
    public void ClearPlayerCommands()
    {
        playerCommandQueue.Clear();
        currentPlayerCommand?.Cancel();
        currentPlayerCommand = null;
    }

    // ===== 이벤트 핸들러 =====

    private void OnHungerCritical()
    {
        Debug.Log($"[UnitAI] {unit.UnitName}: 배고픔 위험!");
        // 다음 결정 때 음식 찾기 시도
    }

    // ===== 유틸리티 =====

    private void SetBehavior(AIBehaviorState newBehavior, string detail = "")
    {
        if (currentBehavior != newBehavior)
        {
            currentBehavior = newBehavior;
            currentBehaviorDetail = detail;
            OnBehaviorChanged?.Invoke(newBehavior);

            Debug.Log($"[UnitAI] {unit.UnitName}: {newBehavior} - {detail}");
        }
    }

    /// <summary>
    /// 홈 위치 설정
    /// </summary>
    public void SetHomePosition(Vector3 position)
    {
        homePosition = position;
    }

    /// <summary>
    /// 음식 먹기 완료 알림
    /// </summary>
    public void OnFoodEaten(float nutritionValue)
    {
        unit.Stats.Eat(nutritionValue);
        SetBehavior(AIBehaviorState.Idle, "식사 완료");
        Debug.Log($"[UnitAI] {unit.UnitName}: 음식 먹음 (영양: {nutritionValue})");
    }
}