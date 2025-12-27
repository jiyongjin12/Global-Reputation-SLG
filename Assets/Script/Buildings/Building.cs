using System;
using UnityEngine;

/// <summary>
/// 배치된 건물에 붙는 컴포넌트
/// 건설 진행 상황 및 상태 관리
/// </summary>
public class Building : MonoBehaviour
{
    [SerializeField] private ObjectData data;
    [SerializeField] private BuildingState currentState = BuildingState.Blueprint;

    private float currentConstructionWork = 0f;
    private GameObject currentVisual;
    private bool isInitialized = false;

    // 이벤트
    public event Action<Building> OnConstructionComplete;
    public event Action<Building, float> OnConstructionProgress; // (building, progress 0~1)

    // Properties
    public ObjectData Data => data;
    public BuildingState CurrentState => currentState;
    public float ConstructionProgress
    {
        get
        {
            if (data == null) return 0f;
            return data.ConstructionWorkRequired > 0
                ? currentConstructionWork / data.ConstructionWorkRequired
                : 1f;
        }
    }
    public bool NeedsConstruction => currentState == BuildingState.Blueprint;
    public Vector3Int GridPosition { get; private set; }
    public bool IsInitialized => isInitialized;

    /// <summary>
    /// 건물 초기화
    /// </summary>
    public void Initialize(ObjectData objectData, Vector3Int gridPos, bool instantBuild = false)
    {
        if (objectData == null)
        {
            Debug.LogError("[Building] Initialize failed: objectData is null!");
            return;
        }

        data = objectData;
        GridPosition = gridPos;
        isInitialized = true;

        if (instantBuild || data.ConstructionWorkRequired <= 0)
        {
            CompleteConstruction();
        }
        else
        {
            SetState(BuildingState.Blueprint);
            UpdateVisual();
        }
    }

    /// <summary>
    /// 유닛이 건설 작업 수행
    /// </summary>
    /// <param name="workAmount">작업량</param>
    /// <returns>건설 완료 여부</returns>
    public bool DoConstructionWork(float workAmount)
    {
        if (data == null) return true;  // 데이터 없으면 완료 처리

        if (currentState == BuildingState.Completed)
            return true;

        if (currentState == BuildingState.Blueprint)
        {
            SetState(BuildingState.UnderConstruction);
            Debug.Log($"[Building] {data.Name} 건설 시작!");
        }

        currentConstructionWork += workAmount;

        // 디버그: 건설 진행 상황
        Debug.Log($"[Building] {data.Name} 건설 중: {currentConstructionWork:F1}/{data.ConstructionWorkRequired} ({ConstructionProgress * 100:F0}%)");

        OnConstructionProgress?.Invoke(this, ConstructionProgress);

        if (currentConstructionWork >= data.ConstructionWorkRequired)
        {
            CompleteConstruction();
            return true;
        }

        return false;
    }

    private void CompleteConstruction()
    {
        if (data != null)
        {
            currentConstructionWork = data.ConstructionWorkRequired;
            Debug.Log($"[Building] {data.Name} construction completed!");
        }

        SetState(BuildingState.Completed);
        UpdateVisual();
        OnConstructionComplete?.Invoke(this);
    }

    private void SetState(BuildingState newState)
    {
        currentState = newState;
    }

    private void UpdateVisual()
    {
        // 기존 비주얼 제거
        if (currentVisual != null)
        {
            Destroy(currentVisual);
        }

        // 새 비주얼 생성
        GameObject prefab = currentState == BuildingState.Completed
            ? data.Prefab
            : (data.BlueprintPrefab ?? data.Prefab);  // Blueprint 없으면 일반 Prefab 사용

        if (prefab != null)
        {
            currentVisual = Instantiate(prefab, transform);
            currentVisual.transform.localPosition = Vector3.zero;
        }
    }

    /// <summary>
    /// 건물 철거
    /// </summary>
    public void Demolish()
    {
        // TODO: 자원 일부 환불?
        Destroy(gameObject);
    }
}