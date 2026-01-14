using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// 유닛 명령 UI (컬트오브더램 스타일)
/// 
/// 메뉴 구조:
/// [메인]
/// ├── 상세보기 → Unit 상세 UI (TODO)
/// ├── 노동 →
/// │   ├── 돌 캐기 (구현)
/// │   ├── 나무 캐기 (구현)
/// │   ├── 밭 관리 (TODO)
/// │   ├── 건물 → (TODO)
/// │   └── 뒤로
/// └── 상호작용 →
///     ├── 선물주기 (TODO)
///     ├── 쓰다듬기 (구현: 정신력+5, 충성심+5)
///     ├── 대화 (TODO)
///     └── 뒤로
/// </summary>
public class UnitCommandUI : MonoBehaviour, IEscapableUI
{
    public static UnitCommandUI Instance { get; private set; }

    [Header("=== UI 참조 ===")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private Image backgroundCircle;
    [SerializeField] private Transform optionsParent;

    [Header("=== 프리팹 ===")]
    [SerializeField] private GameObject optionPrefab;

    [Header("=== 배치 설정 ===")]
    [SerializeField] private float optionDistance = 150f;
    [SerializeField] private float startAngleOffset = 90f;

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float animationDuration = 0.3f;
    [SerializeField] private float startDistance = 250f;
    [SerializeField] private Ease enterEase = Ease.OutBack;
    [SerializeField] private Ease exitEase = Ease.InBack;

    [Header("=== 배경 설정 ===")]
    [SerializeField] private float bgFadeInDuration = 0.2f;
    [SerializeField] private float bgTargetAlpha = 0.8f;

    [Header("=== 쓰다듬기 효과 ===")]
    [SerializeField] private float petMentalHealthBonus = 5f;
    [SerializeField] private float petLoyaltyBonus = 5f;

    [Header("=== ESC 우선순위 ===")]
    [SerializeField] private int escapePriority = 100;

    // 상태
    private bool isOpen = false;
    private Unit currentUnit;
    private List<UnitCommandOption> currentOptions = new();
    private Stack<CommandMenuData> menuStack = new();

    // ★ 명령 실행 여부 플래그
    private bool commandExecuted = false;

    // 이벤트
    public event Action OnPanelOpened;
    public event Action OnPanelClosed;
    public event Action<Unit> OnUnitCommandStarted;

    // IEscapableUI
    public bool IsOpen => isOpen;
    public int EscapePriority => escapePriority;

    // ==================== Unity Lifecycle ====================

    private void Awake()
    {
        Instance = this;
        if (mainPanel != null)
            mainPanel.SetActive(false);
    }

    private void Start()
    {
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.RegisterEscapableUI(this);
        }

        SubscribeToOtherUIs();
    }

    private void OnDestroy()
    {
        if (GameInputManager.Instance != null)
        {
            GameInputManager.Instance.UnregisterEscapableUI(this);
        }
        UnsubscribeFromOtherUIs();
    }

    // ==================== 다른 UI 연동 ====================

    private void SubscribeToOtherUIs()
    {
        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OnPanelOpened += OnOtherUIOpened;

        if (UnitListUI.Instance != null)
            UnitListUI.Instance.OnPanelOpened += OnOtherUIOpened;

        if (BuildingUIManager.Instance != null)
            BuildingUIManager.Instance.OnPanelOpened += OnOtherUIOpened;
    }

    private void UnsubscribeFromOtherUIs()
    {
        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OnPanelOpened -= OnOtherUIOpened;

        if (UnitListUI.Instance != null)
            UnitListUI.Instance.OnPanelOpened -= OnOtherUIOpened;

        if (BuildingUIManager.Instance != null)
            BuildingUIManager.Instance.OnPanelOpened -= OnOtherUIOpened;
    }

    private void OnOtherUIOpened()
    {
        if (isOpen) Close();
    }

    // ==================== 열기/닫기 ====================

    public void Open(Unit unit)
    {
        if (unit == null) return;
        if (isOpen) Close();

        currentUnit = unit;
        isOpen = true;
        commandExecuted = false;  // ★ 초기화
        menuStack.Clear();

        mainPanel.SetActive(true);

        // 배경 페이드인
        if (backgroundCircle != null)
        {
            Color bgColor = backgroundCircle.color;
            bgColor.a = 0;
            backgroundCircle.color = bgColor;
            backgroundCircle.DOFade(bgTargetAlpha, bgFadeInDuration).SetUpdate(true);
        }

        // 루트 메뉴 표시
        ShowRootMenu();

        // 카메라 유닛 포커스
        if (CameraController.Instance != null)
        {
            CameraController.Instance.StartFollowUnit(unit);
        }

        OnPanelOpened?.Invoke();
        OnUnitCommandStarted?.Invoke(unit);

        Debug.Log($"[UnitCommandUI] 열림 - {unit.UnitName}");
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        // 옵션들 퇴장 애니메이션
        AnimateOptionsExit(() =>
        {
            ClearOptions();
            if (mainPanel != null)
                mainPanel.SetActive(false);
        });

        // 배경 페이드아웃
        if (backgroundCircle != null)
        {
            backgroundCircle.DOFade(0, bgFadeInDuration).SetUpdate(true);
        }

        // 카메라 추적 해제
        if (CameraController.Instance != null)
        {
            CameraController.Instance.StopFollowUnit();
        }

        // ★ 명령이 내려지지 않았을 때만 취소 처리
        if (!commandExecuted)
        {
            if (UnitSelectionManager.Instance != null)
            {
                UnitSelectionManager.Instance.CancelUnitCommand();
            }
        }
        else
        {
            // 명령이 내려진 경우, 선택만 해제 (AI는 명령 실행 중)
            if (UnitSelectionManager.Instance != null)
            {
                UnitSelectionManager.Instance.DeselectUnit();
            }
        }

        // 초기화
        currentUnit = null;
        menuStack.Clear();
        commandExecuted = false;

        OnPanelClosed?.Invoke();
        Debug.Log("[UnitCommandUI] 닫힘");
    }

    // ==================== 메뉴 시스템 ====================

    /// <summary>
    /// 루트 메뉴: 상세보기, 노동, 상호작용
    /// </summary>
    private void ShowRootMenu()
    {
        var rootOptions = new List<CommandOptionData>
        {
            new CommandOptionData("상세보기", "detail", OnDetailView),
            new CommandOptionData("노동", "work", ShowWorkMenu),
            new CommandOptionData("상호작용", "interact", ShowInteractMenu)
        };

        ShowMenu(new CommandMenuData("메인", rootOptions));
    }

    /// <summary>
    /// 상세보기 -> Unit 상세 UI (TODO)
    /// </summary>
    private void OnDetailView()
    {
        Debug.Log($"[UnitCommandUI] 상세보기 - {currentUnit?.UnitName}");

        // TODO: Unit 상세 UI 열기
        // UnitDetailUI.Instance?.Open(currentUnit);

        ShowNotImplementedMessage("상세보기 UI는 준비 중입니다.");
        Close();
    }

    // ==================== 상호작용 메뉴 ====================

    /// <summary>
    /// 상호작용 메뉴: 선물주기, 쓰다듬기, 대화
    /// </summary>
    private void ShowInteractMenu()
    {
        var interactOptions = new List<CommandOptionData>
        {
            new CommandOptionData("선물주기", "gift", OnGiftCommand),
            new CommandOptionData("쓰다듬기", "pet", OnPetCommand),
            new CommandOptionData("대화", "talk", OnTalkCommand),
            new CommandOptionData("뒤로", "back", GoBack)
        };

        ShowMenu(new CommandMenuData("상호작용", interactOptions));
    }

    /// <summary>
    /// 선물주기 (TODO)
    /// </summary>
    private void OnGiftCommand()
    {
        Debug.Log($"[UnitCommandUI] 선물주기 - {currentUnit?.UnitName}");
        ShowNotImplementedMessage("선물주기 기능은 준비 중입니다.");
        // TODO: 인벤토리에서 선물 선택 UI
    }

    /// <summary>
    /// 쓰다듬기 - 정신력 +5, 충성심 +5
    /// </summary>
    private void OnPetCommand()
    {
        if (currentUnit == null) return;

        Debug.Log($"[UnitCommandUI] 쓰다듬기 - {currentUnit.UnitName}");

        // 정신력, 충성심 증가
        currentUnit.IncreaseMentalHealth(petMentalHealthBonus);
        currentUnit.ModifyLoyalty(petLoyaltyBonus);

        Debug.Log($"[UnitCommandUI] {currentUnit.UnitName}: 정신력 +{petMentalHealthBonus}, 충성심 +{petLoyaltyBonus}");

        // TODO: 쓰다듬기 애니메이션, 이펙트, 사운드

        // ★ 쓰다듬기는 명령이 아니라 즉시 효과이므로, 명령 취소로 처리
        // commandExecuted = false 상태로 Close() → AI 자유 행동 복귀
        Close();
    }

    /// <summary>
    /// 대화 (TODO)
    /// </summary>
    private void OnTalkCommand()
    {
        Debug.Log($"[UnitCommandUI] 대화 - {currentUnit?.UnitName}");
        ShowNotImplementedMessage("대화 기능은 준비 중입니다.");
        // TODO: 대화 UI
    }

    // ==================== 노동 메뉴 ====================

    /// <summary>
    /// 노동 메뉴: 돌 캐기, 나무 캐기, 밭 관리, 건물
    /// </summary>
    private void ShowWorkMenu()
    {
        var workOptions = new List<CommandOptionData>
        {
            new CommandOptionData("돌 캐기", "mine_stone", OnMineStoneCommand),
            new CommandOptionData("나무 캐기", "chop_wood", OnChopWoodCommand),
            new CommandOptionData("밭 관리", "farm", OnFarmCommand),
            new CommandOptionData("건물", "building", ShowBuildingMenu),
            new CommandOptionData("뒤로", "back", GoBack)
        };

        ShowMenu(new CommandMenuData("노동", workOptions));
    }

    /// <summary>
    /// 돌 캐기 명령
    /// </summary>
    private void OnMineStoneCommand()
    {
        if (currentUnit == null) return;

        Debug.Log($"[UnitCommandUI] 돌 캐기 명령 - {currentUnit.UnitName}");

        // 플레이어 명령으로 돌 채집 지시
        if (GiveHarvestCommand(ResourceNodeType.Rock))
        {
            commandExecuted = true;  // ★ 명령 성공
        }

        Close();
    }

    /// <summary>
    /// 나무 캐기 명령
    /// </summary>
    private void OnChopWoodCommand()
    {
        if (currentUnit == null) return;

        Debug.Log($"[UnitCommandUI] 나무 캐기 명령 - {currentUnit.UnitName}");

        // 플레이어 명령으로 나무 채집 지시
        if (GiveHarvestCommand(ResourceNodeType.Tree))
        {
            commandExecuted = true;  // ★ 명령 성공
        }

        Close();
    }

    /// <summary>
    /// 채집 명령 전달 (플레이어 명령 - 최우선순위)
    /// </summary>
    /// <returns>명령 성공 여부</returns>
    private bool GiveHarvestCommand(ResourceNodeType resourceType)
    {
        if (currentUnit == null) return false;

        // 가장 가까운 해당 타입 자원 노드 찾기
        ResourceNode nearestNode = FindNearestResourceNode(resourceType);

        if (nearestNode == null)
        {
            Debug.LogWarning($"[UnitCommandUI] {resourceType} 자원 노드를 찾을 수 없습니다.");
            ShowNotImplementedMessage($"{resourceType} 자원이 근처에 없습니다.");
            return false;
        }

        // Blackboard에 플레이어 명령 설정
        if (currentUnit.Blackboard != null)
        {
            currentUnit.Blackboard.HasPlayerCommand = true;
            currentUnit.Blackboard.PlayerCommand = new UnitCommand(
                UnitCommandType.Harvest,
                nearestNode.transform.position,
                nearestNode.gameObject
            );

            Debug.Log($"[UnitCommandUI] {currentUnit.UnitName}: {resourceType} 채집 명령 (플레이어 명령)");
            return true;
        }

        return false;
    }

    /// <summary>
    /// 가장 가까운 자원 노드 찾기 (기존 ResourceNode 구조에 맞춤)
    /// </summary>
    private ResourceNode FindNearestResourceNode(ResourceNodeType type)
    {
        ResourceNode[] allNodes = FindObjectsOfType<ResourceNode>();
        ResourceNode nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var node in allNodes)
        {
            // 데이터 없으면 스킵
            if (node.Data == null) continue;

            // 타입 체크
            if (node.Data.NodeType != type) continue;

            // 채집 불가능하면 스킵
            if (!node.CanBeHarvested) continue;

            float dist = Vector3.Distance(currentUnit.transform.position, node.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = node;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 밭 관리 (TODO)
    /// </summary>
    private void OnFarmCommand()
    {
        Debug.Log($"[UnitCommandUI] 밭 관리 - {currentUnit?.UnitName}");
        ShowNotImplementedMessage("밭 관리 기능은 준비 중입니다.");
        // TODO: 밭 건물 시스템 구현 후 연동
    }

    // ==================== 건물 메뉴 ====================

    /// <summary>
    /// 건물 메뉴 (작업대, 요리 등 예약된 작업)
    /// </summary>
    private void ShowBuildingMenu()
    {
        var buildingOptions = new List<CommandOptionData>();

        // TODO: 현재 필드에 있는 작업 가능한 건물들 중 예약된 것들 추가
        // 예시:
        // foreach (var building in BuildingManager.Instance.GetWorkableBuildings())
        // {
        //     if (building.HasReservedTask)
        //     {
        //         buildingOptions.Add(new CommandOptionData(
        //             building.BuildingName,
        //             building.BuildingID,
        //             () => OnBuildingWorkCommand(building)
        //         ));
        //     }
        // }

        // 임시: 기능 미구현 안내
        buildingOptions.Add(new CommandOptionData("제작 (준비중)", "craft", OnCraftCommand));
        buildingOptions.Add(new CommandOptionData("요리 (준비중)", "cook", OnCookCommand));
        buildingOptions.Add(new CommandOptionData("뒤로", "back", GoBack));

        ShowMenu(new CommandMenuData("건물", buildingOptions));
    }

    private void OnCraftCommand()
    {
        ShowNotImplementedMessage("제작 기능은 준비 중입니다.");
    }

    private void OnCookCommand()
    {
        ShowNotImplementedMessage("요리 기능은 준비 중입니다.");
    }

    // ==================== 메뉴 표시 ====================

    private void ShowMenu(CommandMenuData menuData, bool pushToStack = true)
    {
        if (currentOptions.Count > 0)
        {
            AnimateOptionsExit(() =>
            {
                ClearOptions();
                CreateAndShowOptions(menuData);
            });
        }
        else
        {
            CreateAndShowOptions(menuData);
        }

        if (pushToStack)
            menuStack.Push(menuData);
    }

    public void GoBack()
    {
        if (menuStack.Count <= 1)
        {
            Close();
            return;
        }

        menuStack.Pop();
        var previousMenu = menuStack.Pop();
        ShowMenu(previousMenu);
    }

    // ==================== 옵션 생성/애니메이션 ====================

    private void CreateAndShowOptions(CommandMenuData menuData)
    {
        int count = menuData.Options.Count;

        for (int i = 0; i < count; i++)
        {
            var optionData = menuData.Options[i];

            GameObject optionObj = Instantiate(optionPrefab, optionsParent);

            CanvasGroup cg = optionObj.GetComponent<CanvasGroup>();
            if (cg == null) cg = optionObj.AddComponent<CanvasGroup>();

            UnitCommandOption option = optionObj.GetComponent<UnitCommandOption>();
            if (option != null)
            {
                option.Initialize(optionData, OnOptionClicked);
                currentOptions.Add(option);
            }

            Vector2 targetPos = CalculateOptionPosition(i, count);
            Vector2 startPos = targetPos.normalized * startDistance;

            RectTransform rt = optionObj.GetComponent<RectTransform>();
            rt.anchoredPosition = startPos;
            cg.alpha = 0;

            float delay = i * 0.05f;
            rt.DOAnchorPos(targetPos, animationDuration)
                .SetDelay(delay)
                .SetEase(enterEase)
                .SetUpdate(true);

            cg.DOFade(1, animationDuration)
                .SetDelay(delay)
                .SetUpdate(true);
        }
    }

    private Vector2 CalculateOptionPosition(int index, int total)
    {
        float angleStep = 360f / total;
        float angle = startAngleOffset + (angleStep * index);
        float radian = angle * Mathf.Deg2Rad;

        return new Vector2(
            Mathf.Cos(radian) * optionDistance,
            Mathf.Sin(radian) * optionDistance
        );
    }

    private void AnimateOptionsExit(Action onComplete)
    {
        if (currentOptions.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int completed = 0;
        int total = currentOptions.Count;

        for (int i = 0; i < total; i++)
        {
            var option = currentOptions[i];
            if (option == null) continue;

            RectTransform rt = option.GetComponent<RectTransform>();
            CanvasGroup cg = option.GetComponent<CanvasGroup>();

            rt.DOAnchorPos(Vector2.zero, animationDuration * 0.7f)
                .SetEase(exitEase)
                .SetUpdate(true);

            cg.DOFade(0, animationDuration * 0.7f)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    completed++;
                    if (completed >= total)
                        onComplete?.Invoke();
                });
        }
    }

    private void ClearOptions()
    {
        foreach (var option in currentOptions)
        {
            if (option != null)
                Destroy(option.gameObject);
        }
        currentOptions.Clear();
    }

    private void OnOptionClicked(CommandOptionData data)
    {
        Debug.Log($"[UnitCommandUI] 옵션 클릭: {data.DisplayName}");
        data.OnClick?.Invoke();
    }

    // ==================== 유틸리티 ====================

    /// <summary>
    /// 미구현 기능 안내 메시지
    /// </summary>
    private void ShowNotImplementedMessage(string message)
    {
        Debug.Log($"[UnitCommandUI] {message}");
        // TODO: UI 알림 표시
    }

    // ==================== 외부 접근 ====================

    public Unit CurrentUnit => currentUnit;
}

/// <summary>
/// 메뉴 데이터
/// </summary>
public class CommandMenuData
{
    public string MenuName;
    public List<CommandOptionData> Options;

    public CommandMenuData(string name, List<CommandOptionData> options)
    {
        MenuName = name;
        Options = options;
    }
}

/// <summary>
/// 옵션 데이터
/// </summary>
public class CommandOptionData
{
    public string DisplayName;
    public string ID;
    public Action OnClick;
    public Sprite Icon;

    public CommandOptionData(string displayName, string id, Action onClick, Sprite icon = null)
    {
        DisplayName = displayName;
        ID = id;
        OnClick = onClick;
        Icon = icon;
    }
}