using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public class ChatSystemManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField chatInput;
    public Button sendButton;        // 전송(엔터) 버튼
    public RectTransform chatContent; // ScrollView -> Viewport -> Content
    public ScrollRect scrollRect;    // 자동 스크롤용
    public GameObject messagePrefab; // ChatUIItem 스크립트가 붙은 프리팹

    [Header("AI Engine")]
    public AIChatSystem aiSystem;    // AIChatSystem 연결

    void Start()
    {
        // 1. 엔터키 입력 이벤트 연결
        chatInput.onEndEdit.AddListener((text) => {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnSend();
        });

        // 2. 전송 버튼 클릭 이벤트 연결
        sendButton.onClick.AddListener(OnSend);
    }

    public void OnSend()
    {
        string text = chatInput.text;
        if (string.IsNullOrWhiteSpace(text)) return;

        // 비동기로 메시지 처리 프로세스 시작
        ProcessAndSendMessage(text).Forget();

        // 입력창 초기화 및 다시 포커스
        chatInput.text = "";
        chatInput.ActivateInputField();
    }

    private async UniTaskVoid ProcessAndSendMessage(string rawText)
    {
        // [1단계] 독성 검사만 먼저 수행 (번역보다 훨씬 빠름)
        float toxicityScore = await aiSystem.CheckToxicity(rawText);
        bool isFiltered = toxicityScore > 0.5f;

        // [2단계] 메시지 객체 생성 (번역은 아직 하지 않은 상태)
        GameObject newMsgObj = Instantiate(messagePrefab, chatContent);
        var uiItem = newMsgObj.GetComponent<ChatUIItem>();

        // UI 아이템 초기화 (AI 시스템 참조를 넘겨줌)
        uiItem.Initialize(aiSystem, "Player", rawText, isFiltered);

        // [3단계] 레이아웃 갱신 후 하단으로 자동 스크롤
        await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
        scrollRect.verticalNormalizedPosition = 0f;
    }
}