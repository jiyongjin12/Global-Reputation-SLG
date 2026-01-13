using System;
using UnityEngine;

/// <summary>
/// 쿼터뷰 카메라 컨트롤러
/// - WASD 이동 (XZ 평면)
/// - 마우스 휠 줌인/줌아웃
/// - UI 열려있을 때 이동 비활성화
/// - 회전 없음, 상하 이동 없음
/// </summary>
public class CameraController : MonoBehaviour
{
    public static CameraController Instance { get; private set; }

    [Header("=== 이동 설정 ===")]
    [SerializeField] private float moveSpeed = 20f;
    [SerializeField] private float fastMoveMultiplier = 2f;  // Shift 누를 때
    [SerializeField] private float smoothTime = 0.1f;        // 부드러운 이동

    [Header("=== 줌 설정 ===")]
    [SerializeField] private float zoomSpeed = 5f;
    [SerializeField] private float minZoom = 5f;              // 일반 최소 줌
    [SerializeField] private float maxZoom = 30f;             // 일반 최대 줌
    [SerializeField] private float zoomSmoothTime = 0.1f;

    [Header("=== 극한 줌 설정 (림버스 스타일) ===")]
    [SerializeField] private float extremeMinZoom = 3f;       // 극한 줌인
    [SerializeField] private float extremeMaxZoom = 40f;      // 극한 줌아웃

    [Header("=== 카메라 각도 설정 ===")]
    [SerializeField] private float normalAngle = 45f;         // 기본 각도
    [SerializeField] private float minAngle = 35f;            // 극한 줌인 시 각도 (더 낮게)
    [SerializeField] private float maxAngle = 50f;            // 극한 줌아웃 시 각도 (더 높게)
    [SerializeField] private float angleSmoothTime = 0.15f;

    [Header("=== 카메라 높이 설정 ===")]
    [SerializeField] private float normalHeight = 30f;        // 기본 높이
    [SerializeField] private float minHeightOffset = -2f;     // 극한 줌인 시 높이 변화 (아주 작게)
    [SerializeField] private float maxHeightOffset = 3f;      // 극한 줌아웃 시 높이 변화 (아주 작게)
    [SerializeField] private bool useHeightChange = true;     // 높이 변화 사용 여부

    [Header("=== 경계 설정 ===")]
    [SerializeField] private bool useBounds = true;
    [SerializeField] private Vector2 minBounds = new Vector2(-50f, -50f);
    [SerializeField] private Vector2 maxBounds = new Vector2(50f, 50f);

    [Header("=== 유닛 추적 설정 ===")]
    [SerializeField] private float followZoomLevel = 8f;       // 추적 시 줌 레벨
    [SerializeField] private float followZoomDuration = 0.5f;  // 줌 전환 시간
    [SerializeField] private Vector3 followOffset = new Vector3(0, 0, -10f);  // 쿼터뷰 오프셋

    [Header("=== 참조 ===")]
    [SerializeField] private Camera targetCamera;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // 내부 상태
    private Vector3 targetPosition;
    private float targetZoom;
    private Vector3 velocity = Vector3.zero;
    private float zoomVelocity = 0f;
    private bool canMove = true;

    // 각도/높이 상태
    private float targetAngle;
    private float targetHeight;
    private float angleVelocity = 0f;
    private float heightVelocity = 0f;
    private float initialYRotation;  // Y축 회전값 보존

    // 유닛 추적
    private Unit followTarget;
    private bool isFollowingUnit = false;
    private float previousZoom;

    // 카메라 모드
    private bool isOrthographic;

    // 이벤트
    public event Action<float> OnZoomChanged;
    public event Action<Vector3> OnPositionChanged;

    // ==================== Properties ====================

    public float CurrentZoom => isOrthographic ? targetCamera.orthographicSize : targetCamera.fieldOfView;
    public float CurrentAngle => transform.eulerAngles.x;
    public float CurrentHeight => transform.position.y;
    public bool CanMove => canMove;
    public Vector3 Position => transform.position;

    /// <summary>
    /// 줌이 극한 영역에 있는지 (일반 범위 밖)
    /// </summary>
    public bool IsInExtremeZoom => targetZoom < minZoom || targetZoom > maxZoom;

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        isOrthographic = targetCamera.orthographic;
        targetPosition = transform.position;
        targetZoom = isOrthographic ? targetCamera.orthographicSize : targetCamera.fieldOfView;

        // 각도/높이 초기화 (현재 카메라 상태 기준)
        targetAngle = normalAngle;
        normalHeight = transform.position.y;  // 현재 높이를 기본값으로
        targetHeight = normalHeight;
        initialYRotation = transform.eulerAngles.y;  // Y축 회전 보존
    }

    private void Start()
    {
        // UI 상태 구독
        SubscribeToUIEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromUIEvents();
    }

    private void Update()
    {
        // 유닛 추적 모드
        if (isFollowingUnit)
        {
            UpdateFollowUnit();
        }
        else
        {
            HandleMovementInput();
        }

        HandleZoomInput();
        ApplyMovement();
        ApplyZoom();
    }

    // ==================== 유닛 추적 ====================

    /// <summary>
    /// 유닛 추적 시작
    /// </summary>
    public void StartFollowUnit(Unit unit)
    {
        if (unit == null) return;

        followTarget = unit;
        isFollowingUnit = true;
        canMove = false;

        // 현재 줌 레벨 저장
        previousZoom = targetZoom;

        // 추적 시 고정값 (줌, 각도 45도, 높이 기본값)
        targetZoom = followZoomLevel;
        targetAngle = normalAngle;  // 45도 고정
        targetHeight = normalHeight;

        if (showDebugLogs)
            Debug.Log($"[CameraController] 유닛 추적 시작: {unit.UnitName}");
    }

    /// <summary>
    /// 유닛 추적 중지
    /// </summary>
    public void StopFollowUnit()
    {
        if (!isFollowingUnit) return;

        followTarget = null;
        isFollowingUnit = false;
        canMove = !IsAnyUIOpen();

        // 이전 줌 레벨로 복구
        targetZoom = previousZoom;
        CalculateAngleAndHeight();

        if (showDebugLogs)
            Debug.Log("[CameraController] 유닛 추적 종료");
    }

    /// <summary>
    /// 유닛 추적 업데이트
    /// </summary>
    private void UpdateFollowUnit()
    {
        if (followTarget == null || !followTarget.IsAlive)
        {
            StopFollowUnit();
            return;
        }

        // 쿼터뷰 오프셋 적용 (카메라가 유닛을 화면 중앙에 보이도록)
        // 카메라 회전을 고려한 오프셋 계산
        Vector3 unitPos = followTarget.transform.position;

        // 카메라의 forward/right 방향을 XZ 평면에 투영
        Vector3 camForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 camRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        // 오프셋 적용 (카메라 기준 뒤쪽으로)
        Vector3 offset = camForward * followOffset.z + camRight * followOffset.x;

        targetPosition = new Vector3(
            unitPos.x + offset.x,
            transform.position.y,
            unitPos.z + offset.z
        );
    }

    /// <summary>
    /// 현재 유닛을 추적 중인지 확인
    /// </summary>
    public bool IsFollowingUnit => isFollowingUnit;

    /// <summary>
    /// 추적 중인 유닛
    /// </summary>
    public Unit FollowTarget => followTarget;

    // ==================== UI 이벤트 연동 ====================

    private void SubscribeToUIEvents()
    {
        // 각 UI의 열림/닫힘 이벤트 구독
        if (InventoryUI.Instance != null)
        {
            InventoryUI.Instance.OnPanelOpened += OnUIOpened;
            InventoryUI.Instance.OnPanelClosed += OnUIClosed;
        }

        if (UnitListUI.Instance != null)
        {
            UnitListUI.Instance.OnPanelOpened += OnUIOpened;
            UnitListUI.Instance.OnPanelClosed += OnUIClosed;
        }

        if (BuildingUIManager.Instance != null)
        {
            BuildingUIManager.Instance.OnPanelOpened += OnUIOpened;
            BuildingUIManager.Instance.OnPanelClosed += OnUIClosed;
        }

        // ChatPanelController도 있다면 추가
        // if (ChatPanelController.Instance != null)
        // {
        //     ChatPanelController.Instance.OnPanelOpened += OnUIOpened;
        //     ChatPanelController.Instance.OnPanelClosed += OnUIClosed;
        // }
    }

    private void UnsubscribeFromUIEvents()
    {
        if (InventoryUI.Instance != null)
        {
            InventoryUI.Instance.OnPanelOpened -= OnUIOpened;
            InventoryUI.Instance.OnPanelClosed -= OnUIClosed;
        }

        if (UnitListUI.Instance != null)
        {
            UnitListUI.Instance.OnPanelOpened -= OnUIOpened;
            UnitListUI.Instance.OnPanelClosed -= OnUIClosed;
        }

        if (BuildingUIManager.Instance != null)
        {
            BuildingUIManager.Instance.OnPanelOpened -= OnUIOpened;
            BuildingUIManager.Instance.OnPanelClosed -= OnUIClosed;
        }
    }

    private void OnUIOpened()
    {
        canMove = false;
        if (showDebugLogs)
            Debug.Log("[CameraController] UI 열림 - 이동 비활성화");
    }

    private void OnUIClosed()
    {
        // 모든 UI가 닫혔는지 확인
        bool anyUIOpen = IsAnyUIOpen();

        if (!anyUIOpen)
        {
            canMove = true;
            if (showDebugLogs)
                Debug.Log("[CameraController] 모든 UI 닫힘 - 이동 활성화");
        }
    }

    /// <summary>
    /// 열린 UI가 있는지 확인
    /// </summary>
    private bool IsAnyUIOpen()
    {
        if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) return true;
        if (UnitListUI.Instance != null && UnitListUI.Instance.IsOpen) return true;
        if (BuildingUIManager.Instance != null && BuildingUIManager.Instance.IsOpen) return true;
        if (ChatPanelController.Instance != null && ChatPanelController.Instance.IsOpen) return true;
        return false;
    }

    // ==================== 이동 처리 ====================

    private void HandleMovementInput()
    {
        if (!canMove) return;

        float horizontal = 0f;
        float vertical = 0f;

        // WASD 입력
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical = 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical = -1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal = -1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal = 1f;

        if (horizontal == 0f && vertical == 0f) return;

        // 속도 계산
        float currentSpeed = moveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            currentSpeed *= fastMoveMultiplier;

        // 줌 레벨에 따른 속도 조정 (줌 아웃일수록 빠르게)
        float zoomFactor = isOrthographic
            ? targetCamera.orthographicSize / ((minZoom + maxZoom) * 0.5f)
            : targetCamera.fieldOfView / ((minZoom + maxZoom) * 0.5f);
        currentSpeed *= zoomFactor;

        // 쿼터뷰 방향 계산 (카메라 기준)
        // 카메라가 45도로 내려다보고 있으므로 이동 방향 조정
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        // 이동량 계산
        Vector3 moveDirection = (right * horizontal + forward * vertical).normalized;
        Vector3 delta = moveDirection * currentSpeed * Time.unscaledDeltaTime;

        targetPosition += delta;

        // 경계 제한
        if (useBounds)
        {
            targetPosition.x = Mathf.Clamp(targetPosition.x, minBounds.x, maxBounds.x);
            targetPosition.z = Mathf.Clamp(targetPosition.z, minBounds.y, maxBounds.y);
        }
    }

    private void ApplyMovement()
    {
        if (Vector3.Distance(transform.position, targetPosition) < 0.001f) return;

        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        OnPositionChanged?.Invoke(transform.position);
    }

    // ==================== 줌 처리 ====================

    private void HandleZoomInput()
    {
        // 유닛 추적 중이면 줌 입력 무시
        if (isFollowingUnit) return;

        float scrollDelta = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scrollDelta) < 0.01f) return;

        // 줌 방향 (휠 위로 = 줌인, 휠 아래로 = 줌아웃)
        float zoomDelta = -scrollDelta * zoomSpeed;

        // 극한 범위까지 허용
        targetZoom = Mathf.Clamp(targetZoom + zoomDelta, extremeMinZoom, extremeMaxZoom);

        // 줌 레벨에 따른 각도/높이 계산
        CalculateAngleAndHeight();
    }

    /// <summary>
    /// 줌 레벨에 따른 카메라 각도와 높이 계산
    /// </summary>
    private void CalculateAngleAndHeight()
    {
        // 줌 레벨을 0~1 범위로 정규화 (extremeMin=0, extremeMax=1)
        float normalizedZoom = Mathf.InverseLerp(extremeMinZoom, extremeMaxZoom, targetZoom);

        // 일반 범위의 정규화 값 (min=normalizedMin, max=normalizedMax)
        float normalizedMin = Mathf.InverseLerp(extremeMinZoom, extremeMaxZoom, minZoom);
        float normalizedMax = Mathf.InverseLerp(extremeMinZoom, extremeMaxZoom, maxZoom);

        if (normalizedZoom < normalizedMin)
        {
            // 극한 줌인 영역 (extremeMin ~ min)
            // 각도: normalAngle → minAngle (35도로 내려감)
            float t = Mathf.InverseLerp(normalizedMin, 0f, normalizedZoom);
            targetAngle = Mathf.Lerp(normalAngle, minAngle, t);

            // 높이: 오프셋만큼만 변화 (아주 작게)
            if (useHeightChange)
                targetHeight = normalHeight + (minHeightOffset * t);
            else
                targetHeight = normalHeight;
        }
        else if (normalizedZoom > normalizedMax)
        {
            // 극한 줌아웃 영역 (max ~ extremeMax)
            // 각도: normalAngle → maxAngle (50도로 올라감)
            float t = Mathf.InverseLerp(normalizedMax, 1f, normalizedZoom);
            targetAngle = Mathf.Lerp(normalAngle, maxAngle, t);

            // 높이: 오프셋만큼만 변화 (아주 작게)
            if (useHeightChange)
                targetHeight = normalHeight + (maxHeightOffset * t);
            else
                targetHeight = normalHeight;
        }
        else
        {
            // 일반 줌 영역 (min ~ max)
            // 각도/높이 고정
            targetAngle = normalAngle;
            targetHeight = normalHeight;
        }
    }

    private void ApplyZoom()
    {
        float currentZoom = isOrthographic ? targetCamera.orthographicSize : targetCamera.fieldOfView;
        bool zoomChanged = Mathf.Abs(currentZoom - targetZoom) > 0.01f;

        if (zoomChanged)
        {
            float newZoom = Mathf.SmoothDamp(currentZoom, targetZoom, ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);

            if (isOrthographic)
                targetCamera.orthographicSize = newZoom;
            else
                targetCamera.fieldOfView = newZoom;

            OnZoomChanged?.Invoke(newZoom);
        }

        // 각도 적용
        ApplyAngleAndHeight();
    }

    /// <summary>
    /// 카메라 각도와 높이 부드럽게 적용
    /// </summary>
    private void ApplyAngleAndHeight()
    {
        Vector3 currentEuler = transform.eulerAngles;
        float currentAngle = currentEuler.x;
        float currentHeight = transform.position.y;

        // 각도 변화 (임계값 0.5도)
        float angleDiff = Mathf.Abs(currentAngle - targetAngle);
        if (angleDiff > 0.5f)
        {
            float newAngle = Mathf.SmoothDamp(currentAngle, targetAngle, ref angleVelocity, angleSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            transform.eulerAngles = new Vector3(newAngle, initialYRotation, 0f);
        }
        else if (angleDiff > 0.01f)
        {
            // 목표에 가까우면 직접 설정 (떨림 방지)
            transform.eulerAngles = new Vector3(targetAngle, initialYRotation, 0f);
            angleVelocity = 0f;
        }

        // 높이 변화 (임계값 0.1)
        float heightDiff = Mathf.Abs(currentHeight - targetHeight);
        if (heightDiff > 0.1f)
        {
            float newHeight = Mathf.SmoothDamp(currentHeight, targetHeight, ref heightVelocity, angleSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            Vector3 pos = transform.position;
            pos.y = newHeight;
            transform.position = pos;
        }
        else if (heightDiff > 0.01f)
        {
            // 목표에 가까우면 직접 설정 (떨림 방지)
            Vector3 pos = transform.position;
            pos.y = targetHeight;
            transform.position = pos;
            heightVelocity = 0f;
        }
    }

    // ==================== 외부 제어 ====================

    /// <summary>
    /// 카메라 이동 가능 여부 설정
    /// </summary>
    public void SetCanMove(bool value)
    {
        canMove = value;
    }

    /// <summary>
    /// 특정 위치로 이동
    /// </summary>
    public void MoveTo(Vector3 position, bool instant = false)
    {
        targetPosition = new Vector3(position.x, transform.position.y, position.z);

        if (useBounds)
        {
            targetPosition.x = Mathf.Clamp(targetPosition.x, minBounds.x, maxBounds.x);
            targetPosition.z = Mathf.Clamp(targetPosition.z, minBounds.y, maxBounds.y);
        }

        if (instant)
        {
            transform.position = targetPosition;
            velocity = Vector3.zero;
        }
    }

    /// <summary>
    /// 특정 타겟으로 이동
    /// </summary>
    public void FocusOn(Transform target, bool instant = false)
    {
        if (target != null)
            MoveTo(target.position, instant);
    }

    /// <summary>
    /// 특정 유닛으로 포커스
    /// </summary>
    public void FocusOnUnit(Unit unit, bool instant = false)
    {
        if (unit != null)
            MoveTo(unit.transform.position, instant);
    }

    /// <summary>
    /// 줌 레벨 설정
    /// </summary>
    public void SetZoom(float zoom, bool instant = false)
    {
        targetZoom = Mathf.Clamp(zoom, extremeMinZoom, extremeMaxZoom);
        CalculateAngleAndHeight();

        if (instant)
        {
            if (isOrthographic)
                targetCamera.orthographicSize = targetZoom;
            else
                targetCamera.fieldOfView = targetZoom;
            zoomVelocity = 0f;

            // 각도/높이도 즉시 적용
            transform.eulerAngles = new Vector3(targetAngle, initialYRotation, 0f);
            Vector3 pos = transform.position;
            pos.y = targetHeight;
            transform.position = pos;
            angleVelocity = 0f;
            heightVelocity = 0f;
        }
    }

    /// <summary>
    /// 경계 설정
    /// </summary>
    public void SetBounds(Vector2 min, Vector2 max)
    {
        minBounds = min;
        maxBounds = max;
    }

    /// <summary>
    /// 경계 사용 여부 설정
    /// </summary>
    public void SetUseBounds(bool value)
    {
        useBounds = value;
    }

    // ==================== 유틸리티 ====================

    /// <summary>
    /// 마우스 위치의 월드 좌표 (XZ 평면)
    /// </summary>
    public Vector3 GetMouseWorldPosition()
    {
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (groundPlane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }

        return Vector3.zero;
    }

    /// <summary>
    /// 화면 좌표를 월드 좌표로 변환
    /// </summary>
    public Vector3 ScreenToWorldPosition(Vector2 screenPos)
    {
        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (groundPlane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }

        return Vector3.zero;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!useBounds) return;

        // 경계 표시
        Gizmos.color = Color.yellow;
        Vector3 center = new Vector3(
            (minBounds.x + maxBounds.x) * 0.5f,
            transform.position.y,
            (minBounds.y + maxBounds.y) * 0.5f
        );
        Vector3 size = new Vector3(
            maxBounds.x - minBounds.x,
            0.1f,
            maxBounds.y - minBounds.y
        );
        Gizmos.DrawWireCube(center, size);
    }
#endif
}