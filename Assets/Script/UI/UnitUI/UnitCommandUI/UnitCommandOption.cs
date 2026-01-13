using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

/// <summary>
/// 개별 명령 옵션 UI
/// 프리팹 구조:
///   빈 오브젝트 (UnitCommandOption)
///     ㄴ Icon (Image)
///     ㄴ Label (TMP)
///     ㄴ HoverOverlay (Image, 호버 시 색상)
/// 
/// 기능:
///   - 호버 시: 아이콘 커짐 + 아이콘 색상 변경 + 텍스트 페이드인
///   - 클릭 시: 클릭 애니메이션 + 콜백
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class UnitCommandOption : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("=== UI 참조 ===")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private Image hoverOverlay;  // 호버 시 색상 오버레이

    [Header("=== 호버 설정 ===")]
    [SerializeField] private float hoverScale = 1.2f;
    [SerializeField] private float hoverDuration = 0.2f;
    [SerializeField] private Color normalIconColor = Color.white;
    [SerializeField] private Color hoverIconColor = new Color(1f, 0.85f, 0.5f);  // 노란빛

    [Header("=== 텍스트 설정 ===")]
    [SerializeField] private float normalTextAlpha = 0f;
    [SerializeField] private float hoverTextAlpha = 1f;

    [Header("=== 클릭 설정 ===")]
    [SerializeField] private float clickScale = 0.85f;
    [SerializeField] private float clickDuration = 0.1f;

    // 데이터
    private CommandOptionData data;
    private Action<CommandOptionData> onClickCallback;
    private RectTransform rectTransform;
    private RectTransform iconRectTransform;
    private Vector3 iconOriginalScale;
    private CanvasGroup canvasGroup;

    // ==================== 초기화 ====================

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();

        if (iconImage != null)
        {
            iconRectTransform = iconImage.GetComponent<RectTransform>();
            iconOriginalScale = iconRectTransform.localScale;
            iconImage.color = normalIconColor;
        }

        // 텍스트 초기 상태 (투명)
        if (labelText != null)
        {
            Color textColor = labelText.color;
            textColor.a = normalTextAlpha;
            labelText.color = textColor;
        }

        // 호버 오버레이 초기 상태
        if (hoverOverlay != null)
        {
            Color overlayColor = hoverOverlay.color;
            overlayColor.a = 0f;
            hoverOverlay.color = overlayColor;
        }
    }

    /// <summary>
    /// 옵션 초기화
    /// </summary>
    public void Initialize(CommandOptionData optionData, Action<CommandOptionData> onClick)
    {
        data = optionData;
        onClickCallback = onClick;

        // 텍스트 설정
        if (labelText != null)
        {
            labelText.text = optionData.DisplayName;
            // 텍스트 초기 알파값 설정
            Color textColor = labelText.color;
            textColor.a = normalTextAlpha;
            labelText.color = textColor;
        }

        // 아이콘 설정
        if (iconImage != null)
        {
            if (optionData.Icon != null)
            {
                iconImage.sprite = optionData.Icon;
            }
            iconImage.color = normalIconColor;
        }

        // 호버 오버레이 리셋
        if (hoverOverlay != null)
        {
            Color overlayColor = hoverOverlay.color;
            overlayColor.a = 0f;
            hoverOverlay.color = overlayColor;
        }
    }

    // ==================== 포인터 이벤트 ====================

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 아이콘 커지기
        if (iconRectTransform != null)
        {
            iconRectTransform.DOScale(iconOriginalScale * hoverScale, hoverDuration)
                .SetEase(Ease.OutBack)
                .SetUpdate(true);
        }

        // 아이콘 색상 변경
        if (iconImage != null)
        {
            iconImage.DOColor(hoverIconColor, hoverDuration).SetUpdate(true);
        }

        // 텍스트 페이드인
        if (labelText != null)
        {
            labelText.DOFade(hoverTextAlpha, hoverDuration).SetUpdate(true);
        }

        // 호버 오버레이
        if (hoverOverlay != null)
        {
            hoverOverlay.DOFade(0.3f, hoverDuration).SetUpdate(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 아이콘 원래 크기로
        if (iconRectTransform != null)
        {
            iconRectTransform.DOScale(iconOriginalScale, hoverDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);
        }

        // 아이콘 색상 복구
        if (iconImage != null)
        {
            iconImage.DOColor(normalIconColor, hoverDuration).SetUpdate(true);
        }

        // 텍스트 페이드아웃
        if (labelText != null)
        {
            labelText.DOFade(normalTextAlpha, hoverDuration).SetUpdate(true);
        }

        // 호버 오버레이 숨기기
        if (hoverOverlay != null)
        {
            hoverOverlay.DOFade(0f, hoverDuration).SetUpdate(true);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 클릭 애니메이션
        if (iconRectTransform != null)
        {
            Sequence clickSeq = DOTween.Sequence();
            clickSeq.Append(iconRectTransform.DOScale(iconOriginalScale * clickScale, clickDuration).SetEase(Ease.OutQuad));
            clickSeq.Append(iconRectTransform.DOScale(iconOriginalScale * hoverScale, clickDuration).SetEase(Ease.OutBack));
            clickSeq.SetUpdate(true);
            clickSeq.OnComplete(() =>
            {
                onClickCallback?.Invoke(data);
            });
        }
        else
        {
            onClickCallback?.Invoke(data);
        }
    }

    // ==================== 외부 접근 ====================

    public CommandOptionData Data => data;

    /// <summary>
    /// 강제 리셋 (재사용 시)
    /// </summary>
    public void ResetState()
    {
        if (iconRectTransform != null)
            iconRectTransform.localScale = iconOriginalScale;

        if (iconImage != null)
            iconImage.color = normalIconColor;

        if (labelText != null)
        {
            Color textColor = labelText.color;
            textColor.a = normalTextAlpha;
            labelText.color = textColor;
        }

        if (hoverOverlay != null)
        {
            Color overlayColor = hoverOverlay.color;
            overlayColor.a = 0f;
            hoverOverlay.color = overlayColor;
        }
    }
}