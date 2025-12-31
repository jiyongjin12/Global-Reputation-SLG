using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public class ChatSystemManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField chatInput;
    public RectTransform chatContent;
    public GameObject messagePrefab;
    public ScrollRect scrollRect;

    [Header("AI Engine")]
    public AIChatSystem aiSystem;

    void Start()
    {
        chatInput.onEndEdit.AddListener(OnInputSubmit);
    }

    private void OnInputSubmit(string text)
    {
        if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        ProcessAndSendMessage(text).Forget();
        chatInput.text = "";
        chatInput.ActivateInputField();
    }

    private async UniTaskVoid ProcessAndSendMessage(string rawText)
    {
        // AI 분석 실행 (번역 + 독성 검사)
        ChatMessage data = await aiSystem.ProcessChatMessage("Player", rawText);

        string displayOriginal = data.RawContent;
        string displayTranslated = data.TranslatedContent;

        // 기획서 5페이지 연동: 독성 수치에 따른 검열 처리
        if (data.IsFiltered)
        {
            displayTranslated = "<color=red><b>[부적절한 내용으로 검열됨]</b></color>";
            // 여기서 ReputationSystem.Instance.AddScore(-10) 등을 호출하여 평판에 반영 가능
        }

        // 메시지 UI 생성 및 데이터 할당
        GameObject newMsgObj = Instantiate(messagePrefab, chatContent);
        var uiItem = newMsgObj.GetComponent<ChatUIItem>();
        uiItem.SetMessage(data.Sender, displayOriginal, displayTranslated);

        // 레이아웃 갱신 후 하단으로 스크롤
        await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
        scrollRect.verticalNormalizedPosition = 0f;
    }
}