using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 유닛 선택 관리자
/// - 마우스 클릭으로 유닛 선택
/// - 선택된 유닛 관리
/// - 명령 UI 연동
/// </summary>
public class UnitSelectionManager : MonoBehaviour
{
    public static UnitSelectionManager Instance { get; private set; }

    [Header("=== 설정 ===")]
    [SerializeField] private LayerMask unitLayerMask = ~0;
    [SerializeField] private float raycastDistance = 100f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private Unit selectedUnit;
    private Camera mainCamera;

    // 이벤트
    public event Action<Unit> OnUnitSelected;
    public event Action<Unit> OnUnitDeselected;
    public event Action<Unit> OnUnitClicked;

    // Properties
    public Unit SelectedUnit => selectedUnit;
    public bool HasSelection => selectedUnit != null;

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
    }

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        HandleMouseInput();
    }

    // ==================== 입력 처리 ====================

    private void HandleMouseInput()
    {
        // UI 위에서는 무시
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // ★ 명령 UI가 열려있으면 Unit 클릭 무시
        if (IsAnyCommandUIOpen())
            return;

        // 왼쪽 클릭
        if (Input.GetMouseButtonDown(0))
        {
            TrySelectUnit();
        }
    }

    /// <summary>
    /// ★ 명령 관련 UI가 열려있는지 체크
    /// </summary>
    private bool IsAnyCommandUIOpen()
    {
        // UnitCommandUI 열려있으면 클릭 무시
        if (UnitCommandUI.Instance != null && UnitCommandUI.Instance.IsOpen)
            return true;

        // 다른 UI들도 체크 (필요시 추가)
        // if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) return true;
        // if (BuildingUIManager.Instance != null && BuildingUIManager.Instance.IsOpen) return true;

        return false;
    }

    /// <summary>
    /// 유닛 선택 시도
    /// </summary>
    private void TrySelectUnit()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit, raycastDistance, unitLayerMask))
        {
            Unit clickedUnit = hit.collider.GetComponent<Unit>();

            if (clickedUnit == null)
                clickedUnit = hit.collider.GetComponentInParent<Unit>();

            if (clickedUnit != null && clickedUnit.IsAlive)
            {
                SelectUnit(clickedUnit);
                OnUnitClicked?.Invoke(clickedUnit);

                // 유닛을 명령 대기 상태로 전환
                SetUnitToCommandWaitState(clickedUnit);

                // 명령 UI 열기
                if (UnitCommandUI.Instance != null)
                {
                    UnitCommandUI.Instance.Open(clickedUnit);
                }

                if (showDebugLogs)
                    Debug.Log($"[UnitSelectionManager] 유닛 클릭: {clickedUnit.UnitName} - 명령 대기 상태");

                return;
            }
        }

        // 빈 공간 클릭 시 선택 해제 (명령 UI가 열려있지 않을 때만)
        if (UnitCommandUI.Instance == null || !UnitCommandUI.Instance.IsOpen)
        {
            DeselectUnit();
        }
    }

    /// <summary>
    /// 유닛을 명령 대기 상태로 전환
    /// </summary>
    private void SetUnitToCommandWaitState(Unit unit)
    {
        if (unit == null) return;

        // ★ UnitAI에게 명령 대기 상태 설정
        var ai = unit.GetComponent<UnitAI>();
        if (ai != null)
        {
            ai.SetWaitingForCommand(true);
        }

        // 이동 중지
        unit.StopMoving();

        if (showDebugLogs)
            Debug.Log($"[UnitSelectionManager] {unit.UnitName}: 명령 대기 상태로 전환");
    }

    /// <summary>
    /// 유닛 명령 취소 시 (ESC, UI 닫힘 등)
    /// </summary>
    public void CancelUnitCommand()
    {
        if (selectedUnit != null)
        {
            // ★ 명령 대기 해제 → AI 자유 행동 복귀
            var ai = selectedUnit.GetComponent<UnitAI>();
            if (ai != null)
            {
                ai.SetWaitingForCommand(false);
            }

            // Blackboard 명령 초기화
            if (selectedUnit.Blackboard != null)
            {
                selectedUnit.Blackboard.HasPlayerCommand = false;
                selectedUnit.Blackboard.PlayerCommand = null;
            }

            if (showDebugLogs)
                Debug.Log($"[UnitSelectionManager] {selectedUnit.UnitName}: 명령 취소, 자유 행동 복귀");
        }

        DeselectUnit();
    }

    // ==================== 선택 관리 ====================

    /// <summary>
    /// 유닛 선택
    /// </summary>
    public void SelectUnit(Unit unit)
    {
        if (unit == null || !unit.IsAlive) return;

        // 이전 선택 해제
        if (selectedUnit != null && selectedUnit != unit)
        {
            Unit previousUnit = selectedUnit;
            selectedUnit = null;
            OnUnitDeselected?.Invoke(previousUnit);
        }

        selectedUnit = unit;
        OnUnitSelected?.Invoke(unit);

        if (showDebugLogs)
            Debug.Log($"[UnitSelectionManager] 유닛 선택: {unit.UnitName}");
    }

    /// <summary>
    /// 선택 해제
    /// </summary>
    public void DeselectUnit()
    {
        if (selectedUnit == null) return;

        Unit previousUnit = selectedUnit;
        selectedUnit = null;

        OnUnitDeselected?.Invoke(previousUnit);

        if (showDebugLogs)
            Debug.Log($"[UnitSelectionManager] 선택 해제: {previousUnit.UnitName}");
    }

    /// <summary>
    /// 특정 유닛이 선택되어 있는지 확인
    /// </summary>
    public bool IsSelected(Unit unit)
    {
        return selectedUnit == unit;
    }

    // ==================== 외부 접근 ====================

    /// <summary>
    /// 외부에서 유닛 선택 및 명령 UI 열기
    /// </summary>
    public void SelectAndOpenCommand(Unit unit)
    {
        if (unit == null) return;

        SelectUnit(unit);
        SetUnitToCommandWaitState(unit);

        if (UnitCommandUI.Instance != null)
        {
            UnitCommandUI.Instance.Open(unit);
        }
    }
}