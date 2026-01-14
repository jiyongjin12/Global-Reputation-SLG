using UnityEngine;

/// <summary>
/// 유닛 AI - 수면 관련
/// </summary>
public partial class UnitAI
{
    /// <summary>
    /// 밤인지 체크
    /// </summary>
    protected bool IsNightTime()
    {
        return CycleManager.Instance != null && CycleManager.Instance.IsNight;
    }

    /// <summary>
    /// 수면이 필요한지 체크
    /// </summary>
    protected bool NeedsSleep()
    {
        // 밤이고, 수면 중이 아니고, 플레이어 명령이 없을 때
        return IsNightTime() &&
               !isSleeping &&
               !bb.HasPlayerCommand &&
               currentBehavior != AIBehaviorState.Sleeping &&
               currentBehavior != AIBehaviorState.GoingToSleep;
    }

    /// <summary>
    /// 수면 시도
    /// </summary>
    protected bool TryGoToSleep()
    {
        // 이미 수면 중이거나 이동 중이면 스킵
        if (isSleeping || isGoingToSleep) return false;

        // 침대 찾기
        targetBed = unit.AssignedBed;

        if (targetBed != null)
        {
            // 침대가 있으면 침대로 이동
            return StartGoingToBed();
        }
        else
        {
            // 침대가 없으면 노숙
            return StartHomelessSleep();
        }
    }

    /// <summary>
    /// 침대로 이동 시작
    /// </summary>
    private bool StartGoingToBed()
    {
        if (targetBed == null || !targetBed.CanStartSleep(unit))
        {
            targetBed = null;
            return false;
        }

        isGoingToSleep = true;
        unit.MoveTo(targetBed.SleepPosition);
        SetBehaviorAndPriority(AIBehaviorState.GoingToSleep, TaskPriorityLevel.Sleep);

        Debug.Log($"[UnitAI] {unit.UnitName}: 침대로 이동 중");
        return true;
    }

    /// <summary>
    /// 노숙 시작 (침대 없음)
    /// </summary>
    private bool StartHomelessSleep()
    {
        // 제자리에서 노숙
        isSleeping = true;
        isGoingToSleep = false;

        // 노숙 페널티 적용
        BedManager.Instance?.ApplyHomelessPenalty(unit);

        SetBehaviorAndPriority(AIBehaviorState.Sleeping, TaskPriorityLevel.Sleep);

        Debug.Log($"[UnitAI] {unit.UnitName}: 노숙 시작 (침대 없음)");
        return true;
    }

    /// <summary>
    /// 침대로 이동 업데이트
    /// </summary>
    protected void UpdateGoingToSleep()
    {
        // 침대가 없어졌으면 노숙
        if (targetBed == null)
        {
            isGoingToSleep = false;
            StartHomelessSleep();
            return;
        }

        // 낮이 되면 수면 취소
        if (!IsNightTime())
        {
            CancelSleep();
            return;
        }

        // 침대 도착 체크
        if (targetBed.IsInSleepRange(transform.position) || unit.HasArrivedAtDestination())
        {
            // 수면 시작
            if (targetBed.StartSleep(unit))
            {
                isGoingToSleep = false;
                isSleeping = true;
                unit.StopMoving();
                SetBehaviorAndPriority(AIBehaviorState.Sleeping, TaskPriorityLevel.Sleep);

                Debug.Log($"[UnitAI] {unit.UnitName}: 수면 시작");
            }
            else
            {
                // 침대 사용 불가 → 노숙
                isGoingToSleep = false;
                targetBed = null;
                StartHomelessSleep();
            }
        }
    }

    /// <summary>
    /// 수면 중 업데이트
    /// </summary>
    protected void UpdateSleeping()
    {
        // 낮이 되면 기상
        if (!IsNightTime())
        {
            WakeUp(false);
            return;
        }

        // 플레이어 명령이 들어오면 (SetWaitingForCommand에서 처리됨)
        // 여기서는 수면 상태만 유지
    }

    /// <summary>
    /// 기상
    /// </summary>
    /// <param name="interrupted">강제 기상 여부 (페널티 적용)</param>
    public void WakeUp(bool interrupted)
    {
        if (!isSleeping && !isGoingToSleep) return;

        // 침대에서 기상
        if (targetBed != null && targetBed.IsOccupied && targetBed.CurrentSleeper == unit)
        {
            if (interrupted)
                targetBed.InterruptSleep(true);  // 페널티 적용
            else
                targetBed.CompleteSleep();
        }

        isSleeping = false;
        isGoingToSleep = false;
        targetBed = null;

        SetBehaviorAndPriority(AIBehaviorState.Idle, TaskPriorityLevel.FreeWill);

        Debug.Log($"[UnitAI] {unit.UnitName}: 기상" + (interrupted ? " (강제)" : ""));
    }

    /// <summary>
    /// 수면 취소 (명령 등으로)
    /// </summary>
    protected void CancelSleep()
    {
        if (!isSleeping && !isGoingToSleep) return;

        WakeUp(false);
    }

    /// <summary>
    /// 수면 중 플레이어 명령 처리 (강제 기상 + 페널티)
    /// </summary>
    protected void InterruptSleepForCommand()
    {
        if (isSleeping || isGoingToSleep)
        {
            WakeUp(true);  // 페널티 적용
        }
    }
}