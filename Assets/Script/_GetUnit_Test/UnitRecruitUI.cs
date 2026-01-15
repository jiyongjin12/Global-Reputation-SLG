using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 유닛 고용 UI 관리
/// - 주기 전환 시 자동으로 표시
/// - 후보 유닛 선택 및 정보 표시
/// - 현재 보유 유닛 목록 표시
/// </summary>
public class UnitRecruitUI : MonoBehaviour
{
    [Header("=== 루트 패널 ===")]
    [Tooltip("전체 UI 루트 (주기 전환 시 활성화)")]
    [SerializeField] private GameObject rootPanel;

    [Header("=== 후보 유닛 선택 ===")]
    [Tooltip("후보 유닛 스크롤의 Content")]
    [SerializeField] private Transform unitSelectContent;

    [Tooltip("UnitCheckBox 프리팹")]
    [SerializeField] private GameObject unitCheckBoxPrefab;

    [Header("=== 선택된 유닛 정보 (UnitSet) ===")]
    [SerializeField] private GameObject unitSetPanel;
    [SerializeField] private Image unitSetIcon;
    [SerializeField] private TextMeshProUGUI unitSetName;

    [Tooltip("특성 아이콘 부모")]
    [SerializeField] private Transform unitTraitsParent;

    [Tooltip("특성 아이콘 프리팹")]
    [SerializeField] private GameObject skillIconPrefab;

    [Tooltip("비용 부모")]
    [SerializeField] private Transform priceParent;

    [Tooltip("비용 프리팹")]
    [SerializeField] private GameObject unitPricePrefab;

    [Header("=== 현재 유닛 목록 ===")]
    [Tooltip("현재 유닛 스크롤의 Content")]
    [SerializeField] private Transform currentUnitContent;

    [Header("=== 버튼 ===")]
    [SerializeField] private Button recruitButton;
    [SerializeField] private Button skipButton;
    [SerializeField] private Button closeButton;

    // 내부 상태
    private List<GameObject> candidateCheckBoxes = new List<GameObject>();
    private List<GameObject> currentUnitCheckBoxes = new List<GameObject>();
    private List<GameObject> traitIcons = new List<GameObject>();
    private List<GameObject> priceItems = new List<GameObject>();

    private RecruitableUnit selectedCandidate;
    private int selectedIndex = -1;

    private void Awake()
    {
        // 시작 시 비활성화
        if (rootPanel != null)
            rootPanel.SetActive(false);

        // 버튼 이벤트 연결
        if (recruitButton != null)
            recruitButton.onClick.AddListener(OnRecruitButtonClicked);

        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipButtonClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseUI);
    }

    private void Start()
    {
        // UnitRecruitManager 이벤트 구독
        if (UnitRecruitManager.Instance != null)
        {
            UnitRecruitManager.Instance.OnCandidatesGenerated += OnCandidatesGenerated;
            UnitRecruitManager.Instance.OnUnitRecruited += OnUnitRecruited;
        }
    }

    private void OnDestroy()
    {
        if (UnitRecruitManager.Instance != null)
        {
            UnitRecruitManager.Instance.OnCandidatesGenerated -= OnCandidatesGenerated;
            UnitRecruitManager.Instance.OnUnitRecruited -= OnUnitRecruited;
        }
    }

    /// <summary>
    /// 후보 생성됨 → UI 표시
    /// </summary>
    private void OnCandidatesGenerated(List<RecruitableUnit> candidates)
    {
        ShowUI(candidates);
    }

    /// <summary>
    /// 유닛 고용됨
    /// </summary>
    private void OnUnitRecruited(Unit unit)
    {
        // 현재 유닛 목록 갱신
        RefreshCurrentUnitList();

        // 선택 초기화
        ClearSelection();

        // 후보가 없으면 UI 닫기
        if (UnitRecruitManager.Instance != null && !UnitRecruitManager.Instance.HasCandidates)
        {
            CloseUI();
        }
        else
        {
            // 후보 목록 갱신
            RefreshCandidateList();
        }
    }

    /// <summary>
    /// UI 표시
    /// </summary>
    public void ShowUI(List<RecruitableUnit> candidates)
    {
        if (rootPanel == null) return;

        rootPanel.SetActive(true);

        // 후보 목록 생성
        CreateCandidateList(candidates);

        // 현재 유닛 목록 생성
        RefreshCurrentUnitList();

        // 선택 초기화
        ClearSelection();

        // 게임 일시정지 (선택)
        // Time.timeScale = 0f;

        Debug.Log("[UnitRecruitUI] UI 표시됨");
    }

    /// <summary>
    /// UI 닫기
    /// </summary>
    public void CloseUI()
    {
        if (rootPanel != null)
            rootPanel.SetActive(false);

        // 게임 재개
        // Time.timeScale = 1f;

        ClearAllLists();

        Debug.Log("[UnitRecruitUI] UI 닫힘");
    }

    /// <summary>
    /// 후보 목록 생성
    /// </summary>
    private void CreateCandidateList(List<RecruitableUnit> candidates)
    {
        ClearCandidateList();

        if (unitSelectContent == null || unitCheckBoxPrefab == null) return;

        for (int i = 0; i < candidates.Count; i++)
        {
            RecruitableUnit candidate = candidates[i];
            GameObject checkBox = Instantiate(unitCheckBoxPrefab, unitSelectContent);

            // 체크박스 설정
            SetupCheckBox(checkBox, candidate, i, true);

            candidateCheckBoxes.Add(checkBox);
        }
    }

    /// <summary>
    /// 후보 목록 갱신
    /// </summary>
    private void RefreshCandidateList()
    {
        if (UnitRecruitManager.Instance == null) return;
        CreateCandidateList(UnitRecruitManager.Instance.CurrentCandidates);
    }

    /// <summary>
    /// 현재 유닛 목록 갱신
    /// </summary>
    private void RefreshCurrentUnitList()
    {
        ClearCurrentUnitList();

        if (currentUnitContent == null || unitCheckBoxPrefab == null) return;
        if (UnitManager.Instance == null) return;

        List<Unit> allUnits = UnitManager.Instance.GetAllUnits();

        for (int i = 0; i < allUnits.Count; i++)
        {
            Unit unit = allUnits[i];
            if (unit == null) continue;

            GameObject checkBox = Instantiate(unitCheckBoxPrefab, currentUnitContent);

            // 체크박스 설정 (현재 유닛용)
            SetupCurrentUnitCheckBox(checkBox, unit);

            currentUnitCheckBoxes.Add(checkBox);
        }
    }

    /// <summary>
    /// 체크박스 설정 (후보 유닛용)
    /// </summary>
    private void SetupCheckBox(GameObject checkBox, RecruitableUnit candidate, int index, bool isCandidate)
    {
        // Icon 찾기
        Image icon = checkBox.transform.Find("Image")?.GetComponent<Image>();
        if (icon == null)
            icon = checkBox.transform.Find("Icon")?.GetComponent<Image>();
        if (icon != null && candidate.Icon != null)
            icon.sprite = candidate.Icon;

        // 이름 찾기
        TextMeshProUGUI nameText = checkBox.transform.Find("Text (TMP)")?.GetComponent<TextMeshProUGUI>();
        if (nameText == null)
            nameText = checkBox.GetComponentInChildren<TextMeshProUGUI>();
        if (nameText != null)
            nameText.text = candidate.UnitName;

        // 클릭 이벤트
        Button btn = checkBox.GetComponent<Button>();
        if (btn == null)
            btn = checkBox.AddComponent<Button>();

        int capturedIndex = index;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => OnCandidateClicked(capturedIndex));
    }

    /// <summary>
    /// 체크박스 설정 (현재 유닛용)
    /// </summary>
    private void SetupCurrentUnitCheckBox(GameObject checkBox, Unit unit)
    {
        // Icon 찾기
        Image icon = checkBox.transform.Find("Image")?.GetComponent<Image>();
        if (icon == null)
            icon = checkBox.transform.Find("Icon")?.GetComponent<Image>();
        // 아이콘은 유닛에서 가져오기 어려우므로 기본값 유지

        // 이름 찾기
        TextMeshProUGUI nameText = checkBox.transform.Find("Text (TMP)")?.GetComponent<TextMeshProUGUI>();
        if (nameText == null)
            nameText = checkBox.GetComponentInChildren<TextMeshProUGUI>();
        if (nameText != null)
            nameText.text = unit.UnitName;
    }

    /// <summary>
    /// 후보 클릭
    /// </summary>
    private void OnCandidateClicked(int index)
    {
        if (UnitRecruitManager.Instance == null) return;

        var candidates = UnitRecruitManager.Instance.CurrentCandidates;
        if (index < 0 || index >= candidates.Count) return;

        selectedIndex = index;
        selectedCandidate = candidates[index];

        Debug.Log($"[UnitRecruitUI] 후보 선택: {selectedCandidate.UnitName}");

        // 선택 표시 업데이트
        UpdateSelectionVisual();

        // 상세 정보 표시
        ShowUnitDetails(selectedCandidate);
    }

    /// <summary>
    /// 선택 시각 업데이트
    /// </summary>
    private void UpdateSelectionVisual()
    {
        for (int i = 0; i < candidateCheckBoxes.Count; i++)
        {
            var checkBox = candidateCheckBoxes[i];
            if (checkBox == null) continue;

            // 선택된 것 강조 (배경색 변경 등)
            Image bg = checkBox.GetComponent<Image>();
            if (bg != null)
            {
                bg.color = (i == selectedIndex) ? new Color(0.3f, 0.6f, 0.9f, 1f) : Color.white;
            }
        }
    }

    /// <summary>
    /// 유닛 상세 정보 표시
    /// </summary>
    private void ShowUnitDetails(RecruitableUnit candidate)
    {
        if (unitSetPanel != null)
            unitSetPanel.SetActive(true);

        // 아이콘
        if (unitSetIcon != null && candidate.Icon != null)
            unitSetIcon.sprite = candidate.Icon;

        // 이름
        if (unitSetName != null)
            unitSetName.text = candidate.UnitName;

        // 특성 아이콘들
        ClearTraitIcons();
        if (unitTraitsParent != null && skillIconPrefab != null)
        {
            foreach (var trait in candidate.Traits)
            {
                GameObject traitIcon = Instantiate(skillIconPrefab, unitTraitsParent);

                // 아이콘 설정
                Image img = traitIcon.GetComponent<Image>();
                if (img == null)
                    img = traitIcon.transform.Find("Image")?.GetComponent<Image>();
                if (img != null && trait.Icon != null)
                    img.sprite = trait.Icon;

                traitIcons.Add(traitIcon);
            }
        }

        // 비용
        ClearPriceItems();
        if (priceParent != null && unitPricePrefab != null)
        {
            foreach (var cost in candidate.Costs)
            {
                GameObject priceItem = Instantiate(unitPricePrefab, priceParent);

                // 아이콘
                Image img = priceItem.transform.Find("Image")?.GetComponent<Image>();
                if (img != null && cost.Resource != null && cost.Resource.Icon != null)
                    img.sprite = cost.Resource.Icon;

                // 수량
                TextMeshProUGUI text = priceItem.transform.Find("Text (TMP)")?.GetComponent<TextMeshProUGUI>();
                if (text == null)
                    text = priceItem.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null)
                    text.text = cost.Amount.ToString();

                priceItems.Add(priceItem);
            }
        }

        // 고용 버튼 활성화 여부
        if (recruitButton != null)
            recruitButton.interactable = candidate.CanAfford();
    }

    /// <summary>
    /// 선택 초기화
    /// </summary>
    private void ClearSelection()
    {
        selectedCandidate = null;
        selectedIndex = -1;

        if (unitSetPanel != null)
            unitSetPanel.SetActive(false);

        ClearTraitIcons();
        ClearPriceItems();

        // 선택 시각 초기화
        foreach (var checkBox in candidateCheckBoxes)
        {
            if (checkBox == null) continue;
            Image bg = checkBox.GetComponent<Image>();
            if (bg != null)
                bg.color = Color.white;
        }
    }

    /// <summary>
    /// 고용 버튼 클릭
    /// </summary>
    private void OnRecruitButtonClicked()
    {
        if (selectedCandidate == null)
        {
            Debug.LogWarning("[UnitRecruitUI] 선택된 유닛이 없습니다!");
            return;
        }

        if (UnitRecruitManager.Instance != null)
        {
            UnitRecruitManager.Instance.RecruitUnit(selectedCandidate);
        }
    }

    /// <summary>
    /// 건너뛰기 버튼 클릭
    /// </summary>
    private void OnSkipButtonClicked()
    {
        if (UnitRecruitManager.Instance != null)
        {
            UnitRecruitManager.Instance.SkipRecruiting();
        }

        CloseUI();
    }

    // ==================== 정리 메서드 ====================

    private void ClearCandidateList()
    {
        foreach (var obj in candidateCheckBoxes)
        {
            if (obj != null) Destroy(obj);
        }
        candidateCheckBoxes.Clear();
    }

    private void ClearCurrentUnitList()
    {
        foreach (var obj in currentUnitCheckBoxes)
        {
            if (obj != null) Destroy(obj);
        }
        currentUnitCheckBoxes.Clear();
    }

    private void ClearTraitIcons()
    {
        foreach (var obj in traitIcons)
        {
            if (obj != null) Destroy(obj);
        }
        traitIcons.Clear();
    }

    private void ClearPriceItems()
    {
        foreach (var obj in priceItems)
        {
            if (obj != null) Destroy(obj);
        }
        priceItems.Clear();
    }

    private void ClearAllLists()
    {
        ClearCandidateList();
        ClearCurrentUnitList();
        ClearTraitIcons();
        ClearPriceItems();
    }

    // ==================== 테스트 ====================

    [ContextMenu("Test Show UI")]
    private void TestShowUI()
    {
        if (UnitRecruitManager.Instance != null)
        {
            UnitRecruitManager.Instance.GenerateTestCandidates();
        }
    }
}