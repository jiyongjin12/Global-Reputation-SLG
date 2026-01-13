using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum MovementStyle { Direct, Natural, Relaxed, Urgent }

/// <summary>
/// 자연스러운 유닛 이동 시스템
/// ★ 기본은 걷기만, 달리기는 위험 상황에서만 사용
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class UnitMovement : MonoBehaviour
{
    [Header("=== 속도 설정 ===")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float strollSpeed = 1.2f;
    [SerializeField] private float urgentSpeed = 4f;

    [Header("=== 자연스러운 이동 ===")]
    [SerializeField] private bool enableNaturalMovement = true;
    [SerializeField] private float pathDeviationChance = 0.3f;
    [SerializeField] private float maxDeviation = 2f;
    [SerializeField] private float deviationCheckInterval = 1.5f;

    [Header("=== 속도 변화 ===")]
    [SerializeField] private bool enableSpeedVariation = true;
    [SerializeField] private float speedVariationAmount = 0.2f;
    [SerializeField] private float speedChangeInterval = 2f;

    [Header("=== 멈춤/둘러보기 ===")]
    [SerializeField] private bool enableRandomPauses = true;
    [SerializeField] private float pauseChance = 0.08f;
    [SerializeField] private float minPauseDuration = 0.3f;
    [SerializeField] private float maxPauseDuration = 1.2f;
    [SerializeField] private bool enableLookAround = true;
    [SerializeField] private float lookAroundChance = 0.15f;
    [SerializeField] private float lookAroundDuration = 0.8f;

    [Header("=== 디버그 ===")]
    [SerializeField] private MovementStyle currentStyle = MovementStyle.Natural;
    [SerializeField] private bool isMoving = false;
    [SerializeField] private bool isPaused = false;

    private NavMeshAgent agent;
    private Vector3 finalDestination;
    private List<Vector3> waypoints = new();
    private int currentWaypointIndex;
    private float lastDeviationCheck, lastSpeedChange, pauseEndTime, lookAroundEndTime;
    private float currentBaseSpeed;

    public bool IsMoving => isMoving;
    public bool IsPaused => isPaused;
    public MovementStyle CurrentStyle => currentStyle;
    public Vector3 Destination => finalDestination;
    public float CurrentSpeed => agent?.speed ?? walkSpeed;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        currentBaseSpeed = walkSpeed;
    }

    private void Update()
    {
        if (!isMoving) return;

        if (isPaused)
        {
            if (Time.time >= pauseEndTime) ResumeMovement();
            return;
        }

        if (Time.time < lookAroundEndTime) return;

        if (HasArrivedAtWaypoint()) OnWaypointReached();

        if (enableNaturalMovement && currentStyle != MovementStyle.Direct && currentStyle != MovementStyle.Urgent)
            ProcessNaturalMovement();

        if (enableSpeedVariation && (currentStyle == MovementStyle.Natural || currentStyle == MovementStyle.Relaxed))
            ProcessSpeedVariation();
    }

    public void MoveTo(Vector3 destination, MovementStyle style = MovementStyle.Natural)
    {
        finalDestination = destination;
        currentStyle = style;
        isMoving = true;
        isPaused = false;

        ApplyMovementStyle(style);

        waypoints.Clear();
        if (enableNaturalMovement && style != MovementStyle.Direct && style != MovementStyle.Urgent)
            GenerateWaypoints(destination);
        else
            waypoints.Add(destination);

        currentWaypointIndex = 0;
        MoveToNextWaypoint();
    }

    public void WalkTo(Vector3 destination) => MoveTo(destination, MovementStyle.Natural);
    public void RunTo(Vector3 destination) => MoveTo(destination, MovementStyle.Urgent);
    public void StrollTo(Vector3 destination) => MoveTo(destination, MovementStyle.Relaxed);

    public void Stop()
    {
        isMoving = isPaused = false;
        agent.ResetPath();
        waypoints.Clear();
        agent.speed = walkSpeed;
    }

    public bool HasArrived()
    {
        if (!isMoving) return true;
        if (agent.pathPending) return false;
        return agent.remainingDistance <= agent.stoppingDistance &&
               (!agent.hasPath || agent.velocity.sqrMagnitude < 0.1f);
    }

    public void SetSpeed(float speed) => agent.speed = currentBaseSpeed = speed;
    public void SetMovementStyle(MovementStyle style) { currentStyle = style; ApplyMovementStyle(style); }

    private void ApplyMovementStyle(MovementStyle style)
    {
        (agent.speed, agent.angularSpeed, agent.acceleration, currentBaseSpeed) = style switch
        {
            MovementStyle.Direct => (walkSpeed, 360f, 10f, walkSpeed),
            MovementStyle.Natural => (walkSpeed, 180f, 6f, walkSpeed),
            MovementStyle.Relaxed => (strollSpeed, 120f, 4f, strollSpeed),
            MovementStyle.Urgent => (urgentSpeed, 360f, 16f, urgentSpeed),
            _ => (walkSpeed, 180f, 6f, walkSpeed)
        };
    }

    private void GenerateWaypoints(Vector3 destination)
    {
        Vector3 start = transform.position;
        float totalDistance = Vector3.Distance(start, destination);

        if (totalDistance < 5f) { waypoints.Add(destination); return; }

        int waypointCount = Mathf.Clamp((int)(totalDistance / 8f), 1, 4);
        Vector3 direction = (destination - start).normalized;
        Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

        for (int i = 1; i <= waypointCount; i++)
        {
            float t = (float)i / (waypointCount + 1);
            Vector3 basePoint = Vector3.Lerp(start, destination, t);
            float offsetAmount = Random.Range(-maxDeviation, maxDeviation) * (currentStyle == MovementStyle.Relaxed ? 1.5f : 1f);
            Vector3 waypoint = basePoint + perpendicular * offsetAmount;

            waypoints.Add(NavMesh.SamplePosition(waypoint, out NavMeshHit hit, maxDeviation * 2f, NavMesh.AllAreas)
                ? hit.position : basePoint);
        }
        waypoints.Add(destination);
    }

    private void MoveToNextWaypoint()
    {
        if (currentWaypointIndex >= waypoints.Count) { OnDestinationReached(); return; }

        agent.SetDestination(waypoints[currentWaypointIndex]);

        if (enableRandomPauses && currentStyle != MovementStyle.Urgent &&
            Random.value < pauseChance && currentWaypointIndex > 0)
            StartPause();
    }

    private bool HasArrivedAtWaypoint()
    {
        if (agent.pathPending) return false;
        float threshold = currentWaypointIndex == waypoints.Count - 1 ? 0.5f : 1.5f;
        return agent.remainingDistance <= threshold;
    }

    private void OnWaypointReached()
    {
        currentWaypointIndex++;
        if (enableLookAround && currentStyle != MovementStyle.Urgent &&
            Random.value < lookAroundChance && currentWaypointIndex < waypoints.Count)
            StartLookAround();
        MoveToNextWaypoint();
    }

    private void OnDestinationReached()
    {
        isMoving = false;
        waypoints.Clear();
        agent.speed = walkSpeed;
    }

    private void ProcessNaturalMovement()
    {
        if (Time.time - lastDeviationCheck < deviationCheckInterval) return;
        lastDeviationCheck = Time.time;

        if (Random.value < pathDeviationChance && waypoints.Count > 0 && currentWaypointIndex < waypoints.Count - 1)
        {
            Vector3 currentDest = agent.destination;
            Vector3 perpendicular = Vector3.Cross((currentDest - transform.position).normalized, Vector3.up);
            Vector3 newDest = currentDest + perpendicular * Random.Range(-maxDeviation * 0.5f, maxDeviation * 0.5f);

            if (NavMesh.SamplePosition(newDest, out NavMeshHit hit, maxDeviation, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
        }
    }

    private void ProcessSpeedVariation()
    {
        if (Time.time - lastSpeedChange < speedChangeInterval) return;
        lastSpeedChange = Time.time;
        agent.speed = currentBaseSpeed * (1f + Random.Range(-speedVariationAmount, speedVariationAmount));
    }

    private void StartPause()
    {
        isPaused = true;
        agent.isStopped = true;
        pauseEndTime = Time.time + Random.Range(minPauseDuration, maxPauseDuration);
    }

    private void ResumeMovement()
    {
        isPaused = false;
        agent.isStopped = false;
    }

    private void StartLookAround()
    {
        lookAroundEndTime = Time.time + lookAroundDuration;
        agent.isStopped = true;
        StartCoroutine(LookAroundCoroutine());
    }

    private IEnumerator LookAroundCoroutine()
    {
        float duration = lookAroundDuration;
        Quaternion startRot = transform.rotation;
        Quaternion targetRot = startRot * Quaternion.Euler(0, Random.Range(-90f, 90f), 0);

        for (float t = 0; t < duration * 0.5f; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(startRot, targetRot, t / (duration * 0.5f));
            yield return null;
        }

        yield return new WaitForSeconds(duration * 0.3f);

        if (waypoints.Count > currentWaypointIndex)
        {
            Vector3 dir = (waypoints[currentWaypointIndex] - transform.position).normalized;
            if (dir != Vector3.zero) targetRot = Quaternion.LookRotation(dir);
        }

        startRot = transform.rotation;
        for (float t = 0; t < duration * 0.2f; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(startRot, targetRot, t / (duration * 0.2f));
            yield return null;
        }

        agent.isStopped = false;
    }
}