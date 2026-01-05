using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 카테고리 UI 컨트롤
/// - 배경, 제목, 수집 현황 표시
/// - 건물 버튼 컨테이너 관리
/// </summary>
public class CategoryUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TextMeshProUGUI categoryNameText;
    [SerializeField] private TextMeshProUGUI collectionText;      // "3/5 수집" 등
    [SerializeField] private Transform buttonContainer;           // Grid Layout Group이 있는 곳

    [Header("Settings")]
    [SerializeField] private Color defaultBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);

    private BuildingCategory category;
    private int totalCount;
    private int collectedCount; // 나중에 수집 시스템 연동

    public Transform ButtonContainer => buttonContainer;
    public BuildingCategory Category => category;

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize(BuildingCategory category, int buildingCount)
    {
        this.category = category;
        this.totalCount = buildingCount;
        this.collectedCount = 0; // 나중에 수집 시스템에서 가져옴

        // 카테고리 이름 설정
        if (categoryNameText != null)
        {
            categoryNameText.text = BuildingCategoryInfo.GetDisplayName(category);
        }

        // 배경색 설정 (카테고리별 색상)
        if (backgroundImage != null)
        {
            Color catColor = BuildingCategoryInfo.GetCategoryColor(category);
            backgroundImage.color = new Color(catColor.r * 0.3f, catColor.g * 0.3f, catColor.b * 0.3f, 0.9f);
        }

        // 수집 현황 (나중에 구현)
        UpdateCollectionText();

        gameObject.name = $"Category_{category}";
    }

    /// <summary>
    /// 수집 현황 업데이트
    /// </summary>
    public void UpdateCollectionText()
    {
        if (collectionText != null)
        {
            // 나중에 수집 시스템 연동
            // collectionText.text = $"{collectedCount}/{totalCount} 수집";
            collectionText.text = $"{totalCount}개";
        }
    }

    /// <summary>
    /// 수집 카운트 설정
    /// </summary>
    public void SetCollectedCount(int count)
    {
        collectedCount = count;
        UpdateCollectionText();
    }
}