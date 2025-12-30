using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 바닥에 드롭된 아이템
/// - 자원 파괴 시 튀어나오는 애니메이션
/// - 바닥에서 통통 튀는 효과 (스케일은 Y위치에 비례)
/// - 자석 흡수는 옵션 (특성용)
/// </summary>
public class DroppedItem : MonoBehaviour
{
    [Header("=== 자원 정보 ===")]
    [Tooltip("드롭된 자원 아이템 데이터")]
    [SerializeField] private ResourceItemSO resource;

    [Tooltip("드롭된 수량")]
    [SerializeField] private int amount = 1;

    [Header("=== 발사 설정 ===")]
    [Tooltip("수평 방향으로 튀어나가는 힘")]
    [SerializeField] private float launchForce = 2.5f;

    [Tooltip("위로 튀어오르는 초기 속도")]
    [SerializeField] private float launchHeight = 4f;

    [Tooltip("중력 가속도")]
    [SerializeField] private float gravity = 15f;

    [Header("=== 바운스 설정 ===")]
    [Tooltip("바운스 시 속도 유지율 (0.6 = 60% 속도 유지)")]
    [Range(0.3f, 0.85f)]
    [SerializeField] private float bounceDecay = 0.6f;

    [Tooltip("최대 바운스 횟수")]
    [SerializeField] private int maxBounces = 3;

    [Tooltip("바닥 Y 좌표")]
    [SerializeField] private float groundY = 0.1f;

    [Tooltip("바운스 종료 판정 속도")]
    [SerializeField] private float minBounceVelocity = 1.5f;

    [Header("=== 스케일 설정 (속도 기반 탄성) ===")]
    [Tooltip("스폰 시 0→1 커지는 시간")]
    [SerializeField] private float spawnScaleDuration = 0.15f;

    [Tooltip("바닥 충돌/낙하 시 최대 납작해지는 정도 (0.5 = Y가 50%로)")]
    [Range(0.2f, 0.7f)]
    [SerializeField] private float maxSquashAmount = 0.5f;

    [Header("=== 자석 흡수 (특성용, 기본 OFF) ===")]
    [Tooltip("자석 흡수 기능 활성화 (특성으로 켜짐)")]
    [SerializeField] private bool enableMagnet = false;

    [Tooltip("자석 흡수 범위")]
    [SerializeField] private float magnetRadius = 3f;

    [Tooltip("윈드업 - 뒤로 빠지는 거리")]
    [SerializeField] private float windupBackDistance = 0.4f;

    [Tooltip("윈드업 - 위로 뜨는 높이")]
    [SerializeField] private float windupUpDistance = 0.6f;

    [Tooltip("윈드업 시간")]
    [SerializeField] private float windupDuration = 0.12f;

    [Tooltip("빨려들어가는 시간")]
    [SerializeField] private float magnetPullDuration = 0.2f;

    [Header("=== 상태 ===")]
    [SerializeField] private bool isAnimating = false;
    [SerializeField] private bool isBeingMagneted = false;

    // 내부 상태
    private bool isBeingCarried = false;
    private bool isReserved = false;
    private Unit reservedBy = null;
    private Vector3 originalScale;
    private Coroutine magnetCoroutine;

    // Properties
    public ResourceItemSO Resource => resource;
    public int Amount => amount;
    public bool IsAvailable => !isBeingCarried && !isReserved && !isAnimating && !isBeingMagneted;
    public bool IsReserved => isReserved;
    public Unit ReservedBy => reservedBy;
    public bool IsAnimating => isAnimating;

    // 이벤트
    public event Action<DroppedItem> OnPickedUp;
    public event Action<DroppedItem> OnAnimationComplete;

    private void Awake()
    {
        originalScale = transform.localScale;
    }

    private void Update()
    {
        // 자석 활성화 + 애니메이션 끝난 상태에서만 탐지
        if (enableMagnet && !isAnimating && !isBeingMagneted && !isBeingCarried)
        {
            TryFindMagnetTarget();
        }
    }

    public void Initialize(ResourceItemSO resourceItem, int itemAmount)
    {
        resource = resourceItem;
        amount = itemAmount;
        originalScale = transform.localScale;
    }

    /// <summary>
    /// 자석 기능 활성화/비활성화 (특성에서 호출)
    /// </summary>
    public void SetMagnetEnabled(bool enabled)
    {
        enableMagnet = enabled;
    }

    // ==================== 드랍 애니메이션 ====================

    /// <summary>
    /// 튀어나오는 애니메이션 시작
    /// </summary>
    public void PlayDropAnimation(Vector3 spawnPosition)
    {
        transform.position = spawnPosition;
        StartCoroutine(DropAnimationCoroutine());
    }

    private IEnumerator DropAnimationCoroutine()
    {
        isAnimating = true;

        // 스케일 0에서 시작
        transform.localScale = Vector3.zero;

        // 랜덤 방향으로 발사
        Vector2 randomDir = UnityEngine.Random.insideUnitCircle.normalized;
        float randomForce = UnityEngine.Random.Range(launchForce * 0.8f, launchForce * 1.2f);

        Vector3 velocity = new Vector3(
            randomDir.x * randomForce,
            launchHeight + UnityEngine.Random.Range(-0.5f, 0.5f),
            randomDir.y * randomForce
        );

        Vector3 position = transform.position;

        // ========== 1단계: 스케일 커지면서 발사 ==========
        float scaleTimer = 0f;
        while (scaleTimer < spawnScaleDuration)
        {
            scaleTimer += Time.deltaTime;
            float t = scaleTimer / spawnScaleDuration;
            float scale = EaseOutBack(t);
            transform.localScale = originalScale * scale;

            velocity.y -= gravity * Time.deltaTime;
            position += velocity * Time.deltaTime;
            transform.position = position;

            yield return null;
        }

        transform.localScale = originalScale;

        // ========== 2단계: 물리 바운스 ==========
        int bounceCount = 0;

        while (bounceCount < maxBounces)
        {
            // 낙하
            while (position.y > groundY)
            {
                velocity.y -= gravity * Time.deltaTime;
                position += velocity * Time.deltaTime;
                velocity.x *= 0.99f;
                velocity.z *= 0.99f;

                transform.position = position;

                // ★ 속도에 따른 스케일 (떨어질 때 납작)
                UpdateScaleByVelocity(velocity.y);

                yield return null;
            }

            // 바닥 도착 → 최대 스쿼시!
            position.y = groundY;
            transform.position = position;
            ApplyImpactSquash();

            // 바운스 종료 체크
            if (Mathf.Abs(velocity.y) < minBounceVelocity)
                break;

            // ★ 바운스! (위로 튀어오르기)
            float bounceVelocity = Mathf.Abs(velocity.y) * bounceDecay;
            velocity.y = bounceVelocity;
            velocity.x *= bounceDecay;
            velocity.z *= bounceDecay;

            bounceCount++;

            // ★ 실제 바운스 - 위로 올라갔다가 내려오기
            while (true)
            {
                velocity.y -= gravity * Time.deltaTime;
                position += velocity * Time.deltaTime;
                velocity.x *= 0.99f;
                velocity.z *= 0.99f;

                // 바닥 아래로 내려가지 않게
                if (position.y < groundY)
                    position.y = groundY;

                transform.position = position;

                // ★ 속도에 따른 스케일 (튀어오를 때 길쭉, 떨어질 때 납작)
                UpdateScaleByVelocity(velocity.y);

                yield return null;

                // 바닥에 닿고 속도가 아래로 향하면 다음 바운스로
                if (position.y <= groundY && velocity.y <= 0)
                    break;
            }
        }

        // ========== 3단계: 최종 정착 ==========
        position.y = groundY;
        transform.position = position;
        transform.localScale = originalScale; // 원래 스케일로 복원

        isAnimating = false;
        OnAnimationComplete?.Invoke(this);
    }

    /// <summary>
    ///  속도에 따른 스케일 업데이트 (탄성 효과)
    /// - 떨어질 때 (velocity < 0): 납작 (Y, XZ)
    /// - 튀어오를 때 (velocity > 0): 길쭉 (Y, XZ)
    /// - 정지 (velocity  0): 원래 스케일
    /// </summary>
    private void UpdateScaleByVelocity(float velocityY)
    {
        // 속도를 -1 ~ 1 범위로 정규화 (maxVelocity 기준)
        float maxVelocity = launchHeight; // 최대 속도 = 초기 발사 속도
        float normalizedVelocity = Mathf.Clamp(velocityY / maxVelocity, -1f, 1f);

        float scaleY;
        float scaleXZ;

        if (normalizedVelocity < 0)
        {
            // 떨어지는 중: 납작해짐 (Y 줄고, XZ 늘어남)
            float squash = Mathf.Abs(normalizedVelocity) * maxSquashAmount;
            scaleY = 1f - squash;           // 예: 1 - 0.5 = 0.5
            scaleXZ = 1f + squash * 0.6f;   // 예: 1 + 0.3 = 1.3
        }
        else if (normalizedVelocity > 0)
        {
            // 튀어오르는 중: 길쭉해짐 (Y 늘고, XZ 줄어듦)
            float stretch = normalizedVelocity * maxSquashAmount * 0.5f;
            scaleY = 1f + stretch;          // 예: 1 + 0.25 = 1.25
            scaleXZ = 1f - stretch * 0.4f;  // 예: 1 - 0.1 = 0.9
        }
        else
        {
            // 정지: 원래 스케일
            scaleY = 1f;
            scaleXZ = 1f;
        }

        transform.localScale = new Vector3(
            originalScale.x * scaleXZ,
            originalScale.y * scaleY,
            originalScale.z * scaleXZ
        );
    }

    /// <summary>
    /// 바닥 충돌 시 스쿼시 효과 (순간적으로 확 납작)
    /// </summary>
    private void ApplyImpactSquash()
    {
        float scaleY = 1f - maxSquashAmount;      // 최대 납작
        float scaleXZ = 1f + maxSquashAmount * 0.7f;

        transform.localScale = new Vector3(
            originalScale.x * scaleXZ,
            originalScale.y * scaleY,
            originalScale.z * scaleXZ
        );
    }

    // ==================== 자석 흡수 (특성용) ====================

    private void TryFindMagnetTarget()
    {
        if (!enableMagnet) return;

        var colliders = Physics.OverlapSphere(transform.position, magnetRadius);

        Unit nearestUnit = null;
        float nearestDist = magnetRadius;

        foreach (var col in colliders)
        {
            var unit = col.GetComponent<Unit>();
            if (unit == null || !unit.IsAlive || unit.Inventory.IsFull) continue;

            float dist = Vector3.Distance(transform.position, unit.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestUnit = unit;
            }
        }

        if (nearestUnit != null)
        {
            StartMagnetWithWindup(nearestUnit);
        }
    }

    private void StartMagnetWithWindup(Unit target)
    {
        isBeingMagneted = true;
        isReserved = true;
        reservedBy = target;
        magnetCoroutine = StartCoroutine(MagnetCoroutine(target));
    }

    private IEnumerator MagnetCoroutine(Unit target)
    {
        Vector3 startPos = transform.position;
        Vector3 targetPos = target.transform.position + Vector3.up * 0.5f;

        Vector3 awayDirection = (startPos - targetPos).normalized;
        awayDirection.y = 0;

        // 윈드업 (뒤로 빠지면서 위로)
        Vector3 windupTarget = startPos + awayDirection * windupBackDistance + Vector3.up * windupUpDistance;

        float timer = 0f;
        while (timer < windupDuration)
        {
            if (target == null || !target.IsAlive) { CancelMagnet(); yield break; }

            timer += Time.deltaTime;
            float t = timer / windupDuration;
            transform.position = Vector3.Lerp(startPos, windupTarget, EaseOutQuad(t));
            transform.localScale = originalScale * (1f + 0.15f * Mathf.Sin(t * Mathf.PI));

            yield return null;
        }

        // 빨려들어가기 (베지어 곡선)
        Vector3 pullStartPos = transform.position;
        timer = 0f;

        while (timer < magnetPullDuration)
        {
            if (target == null || !target.IsAlive || target.Inventory.IsFull) { CancelMagnet(); yield break; }

            timer += Time.deltaTime;
            float t = timer / magnetPullDuration;

            targetPos = target.transform.position + Vector3.up * 0.5f;
            float easedT = EaseInQuad(t);

            Vector3 midPoint = (pullStartPos + targetPos) / 2f + Vector3.up * 0.4f;
            transform.position = QuadraticBezier(pullStartPos, midPoint, targetPos, easedT);
            transform.localScale = originalScale * Mathf.Max(0.1f, 1f - 0.7f * easedT);

            yield return null;
        }

        // 인벤토리에 추가
        if (target != null && target.IsAlive && !target.Inventory.IsFull)
        {
            target.Inventory.AddItem(resource, amount);
            OnPickedUp?.Invoke(this);
            Destroy(gameObject);
        }
        else
        {
            CancelMagnet();
        }
    }

    private void CancelMagnet()
    {
        isBeingMagneted = false;
        isReserved = false;
        reservedBy = null;
        transform.localScale = originalScale;
    }

    // ==================== 기본 기능 ====================

    public bool Reserve(Unit unit)
    {
        if (!IsAvailable) return false;
        isReserved = true;
        reservedBy = unit;
        return true;
    }

    public void CancelReservation()
    {
        isReserved = false;
        reservedBy = null;
    }

    /// <summary>
    /// 유닛이 아이템 줍기
    /// </summary>
    public bool PickUp(Unit unit)
    {
        if (isBeingCarried) return false;
        if (isReserved && reservedBy != unit) return false;

        isBeingCarried = true;

        if (magnetCoroutine != null)
            StopCoroutine(magnetCoroutine);

        OnPickedUp?.Invoke(this);
        Debug.Log($"[DroppedItem] {resource?.ResourceName} x{amount} picked up by {unit.UnitName}");
        Destroy(gameObject);
        return true;
    }

    // ==================== 이징 함수 ====================

    private float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
    private float EaseInQuad(float t) => t * t;

    private Vector3 QuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }

    private void OnDrawGizmosSelected()
    {
        if (enableMagnet)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, magnetRadius);
        }
    }
}