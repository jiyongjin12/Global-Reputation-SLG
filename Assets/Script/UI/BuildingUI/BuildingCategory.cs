using UnityEngine;

/// <summary>
/// 건물 카테고리 (UI 탭 분류용)
/// </summary>
public enum BuildingCategory
{
    [InspectorName("일반")]
    General,        // 일반 건물

    [InspectorName("농사")]
    Farming,        // 농사 관련

    [InspectorName("생산")]
    Production,     // 생산 시설

    [InspectorName("저장")]
    Storage,        // 저장 시설

    [InspectorName("장식")]
    Decoration,     // 장식물

    [InspectorName("군사")]
    Military        // 군사 시설
}

/// <summary>
/// 카테고리 정보 (UI 표시용)
/// </summary>
public static class BuildingCategoryInfo
{
    public static string GetDisplayName(BuildingCategory category)
    {
        return category switch
        {
            BuildingCategory.General => "일반",
            BuildingCategory.Farming => "농사",
            BuildingCategory.Production => "생산",
            BuildingCategory.Storage => "저장",
            BuildingCategory.Decoration => "장식",
            BuildingCategory.Military => "군사",
            _ => category.ToString()
        };
    }

    public static Color GetCategoryColor(BuildingCategory category)
    {
        return category switch
        {
            BuildingCategory.General => new Color(0.6f, 0.6f, 0.6f),
            BuildingCategory.Farming => new Color(0.4f, 0.8f, 0.4f),
            BuildingCategory.Production => new Color(0.8f, 0.6f, 0.2f),
            BuildingCategory.Storage => new Color(0.5f, 0.5f, 0.8f),
            BuildingCategory.Decoration => new Color(0.8f, 0.5f, 0.8f),
            BuildingCategory.Military => new Color(0.8f, 0.3f, 0.3f),
            _ => Color.white
        };
    }
}