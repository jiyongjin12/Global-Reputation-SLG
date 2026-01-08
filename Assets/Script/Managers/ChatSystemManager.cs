using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

public class ChatSystemManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField chatInput;
    public Button sendButton;
    public RectTransform chatContent;
    public ScrollRect scrollRect;
    public GameObject messagePrefab;

    [Header("AI Engine")]
    public AIChatSystem aiSystem;

    void Start()
    {
        chatInput.onEndEdit.AddListener((text) => {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnSend();
        });

        sendButton.onClick.AddListener(OnSend);
    }

    public void OnSend()
    {
        string text = chatInput.text;
        if (string.IsNullOrWhiteSpace(text)) return;

        ProcessAndSendMessage(text).Forget();

        chatInput.text = "";
        chatInput.ActivateInputField();
    }

    private async UniTaskVoid ProcessAndSendMessage(string rawText)
    {
        // [1단계] 하이브리드 독성 검사 (로컬 -> 캐시 -> API)
        float toxicityScore = await aiSystem.CheckToxicity(rawText);
        bool isFiltered = toxicityScore > 0.5f;

        // [2단계] 강화된 평판 포인트 연동 ★
        UpdateReputationSubPoints(toxicityScore);

        // [3단계] 메시지 생성
        GameObject newMsgObj = Instantiate(messagePrefab, chatContent);
        var uiItem = newMsgObj.GetComponent<ChatUIItem>();
        uiItem.Initialize(aiSystem, "Player", rawText, isFiltered);

        // [4단계] 하단 스크롤
        await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }

    /// <summary>
    /// AI 독성 점수에 따라 서브 포인트를 가감합니다.
    /// </summary>
    private void UpdateReputationSubPoints(float score)
    {
        if (ReputationManager.Instance == null) return;

        float pointChange = 0f;

        // 1. 긍정적인 채팅 (0.5 이하): +1 포인트 (10번 말해야 평판 1 상승)
        if (score <= 0.5f)
        {
            pointChange = 1.0f;
        }
        // 2. 심각한 독성 (0.8 이상): -10 포인트 (한 번만 말해도 평판 1 즉시 하락)
        else if (score >= 0.9f)
        {
            pointChange = -10.0f;
        }
        // 3. 일반적인 비난 (0.5 이상): -3.3 포인트 (약 3번 말하면 평판 1 하락)
        else if (score >= 0.5f)
        {
            pointChange = -3.34f;
        }

        if (pointChange != 0)
        {
            ReputationManager.Instance.AddReputationPoints(pointChange);
        }
    }
}