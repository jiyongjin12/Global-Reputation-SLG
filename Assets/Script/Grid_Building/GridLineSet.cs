using UnityEngine;

[ExecuteAlways]
public class GridLineSet : MonoBehaviour
{
    [Header("Child References")]
    public GameObject visualPlane;

    [Header("Physical Grid Settings")]
    public Vector2 gridTotalSize = new Vector2(10f, 10f);
    public Vector2 cellSize = new Vector2(1f, 1f);

    [Header("Visual Styles")]
    public Color gridColor = Color.cyan;
    [Range(0f, 1f)] public float thickness = 0.1f;

    private Renderer _planeRenderer;
    private MaterialPropertyBlock _propBlock;

    private static readonly int PropDefaultScale = Shader.PropertyToID("_DefaultScale");
    private static readonly int PropColor = Shader.PropertyToID("_Color");
    private static readonly int PropSize = Shader.PropertyToID("_Size");
    private static readonly int PropThickness = Shader.PropertyToID("_Thickness");

    void OnValidate() { SyncGrid(); }
    void OnEnable() { SyncGrid(); }

    public void SyncGrid()
    {
        if (visualPlane == null) return;
        if (_planeRenderer == null) _planeRenderer = visualPlane.GetComponent<Renderer>();
        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        if (_planeRenderer == null) return;

        
        visualPlane.transform.localScale = new Vector3(gridTotalSize.x / 10f, 1f, gridTotalSize.y / 10f);

        // 2. 쉐이더 속성 계산
        _planeRenderer.GetPropertyBlock(_propBlock);

        // 눈금 크기를 일정하게 유지하려면 아래 수식을 사용
        // Tiling = Object Scale * DefaultScale * Size

        _propBlock.SetVector(PropDefaultScale, Vector2.one); // 기준값 1로 고정 

        // 실제 계산식: Size = 10 / cellSize
        // gridTotalSize가 아무리 커져도 눈금은 cellSize(m) 간격을 유지
        float tilingX = 10f / cellSize.x;
        float tilingY = 10f / cellSize.y;
        _propBlock.SetVector(PropSize, new Vector2(tilingX, tilingY));

        _propBlock.SetColor(PropColor, gridColor);
        _propBlock.SetFloat(PropThickness, thickness);
        _planeRenderer.SetPropertyBlock(_propBlock);
    }
}

