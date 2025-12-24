using UnityEngine;

[ExecuteAlways]
public class GridLineSet : MonoBehaviour
{
    [Header("Child References")]
    public GameObject visualPlane;

    [Header("Physical Grid Settings")]
    public Vector2 gridTotalSize = new Vector2(10f, 10f); // 월드 크기 (m)
    public Vector2 cellSize = new Vector2(1f, 1f);       // 칸 크기 (m)

    [Header("Visual Styles")]
    public Color gridColor = Color.cyan;
    [Range(0f, 0.5f)] public float gap = 0.1f;        // 칸 사이 간격
    [Range(0f, 0.5f)] public float thickness = 0.05f; // 테두리 두께

    private Renderer _planeRenderer;
    private MaterialPropertyBlock _propBlock;

    // Shader Graph Reference Names (반드시 쉐이더 내 Reference와 일치해야 함)
    private static readonly int PropDefaultScale = Shader.PropertyToID("_DefaultScale");
    private static readonly int PropColor = Shader.PropertyToID("_Color");
    private static readonly int PropSize = Shader.PropertyToID("_Size");
    private static readonly int PropThickness = Shader.PropertyToID("_Thickness");
    private static readonly int PropGap = Shader.PropertyToID("_Gap");

    void OnEnable() => SyncGrid();
    void OnValidate() => SyncGrid(); // 인스펙터 수정 시 즉시 반영

    public void SyncGrid()
    {
        if (visualPlane == null) return;

        if (_planeRenderer == null) _planeRenderer = visualPlane.GetComponent<Renderer>();
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        if (_planeRenderer == null) return;

        // 1. 물리적 Plane 크기 설정 (유니티 Plane 10x10 기준)
        visualPlane.transform.localScale = new Vector3(gridTotalSize.x / 10f, 1f, gridTotalSize.y / 10f);

        _planeRenderer.GetPropertyBlock(_propBlock);

        // Tiling 계산: 실제 월드 크기 / 칸 크기
        Vector2 tilingValues = new Vector2(gridTotalSize.x / cellSize.x, gridTotalSize.y / cellSize.y);

        _propBlock.SetVector(PropDefaultScale, Vector2.one);
        _propBlock.SetVector(PropSize, tilingValues);
        _propBlock.SetColor(PropColor, gridColor);
        _propBlock.SetFloat(PropThickness, thickness);
        _propBlock.SetFloat(PropGap, gap);

        _planeRenderer.SetPropertyBlock(_propBlock);
    }


    public void UpdateReputationStyle(Color color, float newGap)
    {
        gridColor = color;
        gap = newGap;
        SyncGrid();
    }
}

