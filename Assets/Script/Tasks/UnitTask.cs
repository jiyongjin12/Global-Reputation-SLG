using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 작업 타입
/// </summary>
public enum TaskType
{
    Idle,
    MoveTo,
    Construct,
    Harvest,
    PickupItem,
    DeliverToStorage,
    Eat,
    Attack,
    Flee,
    Workstation  // ★ 추가: 워크스테이션 작업
}

/// <summary>
/// 작업 우선순위
/// </summary>
public enum TaskPriority
{
    Critical = 0,
    High = 1,
    Normal = 2,
    Low = 3
}

/// <summary>
/// 작업 상태
/// </summary>
public enum TaskState
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 기본 작업 클래스
/// </summary>
[Serializable]
public abstract class UnitTask
{
    public TaskType Type { get; protected set; }
    public TaskPriority Priority { get; protected set; }
    public TaskState State { get; protected set; } = TaskState.Pending;

    public Vector3 TargetPosition { get; protected set; }
    public GameObject TargetObject { get; protected set; }

    public event Action<UnitTask> OnTaskCompleted;
    public event Action<UnitTask> OnTaskFailed;

    public abstract void Execute(Unit unit);
    public abstract bool IsComplete(Unit unit);

    public virtual void Start(Unit unit)
    {
        State = TaskState.InProgress;
    }

    public virtual void Complete(Unit unit)
    {
        State = TaskState.Completed;
        OnTaskCompleted?.Invoke(this);
    }

    public virtual void Fail(Unit unit)
    {
        State = TaskState.Failed;
        OnTaskFailed?.Invoke(this);
    }

    public virtual void Cancel()
    {
        State = TaskState.Cancelled;
    }
}

/// <summary>
/// 이동 작업
/// </summary>
public class MoveToTask : UnitTask
{
    private float arrivalThreshold = 1.5f;

    public MoveToTask(Vector3 destination, TaskPriority priority = TaskPriority.Normal)
    {
        Type = TaskType.MoveTo;
        Priority = priority;
        TargetPosition = destination;
    }

    public override void Start(Unit unit)
    {
        base.Start(unit);
        unit.MoveTo(TargetPosition);
        Debug.Log($"[MoveToTask] {unit.UnitName} 이동 시작 → 목표: {TargetPosition}");
    }

    public override void Execute(Unit unit)
    {
        if (!IsComplete(unit))
        {
            if (!unit.Agent.hasPath || unit.Agent.remainingDistance < 0.1f)
            {
                unit.MoveTo(TargetPosition);
            }
        }
    }

    public override bool IsComplete(Unit unit)
    {
        if (unit.HasArrivedAtDestination())
        {
            Debug.Log($"[MoveToTask] {unit.UnitName} 도착! (NavMesh 기준)");
            return true;
        }

        Vector3 unitPos = unit.transform.position;
        Vector3 targetPos = TargetPosition;

        float horizontalDistance = Vector2.Distance(
            new Vector2(unitPos.x, unitPos.z),
            new Vector2(targetPos.x, targetPos.z)
        );

        if (horizontalDistance < arrivalThreshold)
        {
            Debug.Log($"[MoveToTask] {unit.UnitName} 도착! (거리: {horizontalDistance:F2}m)");
            return true;
        }

        return false;
    }
}

/// <summary>
/// 건설 작업
/// </summary>
public class ConstructTask : UnitTask
{
    private Building targetBuilding;
    private float workInterval = 1f;
    private float lastWorkTime;
    private float workRange = 2f;

    public ConstructTask(Building building, TaskPriority priority = TaskPriority.Normal)
    {
        Type = TaskType.Construct;
        Priority = priority;
        targetBuilding = building;
        TargetObject = building.gameObject;
        TargetPosition = building.transform.position;
        Debug.Log($"[ConstructTask] 건설 작업 생성됨: {building.Data.Name}");
    }

    public override void Start(Unit unit)
    {
        base.Start(unit);
        Debug.Log($"[ConstructTask] {unit.UnitName}이(가) {targetBuilding.Data.Name} 건설 작업 시작");
    }

    public override void Execute(Unit unit)
    {
        float distance = Vector3.Distance(unit.transform.position, TargetPosition);

        if (distance > workRange)
        {
            Debug.Log($"[ConstructTask] {unit.UnitName} 건물까지 거리: {distance:F1}m (작업 범위: {workRange}m) - 이동 중...");
            unit.MoveTo(TargetPosition);
            return;
        }

        if (Time.time - lastWorkTime >= workInterval)
        {
            float workAmount = unit.Stats.WorkSpeed;
            Debug.Log($"[ConstructTask] {unit.UnitName}이(가) 작업 수행! (작업량: {workAmount})");
            targetBuilding.DoConstructionWork(workAmount);
            lastWorkTime = Time.time;
        }
    }

    public override bool IsComplete(Unit unit)
    {
        bool complete = targetBuilding == null || targetBuilding.CurrentState == BuildingState.Completed;
        if (complete)
        {
            Debug.Log($"[ConstructTask] 건설 완료!");
        }
        return complete;
    }
}

/// <summary>
/// 채집 작업
/// </summary>
public class HarvestTask : UnitTask
{
    private ResourceNode targetNode;
    private float workInterval = 1f;
    private float lastWorkTime;

    public HarvestTask(ResourceNode node, TaskPriority priority = TaskPriority.Normal)
    {
        Type = TaskType.Harvest;
        Priority = priority;
        targetNode = node;
        TargetObject = node.gameObject;
        TargetPosition = node.transform.position;
    }

    public override void Execute(Unit unit)
    {
        if (Time.time - lastWorkTime >= workInterval)
        {
            float gatherPower = unit.Stats.GatherPower;
            var drops = targetNode.Harvest(gatherPower);

            foreach (var drop in drops)
            {
                if (!unit.Inventory.IsFull)
                {
                    unit.Inventory.AddItem(drop.Resource, drop.Amount);
                    UnityEngine.Object.Destroy(drop.gameObject);
                }
            }

            lastWorkTime = Time.time;
        }
    }

    public override bool IsComplete(Unit unit)
    {
        return targetNode == null || targetNode.IsDepleted || unit.Inventory.IsFull;
    }
}

/// <summary>
/// 아이템 줍기 작업
/// </summary>
public class PickupItemTask : UnitTask
{
    private DroppedItem targetItem;

    public PickupItemTask(DroppedItem item, TaskPriority priority = TaskPriority.Normal)
    {
        Type = TaskType.PickupItem;
        Priority = priority;
        targetItem = item;
        TargetObject = item.gameObject;
        TargetPosition = item.transform.position;
    }

    public override void Start(Unit unit)
    {
        base.Start(unit);
        targetItem.Reserve(unit);
    }

    public override void Execute(Unit unit)
    {
        if (Vector3.Distance(unit.transform.position, TargetPosition) < 0.5f)
        {
            if (targetItem != null)
            {
                unit.Inventory.AddItem(targetItem.Resource, targetItem.Amount);
                targetItem.PickUp(unit);
            }
        }
    }

    public override bool IsComplete(Unit unit)
    {
        return targetItem == null;
    }

    public override void Cancel()
    {
        base.Cancel();
        targetItem?.CancelReservation();
    }
}

/// <summary>
/// 창고에 저장 작업
/// </summary>
public class DeliverToStorageTask : UnitTask
{
    private Building storageBuilding;

    public DeliverToStorageTask(Building storage, TaskPriority priority = TaskPriority.Normal)
    {
        Type = TaskType.DeliverToStorage;
        Priority = priority;
        storageBuilding = storage;
        TargetObject = storage.gameObject;
        TargetPosition = storage.transform.position;
    }

    public override void Execute(Unit unit)
    {
        if (Vector3.Distance(unit.transform.position, TargetPosition) < 1f)
        {
            unit.Inventory.DepositToStorage();
        }
    }

    public override bool IsComplete(Unit unit)
    {
        return unit.Inventory.IsEmpty;
    }
}

/// <summary>
/// 음식 먹기 작업
/// </summary>
public class EatFoodTask : UnitTask
{
    private DroppedItem targetFood;
    private bool hasEaten = false;

    public EatFoodTask(DroppedItem food, TaskPriority priority = TaskPriority.Critical)
    {
        Type = TaskType.Eat;
        Priority = priority;
        targetFood = food;

        if (food != null)
        {
            TargetObject = food.gameObject;
            TargetPosition = food.transform.position;
        }
    }

    public override void Start(Unit unit)
    {
        base.Start(unit);
        targetFood?.Reserve(unit);
        Debug.Log($"[EatFoodTask] {unit.UnitName} 음식 먹기 시작");
    }

    public override void Execute(Unit unit)
    {
        if (hasEaten) return;

        if (targetFood == null)
        {
            hasEaten = true;
            return;
        }

        float dist = Vector3.Distance(unit.transform.position, TargetPosition);
        if (dist < 1f)
        {
            if (targetFood.Resource != null && targetFood.Resource.IsFood)
            {
                float nutrition = targetFood.Resource.NutritionValue * targetFood.Amount;
                unit.Stats.Eat(nutrition);

                if (targetFood.Resource.HealthRestore > 0)
                {
                    unit.Stats.Heal(targetFood.Resource.HealthRestore * targetFood.Amount);
                }

                Debug.Log($"[EatFoodTask] {unit.UnitName} 음식 먹음! 영양: {nutrition}");

                var ai = unit.GetComponent<UnitAI>();
                ai?.OnFoodEaten(nutrition);
            }

            targetFood.PickUp(unit);
            hasEaten = true;
        }
    }

    public override bool IsComplete(Unit unit)
    {
        return hasEaten || targetFood == null;
    }

    public override void Cancel()
    {
        base.Cancel();
        targetFood?.CancelReservation();
    }
}

/// <summary>
/// 서성이기 작업 (자유 행동용)
/// </summary>
public class WanderTask : UnitTask
{
    private float duration;
    private float startTime;
    private bool hasArrived = false;

    public WanderTask(Vector3 destination, float wanderDuration = 5f, TaskPriority priority = TaskPriority.Low)
    {
        Type = TaskType.Idle;
        Priority = priority;
        TargetPosition = destination;
        duration = wanderDuration;
    }

    public override void Start(Unit unit)
    {
        base.Start(unit);
        startTime = Time.time;
        unit.MoveTo(TargetPosition);
    }

    public override void Execute(Unit unit)
    {
        float dist = Vector3.Distance(unit.transform.position, TargetPosition);
        if (dist < 1f)
        {
            hasArrived = true;
        }
    }

    public override bool IsComplete(Unit unit)
    {
        return hasArrived || (Time.time - startTime >= duration);
    }
}

/// <summary>
/// 대기 작업 (제자리에서 가만히)
/// </summary>
public class IdleWaitTask : UnitTask
{
    private float duration;
    private float startTime;

    public IdleWaitTask(float waitDuration = 3f, TaskPriority priority = TaskPriority.Low)
    {
        Type = TaskType.Idle;
        Priority = priority;
        duration = waitDuration;
    }

    public override void Start(Unit unit)
    {
        base.Start(unit);
        startTime = Time.time;
        unit.StopMoving();
    }

    public override void Execute(Unit unit)
    {
        // 아무것도 안 함 - 그냥 대기
    }

    public override bool IsComplete(Unit unit)
    {
        return Time.time - startTime >= duration;
    }
}

// ==================== ★ 워크스테이션 작업 추가 ====================

/// <summary>
/// 워크스테이션 작업 (농경지, 작업장, 주방 등)
/// </summary>
public class WorkstationTask : UnitTask
{
    private IWorkstation workstation;
    private Building building;
    private float workInterval = 1f;
    private float lastWorkTime;
    private float workRange = 1.5f;
    private bool isWorkStarted = false;

    public WorkstationTask(IWorkstation ws, TaskPriority priority = TaskPriority.Normal)
    {
        Type = TaskType.Workstation;
        Priority = priority;
        workstation = ws;

        var component = ws as MonoBehaviour;
        if (component != null)
        {
            TargetObject = component.gameObject;
            TargetPosition = ws.WorkPoint?.position ?? component.transform.position;
            building = component.GetComponent<Building>();
        }

        Debug.Log($"[WorkstationTask] 워크스테이션 작업 생성: {ws.TaskType}");
    }

    public override void Start(Unit unit)
    {
        base.Start(unit);

        if (!workstation.AssignWorker(unit))
        {
            Debug.LogWarning($"[WorkstationTask] 작업자 배정 실패!");
            State = TaskState.Failed;
            return;
        }

        Debug.Log($"[WorkstationTask] {unit.UnitName} 워크스테이션 작업 시작: {workstation.TaskType}");
    }

    public override void Execute(Unit unit)
    {
        if (workstation == null)
        {
            State = TaskState.Failed;
            return;
        }

        float distance = Vector3.Distance(unit.transform.position, TargetPosition);

        // 작업 위치로 이동
        if (distance > workRange)
        {
            unit.MoveTo(TargetPosition);
            return;
        }

        // 작업 시작
        if (!isWorkStarted)
        {
            workstation.StartWork();
            isWorkStarted = true;
        }

        // 작업 수행
        if (Time.time - lastWorkTime >= workInterval)
        {
            float workAmount = unit.DoWork();
            workstation.DoWork(workAmount);
            lastWorkTime = Time.time;
        }
    }

    public override bool IsComplete(Unit unit)
    {
        if (workstation == null)
            return true;

        // 작업 완료 체크
        if (!workstation.CanStartWork && !workstation.IsOccupied)
            return true;

        // 작업 중이 아니면 완료
        var component = workstation as WorkstationComponent;
        if (component != null && !component.IsWorking && isWorkStarted)
            return true;

        return false;
    }

    public override void Complete(Unit unit)
    {
        base.Complete(unit);
        workstation?.ReleaseWorker();
        Debug.Log($"[WorkstationTask] {unit.UnitName} 워크스테이션 작업 완료!");
    }

    public override void Cancel()
    {
        base.Cancel();
        workstation?.CancelWork();
        workstation?.ReleaseWorker();
    }
}