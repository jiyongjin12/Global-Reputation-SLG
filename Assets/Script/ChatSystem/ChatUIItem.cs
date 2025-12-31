using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public class ChatUIItem : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI senderText;
    public TextMeshProUGUI contentText;
    public TextMeshProUGUI btnLabel;
    public Button translateBtn;

    private AIChatSystem _aiSystem;
    private string _originalText;
    private string _translatedText = "";
    private bool _isFiltered;

    private bool _isShowingOriginal = true; // 현재 원본을 보고 있는지 여부
    private bool _isTranslating = false;    // 현재 번역 API 호출 중인지 여부

    // 초기 생성 시 데이터 설정
    public void Initialize(AIChatSystem ai, string sender, string original, bool isFiltered)
    {
        _aiSystem = ai;
        _originalText = original;
        _isFiltered = isFiltered;

        senderText.text = sender;
        UpdateDisplay();

        // 버튼 클릭 이벤트 연결
        translateBtn.onClick.RemoveAllListeners();
        translateBtn.onClick.AddListener(() => OnTranslateClick().Forget());
    }

    private async UniTaskVoid OnTranslateClick()
    {
        if (_isTranslating) return; // 이미 번역 중이면 무시

        // 1. 이미 번역된 텍스트가 있다면 서버 호출 없이 바로 토글
        if (!string.IsNullOrEmpty(_translatedText))
        {
            _isShowingOriginal = !_isShowingOriginal;
            UpdateDisplay();
            return;
        }

        // 2. 처음 번역하는 경우: AI에게 번역 요청
        _isTranslating = true;
        btnLabel.text = "Loading..."; // 로딩 상태 표시

        _translatedText = await _aiSystem.TranslateText(_originalText);

        _isTranslating = false;
        _isShowingOriginal = false; // 번역이 완료되면 바로 번역본 표시
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        // 표시할 텍스트 결정 (원본 vs 번역본)
        string baseText = _isShowingOriginal ? _originalText : _translatedText;

        // 검열 여부에 따른 서식 적용 (기획서 5페이지 반영)
        if (_isFiltered)
        {
            contentText.text = $"<color=red><b>[Censored]</b></color> {baseText}";
        }
        else
        {
            contentText.text = baseText;
        }

        // 버튼 라벨 갱신 (이터널 리턴 스타일)
        if (string.IsNullOrEmpty(_translatedText))
        {
            btnLabel.text = "<번역>";
        }
        else
        {
            btnLabel.text = _isShowingOriginal ? "<번역>" : "<원본>";
        }
    }
}