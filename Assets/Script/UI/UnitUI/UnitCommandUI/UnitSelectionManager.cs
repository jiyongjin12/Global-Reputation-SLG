using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 유닛 + 건물 선택 관리자
/// - 마우스 클릭으로 유닛 선택 → UnitCommandUI
/// - 마우스 클릭으로 건물 선택 → CraftingUI 등
/// </summary>
public class UnitSelectionManager : MonoBehaviour
{
    public static UnitSelectionManager Instance { get; private set; }

    [Header("=== 레이어 설정 ===")]
    [SerializeField] private LayerMask unitLayerMask = ~0;
    [SerializeField] private LayerMask buildingLayerMask;
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

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
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

    #endregion

    #region Input Handling

    private void HandleMouseInput()
    {
        // UI 위에서는 무시
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // 명령 UI가 열려있으면 클릭 무시
        if (IsAnyCommandUIOpen())
            return;

        // 왼쪽 클릭
        if (Input.GetMouseButtonDown(0))
        {
            TryInteract();
        }
    }

    private bool IsAnyCommandUIOpen()
    {
        // UnitCommandUI 열려있으면
        if (UnitCommandUI.Instance != null && UnitCommandUI.Instance.IsOpen)
            return true;

        // CraftingUI 열려있으면
        if (CraftingUI.Instance != null && CraftingUI.Instance.IsOpen)
            return true;

        // 다른 UI들도 체크 (필요시 추가)
        // if (InventoryUI.Instance?.IsOpen == true) return true;

        return false;
    }

    private void TryInteract()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        // 1. Unit 먼저 체크
        if (Physics.Raycast(ray, out RaycastHit unitHit, raycastDistance, unitLayerMask))
        {
            Unit clickedUnit = unitHit.collider.GetComponent<Unit>();
            if (clickedUnit == null)
                clickedUnit = unitHit.collider.GetComponentInParent<Unit>();

            if (clickedUnit != null && clickedUnit.IsAlive)
            {
                HandleUnitClick(clickedUnit);
                return;
            }
        }

        // 2. Building 체크
        if (Physics.Raycast(ray, out RaycastHit buildingHit, raycastDistance, buildingLayerMask))
        {
            var building = buildingHit.collider.GetComponent<Building>();
            if (building == null)
                building = buildingHit.collider.GetComponentInParent<Building>();

            if (building != null)
            {
                HandleBuildingClick(building);
                return;
            }
        }

        // 3. 빈 공간 클릭 시 선택 해제
        DeselectUnit();
    }

    #endregion

    #region Unit Handling

    private void HandleUnitClick(Unit unit)
    {
        SelectUnit(unit);
        OnUnitClicked?.Invoke(unit);

        // 유닛을 명령 대기 상태로 전환
        SetUnitToCommandWaitState(unit);

        // 명령 UI 열기
        UnitCommandUI.Instance?.Open(unit);

        if (showDebugLogs)
            Debug.Log($"[UnitSelectionManager] 유닛 클릭: {unit.UnitName} - 명령 대기 상태");
    }

    private void SetUnitToCommandWaitState(Unit unit)
    {
        if (unit == null) return;

        var ai = unit.GetComponent<UnitAI>();
        ai?.SetWaitingForCommand(true);

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
            var ai = selectedUnit.GetComponent<UnitAI>();
            ai?.SetWaitingForCommand(false);

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

    #endregion

    #region Building Handling

    private void HandleBuildingClick(Building building)
    {
        if (building.CurrentState != BuildingState.Completed)
        {
            if (showDebugLogs)
                Debug.Log($"[UnitSelectionManager] 건물 미완성: {building.name}");
            return;
        }

        // GetComponent → GetComponentInChildren으로 변경!
        var craftingBuilding = building.GetComponentInChildren<CraftingBuildingComponent>();
        if (craftingBuilding != null)
        {
            CraftingUI.Instance?.Open(craftingBuilding);

            if (showDebugLogs)
                Debug.Log($"[UnitSelectionManager] 제작 건물 클릭: {building.name}");
            return;
        }

        if (showDebugLogs)
            Debug.Log($"[UnitSelectionManager] 일반 건물 클릭: {building.name}");
    }

    #endregion

    #region Selection Management

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

    public void DeselectUnit()
    {
        if (selectedUnit == null) return;

        Unit previousUnit = selectedUnit;
        selectedUnit = null;

        OnUnitDeselected?.Invoke(previousUnit);

        if (showDebugLogs)
            Debug.Log($"[UnitSelectionManager] 선택 해제: {previousUnit.UnitName}");
    }

    public bool IsSelected(Unit unit)
    {
        return selectedUnit == unit;
    }

    #endregion

    #region External Access

    /// <summary>
    /// 외부에서 유닛 선택 및 명령 UI 열기
    /// </summary>
    public void SelectAndOpenCommand(Unit unit)
    {
        if (unit == null) return;

        SelectUnit(unit);
        SetUnitToCommandWaitState(unit);
        UnitCommandUI.Instance?.Open(unit);
    }

    #endregion
}