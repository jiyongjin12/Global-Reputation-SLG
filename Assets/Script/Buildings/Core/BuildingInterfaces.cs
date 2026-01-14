using UnityEngine;
using System;
using System.Collections.Generic;

// ============================================================
// 건물 시스템 인터페이스
// 컴포넌트 기반 설계로 여러 인터페이스를 조합하여 건물 기능 구현
// ============================================================

/// <summary>
/// 작업 가능한 건물 (Unit이 와서 작업할 수 있음)
/// 예: 농경지, 제작대, 주방
/// </summary>
public interface IWorkstation
{
    /// <summary>현재 작업자가 있는지</summary>
    bool IsOccupied { get; }

    /// <summary>작업자가 서 있을 위치</summary>
    Transform WorkPoint { get; }

    /// <summary>현재 작업자</summary>
    Unit CurrentWorker { get; }

    /// <summary>작업 가능한 상태인지 (재료 충족, 작업 대기열 있음 등)</summary>
    bool CanStartWork { get; }

    /// <summary>작업 타입</summary>
    WorkTaskType TaskType { get; }

    /// <summary>작업자 배정</summary>
    bool AssignWorker(Unit worker);

    /// <summary>작업자 해제</summary>
    void ReleaseWorker();

    /// <summary>작업 시작</summary>
    void StartWork();

    /// <summary>작업 진행 (Unit이 호출, 작업량 반환)</summary>
    float DoWork(float workAmount);

    /// <summary>작업 완료</summary>
    void CompleteWork();

    /// <summary>작업 취소</summary>
    void CancelWork();

    /// <summary>작업 완료 이벤트</summary>
    event Action<IWorkstation> OnWorkCompleted;

    /// <summary>작업 가능 상태 변경 이벤트</summary>
    event Action<IWorkstation> OnWorkAvailable;
}

/// <summary>
/// 저장 가능한 건물 (아이템 보관)
/// 예: 보관함, 창고
/// </summary>
public interface IStorage
{
    /// <summary>저장된 아이템 목록</summary>
    IReadOnlyList<StoredResource> StoredItems { get; }

    /// <summary>최대 용량 (-1이면 무제한)</summary>
    int MaxCapacity { get; }

    /// <summary>현재 저장량</summary>
    int CurrentAmount { get; }

    /// <summary>꽉 찼는지</summary>
    bool IsFull { get; }

    /// <summary>아이템 추가</summary>
    bool AddItem(ResourceItemSO item, int amount);

    /// <summary>아이템 제거</summary>
    bool RemoveItem(ResourceItemSO item, int amount);

    /// <summary>특정 아이템 보유량</summary>
    int GetItemCount(ResourceItemSO item);

    /// <summary>특정 아이템이 있는지</summary>
    bool HasItem(ResourceItemSO item, int amount = 1);

    /// <summary>내용물 변경 이벤트</summary>
    event Action<IStorage> OnStorageChanged;
}

/// <summary>
/// 수확 가능한 건물 (시간이 지나면 결과물 생성)
/// 예: 농경지, 과수원
/// </summary>
public interface IHarvestable
{
    /// <summary>수확 가능한 상태인지</summary>
    bool IsReadyToHarvest { get; }

    /// <summary>성장 진행도 (0~1)</summary>
    float GrowthProgress { get; }

    /// <summary>현재 심어진 작물/씨앗</summary>
    ResourceItemSO CurrentCrop { get; }

    /// <summary>심기</summary>
    bool Plant(ResourceItemSO seed);

    /// <summary>수확</summary>
    List<ResourceItemSO> Harvest();

    /// <summary>수확 준비 완료 이벤트</summary>
    event Action<IHarvestable> OnReadyToHarvest;

    /// <summary>심기 완료 이벤트</summary>
    event Action<IHarvestable> OnPlanted;
}

/// <summary>
/// 상호작용 가능한 건물 (플레이어 클릭해서 UI 열기)
/// </summary>
public interface IInteractable
{
    /// <summary>상호작용 가능한지</summary>
    bool CanInteract { get; }

    /// <summary>상호작용 (클릭)</summary>
    void Interact();

    /// <summary>상호작용 UI 타입</summary>
    BuildingUIType UIType { get; }
}

/// <summary>
/// 건물 UI 타입
/// </summary>
public enum BuildingUIType
{
    None,
    Info,       // 정보만 표시
    Recipe,     // 레시피 선택 (요리)
    Storage,    // 저장소
    Farming,    // 농경
    Production, // 제작
}

/// <summary>
/// 워크스테이션 작업 타입
/// </summary>
public enum WorkTaskType
{
    None,
    Farming,            // 농업
    Crafting,           // 제작
    Cooking,            // 요리
    Mining,             // 채굴
    Woodcutting,        // 목공
    Hauling,            // 운송
    Storing,            // 저장
}

// StoredResource는 ResourceManager.cs에 정의되어 있음