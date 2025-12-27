using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 유닛 이동 스타일
/// </summary>
public enum MovementStyle
{
    Direct,         // 직선 이동 (작업 시)
    Natural,        // 자연스러운 이동 (일반)
    Relaxed,        // 느긋한 이동 (서성이기)
    Urgent          // 급한 이동 (배고픔, 위험)
}

/// <summary>
/// 자연스러운 유닛 이동 시스템
/// NavMeshAgent와 함께 사용하여 변칙적인 움직임 구현
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class UnitMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float baseSpeed = 3.5f;
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float strollSpeed = 1.5f;      // 산책 속도

    [Header("Natural Movement")]
    [SerializeField] private bool enableNaturalMovement = true;
    [SerializeField] private float pathDeviationChance = 0.3f;      // 경로 이탈 확률
    [SerializeField] private float maxDeviation = 2f;               // 최대 이탈 거리
    [SerializeField] private float deviationCheckInterval = 1.5f;   // 이탈 체크 주기

    [Header("Speed Variation")]
    [SerializeField] private bool enableSpeedVariation = true;
    [SerializeField] private float speedVariationAmount = 0.3f;     // 속도 변화량 (30%)
    [SerializeField] private float speedChangeInterval = 2f;        // 속도 변경 주기

    [Header("Pause Behavior")]
    [SerializeField] private bool enableRandomPauses = true;
    [SerializeField] private float pauseChance = 0.1f;              // 멈춤 확률
    [SerializeField] private float minPauseDuration = 0.3f;
    [SerializeField] private float maxPauseDuration = 1.5f;

    [Header("Look Around")]
    [SerializeField] private bool enableLookAround = true;
    [SerializeField] private float lookAroundChance = 0.2f;         // 주변 둘러보기 확률
    [SerializeField] private float lookAroundDuration = 1f;

    [Header("Debug")]
    [SerializeField] private MovementStyle currentStyle = MovementStyle.Natural;
    [SerializeField] private bool isMoving = false;
    [SerializeField] private bool isPaused = false;

    // 컴포넌트
    private NavMeshAgent agent;
    private Unit unit;

    // 상태
    private Vector3 finalDestination;
    private Vector3 currentWaypoint;
    private List<Vector3> waypoints = new List<Vector3>();
    private int currentWaypointIndex = 0;

    // 타이머
    private float lastDeviationCheck;
    private float lastSpeedChange;
    private float pauseEndTime;
    private float lookAroundEndTime;

    // 원래 설정 저장
    private float originalSpeed;
    private float originalAngularSpeed;

    // Properties
    public bool IsMoving => isMoving;
    public bool IsPaused => isPaused;
    public MovementStyle CurrentStyle => currentStyle;
    public Vector3 Destination => finalDestination;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        unit = GetComponent<Unit>();

        originalSpeed = agent.speed;
        originalAngularSpeed = agent.angularSpeed;
    }

    private void Update()
    {
        if (!isMoving) return;

        // 일시 정지 중
        if (isPaused)
        {
            if (Time.time >= pauseEndTime)
            {
                ResumMovement();
            }
            return;
        }

        // 주변 둘러보기 중
        if (Time.time < lookAroundEndTime)
        {
            return;
        }

        // 도착 확인
        if (HasArrivedAtWaypoint())
        {
            OnWaypointReached();
        }

        // Natural 움직임 처리
        if (enableNaturalMovement && currentStyle != MovementStyle.Direct)
        {
            ProcessNaturalMovement();
        }

        // 속도 변화
        if (enableSpeedVariation && currentStyle == MovementStyle.Natural)
        {
            ProcessSpeedVariation();
        }
    }

    /// <summary>
    /// 목적지로 이동 시작
    /// </summary>
    public void MoveTo(Vector3 destination, MovementStyle style = MovementStyle.Natural)
    {
        finalDestination = destination;
        currentStyle = style;
        isMoving = true;
        isPaused = false;

        // 스타일에 따른 설정
        ApplyMovementStyle(style);

        // 웨이포인트 생성
        if (enableNaturalMovement && style != MovementStyle.Direct)
        {
            GenerateWaypoints(destination);
        }
        else
        {
            waypoints.Clear();
            waypoints.Add(destination);
        }

        currentWaypointIndex = 0;
        MoveToNextWaypoint();
    }

    /// <summary>
    /// 이동 중지
    /// </summary>
    public void Stop()
    {
        isMoving = false;
        isPaused = false;
        agent.ResetPath();
        waypoints.Clear();
    }

    /// <summary>
    /// 이동 스타일에 따른 설정 적용
    /// </summary>
    private void ApplyMovementStyle(MovementStyle style)
    {
        switch (style)
        {
            case MovementStyle.Direct:
                agent.speed = baseSpeed;
                agent.angularSpeed = 360f;
                agent.acceleration = 12f;
                break;

            case MovementStyle.Natural:
                agent.speed = baseSpeed;
                agent.angularSpeed = 180f;
                agent.acceleration = 8f;
                break;

            case MovementStyle.Relaxed:
                agent.speed = strollSpeed;
                agent.angularSpeed = 120f;
                agent.acceleration = 4f;
                break;

            case MovementStyle.Urgent:
                agent.speed = runSpeed;
                agent.angularSpeed = 360f;
                agent.acceleration = 16f;
                break;
        }
    }

    /// <summary>
    /// 중간 웨이포인트 생성 (자연스러운 경로)
    /// </summary>
    private void GenerateWaypoints(Vector3 destination)
    {
        waypoints.Clear();

        Vector3 start = transform.position;
        float totalDistance = Vector3.Distance(start, destination);

        // 거리가 짧으면 웨이포인트 없이 직접 이동
        if (totalDistance < 5f)
        {
            waypoints.Add(destination);
            return;
        }

        // 중간 웨이포인트 개수 결정 (5~15 유닛당 1개)
        int waypointCount = Mathf.Clamp((int)(totalDistance / 8f), 1, 4);

        Vector3 direction = (destination - start).normalized;
        Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

        for (int i = 1; i <= waypointCount; i++)
        {
            float t = (float)i / (waypointCount + 1);
            Vector3 basePoint = Vector3.Lerp(start, destination, t);

            // 랜덤 오프셋 추가
            float offsetAmount = Random.Range(-maxDeviation, maxDeviation);

            // 스타일에 따라 오프셋 조절
            if (currentStyle == MovementStyle.Relaxed)
            {
                offsetAmount *= 1.5f;  // 더 많이 돌아다님
            }

            Vector3 offset = perpendicular * offsetAmount;
            Vector3 waypoint = basePoint + offset;

            // NavMesh에서 유효한 위치 찾기
            if (NavMesh.SamplePosition(waypoint, out NavMeshHit hit, maxDeviation * 2f, NavMesh.AllAreas))
            {
                waypoints.Add(hit.position);
            }
            else
            {
                waypoints.Add(basePoint);  // 유효한 위치 없으면 직선 경로
            }
        }

        // 최종 목적지 추가
        waypoints.Add(destination);
    }

    /// <summary>
    /// 다음 웨이포인트로 이동
    /// </summary>
    private void MoveToNextWaypoint()
    {
        if (currentWaypointIndex >= waypoints.Count)
        {
            OnDestinationReached();
            return;
        }

        currentWaypoint = waypoints[currentWaypointIndex];
        agent.SetDestination(currentWaypoint);

        // 가끔 멈춤
        if (enableRandomPauses && Random.value < pauseChance && currentWaypointIndex > 0)
        {
            StartPause();
        }
    }

    /// <summary>
    /// 웨이포인트 도착 확인
    /// </summary>
    private bool HasArrivedAtWaypoint()
    {
        if (agent.pathPending) return false;

        float threshold = (currentWaypointIndex == waypoints.Count - 1) ? 0.5f : 1.5f;
        return agent.remainingDistance <= threshold;
    }

    /// <summary>
    /// 웨이포인트 도착 시
    /// </summary>
    private void OnWaypointReached()
    {
        currentWaypointIndex++;

        // 가끔 주변 둘러보기
        if (enableLookAround && Random.value < lookAroundChance && currentWaypointIndex < waypoints.Count)
        {
            StartLookAround();
        }

        MoveToNextWaypoint();
    }

    /// <summary>
    /// 최종 목적지 도착
    /// </summary>
    private void OnDestinationReached()
    {
        isMoving = false;
        waypoints.Clear();
    }

    /// <summary>
    /// 자연스러운 움직임 처리
    /// </summary>
    private void ProcessNaturalMovement()
    {
        if (Time.time - lastDeviationCheck < deviationCheckInterval) return;
        lastDeviationCheck = Time.time;

        // 경로 이탈 (가끔 약간 다른 방향으로)
        if (Random.value < pathDeviationChance && waypoints.Count > 0 && currentWaypointIndex < waypoints.Count - 1)
        {
            Vector3 currentDest = agent.destination;
            Vector3 perpendicular = Vector3.Cross((currentDest - transform.position).normalized, Vector3.up);

            float deviation = Random.Range(-maxDeviation * 0.5f, maxDeviation * 0.5f);
            Vector3 newDest = currentDest + perpendicular * deviation;

            if (NavMesh.SamplePosition(newDest, out NavMeshHit hit, maxDeviation, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }
    }

    /// <summary>
    /// 속도 변화 처리
    /// </summary>
    private void ProcessSpeedVariation()
    {
        if (Time.time - lastSpeedChange < speedChangeInterval) return;
        lastSpeedChange = Time.time;

        float variation = 1f + Random.Range(-speedVariationAmount, speedVariationAmount);
        agent.speed = baseSpeed * variation;
    }

    /// <summary>
    /// 잠시 멈춤
    /// </summary>
    private void StartPause()
    {
        isPaused = true;
        agent.isStopped = true;
        pauseEndTime = Time.time + Random.Range(minPauseDuration, maxPauseDuration);
    }

    /// <summary>
    /// 멈춤 해제
    /// </summary>
    private void ResumMovement()
    {
        isPaused = false;
        agent.isStopped = false;
    }

    /// <summary>
    /// 주변 둘러보기
    /// </summary>
    private void StartLookAround()
    {
        lookAroundEndTime = Time.time + lookAroundDuration;
        agent.isStopped = true;

        // 랜덤 방향 바라보기
        StartCoroutine(LookAroundCoroutine());
    }

    private IEnumerator LookAroundCoroutine()
    {
        float duration = lookAroundDuration;
        float elapsed = 0f;

        // 랜덤 방향
        float randomAngle = Random.Range(-90f, 90f);
        Quaternion targetRot = transform.rotation * Quaternion.Euler(0, randomAngle, 0);
        Quaternion startRot = transform.rotation;

        while (elapsed < duration * 0.5f)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(startRot, targetRot, elapsed / (duration * 0.5f));
            yield return null;
        }

        // 잠시 대기
        yield return new WaitForSeconds(duration * 0.3f);

        // 원래 방향으로 (또는 목적지 방향)
        if (waypoints.Count > currentWaypointIndex)
        {
            Vector3 dirToWaypoint = (waypoints[currentWaypointIndex] - transform.position).normalized;
            targetRot = Quaternion.LookRotation(dirToWaypoint);
        }

        elapsed = 0f;
        startRot = transform.rotation;
        while (elapsed < duration * 0.2f)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(startRot, targetRot, elapsed / (duration * 0.2f));
            yield return null;
        }

        agent.isStopped = false;
    }

    /// <summary>
    /// 도착 확인 (외부용)
    /// </summary>
    public bool HasArrived()
    {
        if (!isMoving) return true;
        if (agent.pathPending) return false;

        return agent.remainingDistance <= agent.stoppingDistance &&
               (!agent.hasPath || agent.velocity.sqrMagnitude < 0.1f);
    }

    /// <summary>
    /// 속도 직접 설정
    /// </summary>
    public void SetSpeed(float speed)
    {
        agent.speed = speed;
        baseSpeed = speed;
    }

    /// <summary>
    /// 이동 스타일 변경
    /// </summary>
    public void SetMovementStyle(MovementStyle style)
    {
        currentStyle = style;
        ApplyMovementStyle(style);
    }
}