using UnityEngine;
using System;

/// <summary>
/// 워크스테이션 기본 컴포넌트
/// - IWorkstation 기본 구현
/// - 작업자 관리, 작업 진행
/// - 상속하여 특화된 워크스테이션 구현
/// </summary>
public class WorkstationComponent : MonoBehaviour, IWorkstation
{
    [Header("=== 워크스테이션 설정 ===")]
    [SerializeField] protected WorkTaskType taskType = WorkTaskType.Crafting;
    [SerializeField] protected float baseWorkTime = 5f;

    [Header("=== 참조 ===")]
    [SerializeField] protected Transform workPoint;

    [Header("=== 상태 (읽기 전용) ===")]
    [SerializeField] protected bool isOccupied = false;
    [SerializeField] protected bool isWorking = false;
    [SerializeField] protected float workProgress = 0f;
    [SerializeField] protected float currentWorkDone = 0f;

    // 내부 상태
    protected Unit currentWorker;
    protected Building building;
    protected float currentWorkRequired;

    // Properties
    public WorkTaskType TaskType => taskType;
    public bool IsOccupied => isOccupied;
    public Transform WorkPoint => workPoint ?? building?.WorkPoint;
    public Unit CurrentWorker => currentWorker;
    public float WorkProgress => workProgress;
    public bool IsWorking => isWorking;

    // IWorkstation 구현
    public virtual bool CanStartWork => !isOccupied && HasPendingWork();

    // 이벤트
    public event Action<IWorkstation> OnWorkCompleted;
    public event Action<IWorkstation> OnWorkAvailable;
    public event Action<IWorkstation> OnWorkerAssigned;
    public event Action<IWorkstation> OnWorkerReleased;

    protected virtual void Awake()
    {
        building = GetComponent<Building>();
    }

    protected virtual void Start()
    {
        if (workPoint == null && building != null)
        {
            workPoint = building.WorkPoint;
        }
    }

    // ==================== IWorkstation 구현 ====================

    public virtual bool AssignWorker(Unit worker)
    {
        if (isOccupied || worker == null)
            return false;

        currentWorker = worker;
        isOccupied = true;

        Debug.Log($"[Workstation] {building?.Data?.Name}: 작업자 배정 - {worker.UnitName}");

        OnWorkerAssigned?.Invoke(this);
        return true;
    }

    public virtual void ReleaseWorker()
    {
        if (currentWorker == null)
            return;

        Debug.Log($"[Workstation] {building?.Data?.Name}: 작업자 해제 - {currentWorker.UnitName}");

        currentWorker = null;
        isOccupied = false;
        isWorking = false;
        workProgress = 0f;
        currentWorkDone = 0f;

        OnWorkerReleased?.Invoke(this);

        if (HasPendingWork())
        {
            OnWorkAvailable?.Invoke(this);
        }
    }

    public virtual void StartWork()
    {
        if (!isOccupied || currentWorker == null)
        {
            Debug.LogWarning($"[Workstation] 작업 시작 실패: 작업자 없음");
            return;
        }

        if (!HasPendingWork())
        {
            Debug.LogWarning($"[Workstation] 작업 시작 실패: 대기 작업 없음");
            return;
        }

        isWorking = true;
        workProgress = 0f;
        currentWorkDone = 0f;
        currentWorkRequired = GetWorkTime();

        OnWorkStarted();

        Debug.Log($"[Workstation] {building?.Data?.Name}: 작업 시작 (소요시간: {currentWorkRequired}초)");
    }

    /// <summary>작업 진행 (Unit이 호출)</summary>
    public virtual float DoWork(float workAmount)
    {
        if (!isWorking || !isOccupied)
            return 0f;

        currentWorkDone += workAmount;

        if (currentWorkRequired > 0)
            workProgress = Mathf.Clamp01(currentWorkDone / currentWorkRequired);
        else
            workProgress = 1f;

        if (workProgress >= 1f)
        {
            CompleteWork();
        }

        return workAmount;
    }

    public virtual void CompleteWork()
    {
        if (!isWorking)
            return;

        Debug.Log($"[Workstation] {building?.Data?.Name}: 작업 완료!");

        OnWorkFinished();

        isWorking = false;
        workProgress = 0f;
        currentWorkDone = 0f;

        OnWorkCompleted?.Invoke(this);

        if (HasPendingWork())
        {
            OnWorkAvailable?.Invoke(this);
        }
    }

    public virtual void CancelWork()
    {
        if (!isWorking)
            return;

        Debug.Log($"[Workstation] {building?.Data?.Name}: 작업 취소");

        OnWorkCancelled();

        isWorking = false;
        workProgress = 0f;
        currentWorkDone = 0f;
    }

    // ==================== 가상 메서드 (상속용) ====================

    protected virtual bool HasPendingWork()
    {
        return false;
    }

    protected virtual float GetWorkTime()
    {
        return baseWorkTime;
    }

    protected virtual void OnWorkStarted()
    {
    }

    protected virtual void OnWorkFinished()
    {
    }

    protected virtual void OnWorkCancelled()
    {
    }

    // ==================== 유틸리티 ====================

    protected void NotifyWorkAvailable()
    {
        OnWorkAvailable?.Invoke(this);
        TaskManager.Instance?.AddWorkstationTask(this);
    }

#if UNITY_EDITOR
    protected virtual void OnDrawGizmosSelected()
    {
        Transform wp = workPoint ?? GetComponent<Building>()?.WorkPoint;
        if (wp != null)
        {
            Gizmos.color = isOccupied ? Color.red : Color.green;
            Gizmos.DrawWireSphere(wp.position, 0.4f);

            if (isWorking)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(wp.position + Vector3.up, Vector3.one * 0.3f);
            }
        }
    }
#endif
}