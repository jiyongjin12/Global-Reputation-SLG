using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 작업 타입
/// </summary>
public enum TaskType
{
    Idle,           // 대기
    MoveTo,         // 이동
    Construct,      // 건설
    Harvest,        // 채집 (나무, 돌 등)
    PickupItem,     // 아이템 줍기
    DeliverToStorage, // 창고에 저장
    Eat,            // 음식 먹기
    Attack,         // 공격
    Flee            // 도망
}

/// <summary>
/// 작업 우선순위
/// </summary>
public enum TaskPriority
{
    Critical = 0,   // 생존 (먹기, 도망)
    High = 1,       // 중요 작업
    Normal = 2,     // 일반 작업
    Low = 3         // 낮은 우선순위
}

/// <summary>
/// 작업 상태
/// </summary>
public enum TaskState
{
    Pending,        // 대기 중
    InProgress,     // 진행 중
    Completed,      // 완료
    Failed,         // 실패
    Cancelled       // 취소됨
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

    // 이벤트
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
        // 도착 안 했으면 계속 이동 명령 (경로 재설정)
        if (!IsComplete(unit))
        {
            // 이미 이동 중이면 다시 호출 안 함
            if (!unit.Agent.hasPath || unit.Agent.remainingDistance < 0.1f)
            {
                unit.MoveTo(TargetPosition);
            }
        }
    }

    public override bool IsComplete(Unit unit)
    {
        // 방법 1: NavMeshAgent 기반 확인
        if (unit.HasArrivedAtDestination())
        {
            Debug.Log($"[MoveToTask] {unit.UnitName} 도착! (NavMesh 기준)");
            return true;
        }

        // 방법 2: 수평 거리 기반 확인 (백업)
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
    private float workRange = 2f;  // 작업 가능 거리

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

        // 거리 체크 - 너무 멀면 작업 안 함
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

            // 특성에 따른 보너스 체크
            // TODO: 특성 시스템과 연동

            var drops = targetNode.Harvest(gatherPower);

            // 드롭된 아이템을 바로 줍기 (인벤토리에 공간 있으면)
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