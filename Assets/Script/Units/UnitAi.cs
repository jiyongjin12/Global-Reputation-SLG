using UnityEngine;
using UnityEngine.AI;

public enum AIBehaviorState
{
    Idle,
    Wandering,
    SeekingFood,
    Eating,
    Working,
    ExecutingCommand,
    Fleeing
}

/// <summary>
/// 유닛 AI (Mind) - Pull 방식 작업 수행
/// 우선순위: 생존 > 플레이어 명령 > 자동 작업 > 자유 행동
/// </summary>
public class UnitAI : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float decisionInterval = 0.5f;
    [SerializeField] private float hungerDecreasePerMinute = 3f;
    [SerializeField] private float foodSearchRadius = 20f;
    [SerializeField] private float workRadius = 2f;
    [SerializeField] private float wanderRadius = 10f;

    [Header("Thresholds")]
    [SerializeField] private float hungerSeekThreshold = 50f;
    [SerializeField] private float hungerCriticalThreshold = 20f;

    [Header("Debug")]
    [SerializeField] private AIBehaviorState currentBehavior = AIBehaviorState.Idle;

    private Unit unit;
    private UnitBlackboard bb;
    private NavMeshAgent agent;

    private float lastDecisionTime;
    private float workTimer;

    // Properties
    public AIBehaviorState CurrentBehavior => currentBehavior;
    public bool HasPlayerCommand => bb?.HasPlayerCommand ?? false;
    public bool IsBusy => currentBehavior != AIBehaviorState.Idle && currentBehavior != AIBehaviorState.Wandering;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        bb = unit.Blackboard;
        if (bb != null)
            bb.OnHungerCritical += OnHungerCritical;
    }

    private void Update()
    {
        if (bb == null || !bb.IsAlive) return;

        // 배고픔 감소
        bb.DecreaseHunger((hungerDecreasePerMinute / 60f) * Time.deltaTime);

        // 기아 시 체력 감소
        if (bb.IsStarving)
            unit.TakeDamage(Time.deltaTime * 5f);

        // 주기적 의사결정
        if (Time.time - lastDecisionTime >= decisionInterval)
        {
            MakeDecision();
            lastDecisionTime = Time.time;
        }

        // 현재 행동 실행
        ExecuteCurrentBehavior();
    }

    private void MakeDecision()
    {
        // 1. 생존 (최우선)
        if (bb.Hunger <= hungerCriticalThreshold)
        {
            if (TrySeekFood())
            {
                SetBehavior(AIBehaviorState.SeekingFood);
                return;
            }
        }

        // 2. 플레이어 명령
        if (bb.HasPlayerCommand && bb.PlayerCommand != null)
        {
            ExecutePlayerCommand();
            return;
        }

        // 3. 현재 작업 계속
        if (bb.CurrentTask != null)
            return;

        // 4. 자동 작업 (Pull)
        if (bb.IsIdle)
        {
            // 배고프면 음식 먼저
            if (bb.Hunger <= hungerSeekThreshold && TrySeekFood())
            {
                SetBehavior(AIBehaviorState.SeekingFood);
                return;
            }

            // TaskManager에서 Pull
            if (TryPullTask())
            {
                SetBehavior(AIBehaviorState.Working);
                return;
            }

            // 5. 자유 행동
            PerformFreeWill();
        }
    }

    private bool TryPullTask()
    {
        if (TaskManager.Instance == null) return false;

        var task = TaskManager.Instance.FindNearestTask(unit);
        if (task != null && TaskManager.Instance.TakeTask(task, unit))
        {
            bb.CurrentTask = task;
            bb.TargetPosition = task.Data.TargetPosition;
            bb.TargetObject = task.Data.TargetObject;
            unit.MoveTo(task.Data.TargetPosition);

            Debug.Log($"[UnitAI] {unit.UnitName}: 작업 수락 - {task.Data.Type}");
            return true;
        }
        return false;
    }

    private void ExecuteCurrentBehavior()
    {
        switch (currentBehavior)
        {
            case AIBehaviorState.SeekingFood:
                UpdateSeekingFood();
                break;
            case AIBehaviorState.Working:
                UpdateWorking();
                break;
            case AIBehaviorState.ExecutingCommand:
                UpdatePlayerCommand();
                break;
            case AIBehaviorState.Wandering:
                UpdateWandering();
                break;
        }
    }

    // ==================== 음식 ====================

    private bool TrySeekFood()
    {
        var food = FindNearestFood();
        if (food != null)
        {
            bb.NearestFood = food;
            bb.TargetPosition = food.transform.position;
            unit.MoveTo(food.transform.position);
            return true;
        }
        return false;
    }

    private void UpdateSeekingFood()
    {
        if (bb.NearestFood == null)
        {
            SetBehavior(AIBehaviorState.Idle);
            return;
        }

        float dist = Vector3.Distance(transform.position, bb.NearestFood.transform.position);
        if (dist < 1.5f)
        {
            var food = bb.NearestFood;
            if (food.Resource != null && food.Resource.IsFood)
            {
                bb.Eat(food.Resource.NutritionValue * food.Amount);
                unit.Heal(food.Resource.HealthRestore * food.Amount);
                food.PickUp(unit);
            }
            bb.NearestFood = null;
            SetBehavior(AIBehaviorState.Idle);
        }
    }

    private DroppedItem FindNearestFood()
    {
        var items = FindObjectsOfType<DroppedItem>();
        DroppedItem nearest = null;
        float nearestDist = foodSearchRadius;

        foreach (var item in items)
        {
            if (!item.IsAvailable) continue;
            if (item.Resource == null || !item.Resource.IsFood) continue;

            float dist = Vector3.Distance(transform.position, item.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = item;
            }
        }
        return nearest;
    }

    // ==================== 작업 ====================

    private void UpdateWorking()
    {
        if (bb.CurrentTask == null)
        {
            SetBehavior(AIBehaviorState.Idle);
            return;
        }

        var task = bb.CurrentTask;
        float dist = Vector3.Distance(transform.position, task.Data.TargetPosition);

        if (dist > workRadius)
        {
            if (unit.HasArrivedAtDestination())
                unit.MoveTo(task.Data.TargetPosition);
            return;
        }

        // 작업 수행
        workTimer += Time.deltaTime;
        if (workTimer >= 1f)
        {
            workTimer = 0f;
            PerformWork(task);
        }
    }

    private void PerformWork(PostedTask task)
    {
        switch (task.Data.Type)
        {
            case TaskType.Construct:
                var building = task.Owner as Building;
                if (building == null || building.CurrentState == BuildingState.Completed)
                {
                    CompleteCurrentTask();
                    return;
                }
                float work = unit.DoWork();
                if (building.DoConstructionWork(work))
                {
                    TaskManager.Instance?.CompleteTask(task);
                    CompleteCurrentTask();
                }
                break;

            case TaskType.Harvest:
                var node = task.Owner as ResourceNode;
                if (node == null || node.IsDepleted)
                {
                    CompleteCurrentTask();
                    return;
                }
                float gather = unit.DoGather(node.Data?.NodeType);
                var drops = node.Harvest(gather);
                foreach (var drop in drops)
                {
                    if (!unit.Inventory.IsFull)
                    {
                        unit.Inventory.AddItem(drop.Resource, drop.Amount);
                        Destroy(drop.gameObject);
                    }
                }
                if (node.IsDepleted || unit.Inventory.IsFull)
                    CompleteCurrentTask();
                break;
        }
    }

    private void CompleteCurrentTask()
    {
        if (bb.CurrentTask != null)
            TaskManager.Instance?.LeaveTask(bb.CurrentTask, unit);

        bb.CurrentTask = null;
        bb.TargetObject = null;
        bb.TargetPosition = null;
        SetBehavior(AIBehaviorState.Idle);
    }

    // ==================== 플레이어 명령 ====================

    public void GiveCommand(UnitCommand command)
    {
        bb.HasPlayerCommand = true;
        bb.PlayerCommand = command;

        if (bb.CurrentTask != null)
        {
            TaskManager.Instance?.LeaveTask(bb.CurrentTask, unit);
            bb.CurrentTask = null;
        }

        ExecutePlayerCommand();
    }

    private void ExecutePlayerCommand()
    {
        var cmd = bb.PlayerCommand;
        if (cmd == null) return;

        SetBehavior(AIBehaviorState.ExecutingCommand);

        switch (cmd.Type)
        {
            case UnitCommandType.MoveTo:
                if (cmd.TargetPosition.HasValue)
                    unit.MoveTo(cmd.TargetPosition.Value);
                break;
            case UnitCommandType.Stop:
                unit.StopMoving();
                ClearPlayerCommand();
                break;
        }
    }

    private void UpdatePlayerCommand()
    {
        if (!bb.HasPlayerCommand || bb.PlayerCommand == null)
        {
            SetBehavior(AIBehaviorState.Idle);
            return;
        }

        if (bb.PlayerCommand.Type == UnitCommandType.MoveTo && unit.HasArrivedAtDestination())
        {
            ClearPlayerCommand();
            SetBehavior(AIBehaviorState.Idle);
        }
    }

    private void ClearPlayerCommand()
    {
        bb.HasPlayerCommand = false;
        bb.PlayerCommand = null;
    }

    // ==================== 자유 행동 ====================

    private void PerformFreeWill()
    {
        if (Random.value < 0.5f)
            StartWandering();
        else
            SetBehavior(AIBehaviorState.Idle);
    }

    private void StartWandering()
    {
        Vector3 randomPoint = transform.position + Random.insideUnitSphere * wanderRadius;
        randomPoint.y = transform.position.y;

        if (NavMesh.SamplePosition(randomPoint, out var hit, wanderRadius, NavMesh.AllAreas))
        {
            unit.MoveTo(hit.position);
            SetBehavior(AIBehaviorState.Wandering);
        }
    }

    private void UpdateWandering()
    {
        if (unit.HasArrivedAtDestination())
            SetBehavior(AIBehaviorState.Idle);
    }

    // ==================== 유틸리티 ====================

    private void SetBehavior(AIBehaviorState newBehavior)
    {
        currentBehavior = newBehavior;
        bb?.SetState(newBehavior switch
        {
            AIBehaviorState.Idle => UnitState.Idle,
            AIBehaviorState.Working => UnitState.Working,
            AIBehaviorState.SeekingFood or AIBehaviorState.Eating => UnitState.Eating,
            AIBehaviorState.Wandering or AIBehaviorState.ExecutingCommand => UnitState.Moving,
            _ => UnitState.Idle
        });
    }

    private void OnHungerCritical()
    {
        Debug.Log($"[UnitAI] {unit.UnitName}: 배고픔!");
    }

    // 호환성
    public void AddPlayerCommand(UnitTask task) { }
    public void AddPlayerCommandImmediate(UnitTask task) { }
    public void OnTaskCompleted(UnitTask task) { }
    public void OnFoodEaten(float nutrition) => bb?.Eat(nutrition);
}