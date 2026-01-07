using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic; // Dictionary 사용을 위해 필요
using System.Text;
using System;

public class AIChatSystem : MonoBehaviour
{
    [Header("API Configuration")]
    public APIConfig apiConfig;

    [Header("Local AI Engines")]
    [Tooltip("로컬 비속어 필터 컴포넌트 연결")]
    public LocalProfanityFilter localFilter;

    [Header("Settings")]
    public string targetLanguage = "English";

    // Gemini 2.5 Flash-Lite: 무료 한도 일일 1,000회 제공
    private const string ModelName = "gemini-2.5-flash-lite";
    private const string ApiVersion = "v1beta";

    // [캐시 시스템] 중복 문장 검사 방지
    private Dictionary<string, float> _toxicityCache = new Dictionary<string, float>();

    // ==================== 1단계: 하이브리드 독성 검사 ====================

    public async UniTask<float> CheckToxicity(string text)
    {
        string normalizedText = text.Trim().Replace(" ", "");

        // 1. [캐시 확인]
        if (_toxicityCache.TryGetValue(normalizedText, out float cachedScore))
        {
            // [LOG] API 미사용 (캐시)
            Debug.Log($"<color=cyan>[AI_CHECK]</color> <b>캐시 재사용</b> (API 미사용) | Text: {text} | Score: {cachedScore}");
            return cachedScore;
        }

        // 2. [로컬 필터 확인]
        if (localFilter != null && localFilter.IsBlacklisted(text))
        {
            // [LOG] API 미사용 (로컬 필터)
            Debug.Log($"<color=yellow>[AI_CHECK]</color> <b>로컬 필터 차단</b> (API 미사용) | Text: {text} | Score: 1.0");
            _toxicityCache[normalizedText] = 1.0f;
            return 1.0f;
        }

        // 3. [API 호출] 위 두 단계를 통과했을 때만 실행
        Debug.Log($"<color=#FF00FF>[AI_CHECK]</color> <b>Gemini API 호출 중...</b> | Text: {text}");

        float apiScore = await CallGeminiToxicityAPI(text);

        // 결과 저장
        _toxicityCache[normalizedText] = apiScore;

        // [LOG] API 사용 완료
        Debug.Log($"<color=#00FF00>[AI_CHECK]</color> <b>API 분석 완료</b> (API 사용됨) | Text: {text} | Score: {apiScore}");

        return apiScore;
    }

    private async UniTask<float> CallGeminiToxicityAPI(string text)
    {
        string url = $"https://generativelanguage.googleapis.com/{ApiVersion}/models/{ModelName}:generateContent";

        string prompt = 
            "You are a professional game community moderator analyzing chat toxicity. " +
            "Analyze the following Korean text and return ONLY a float number between 0.0 and 1.0. " +
            "Use the following strict guidelines to determine the score:\n\n" +

            "1. **Emphasis of Praise (Score: 0.1 ~ 0.2)**: " +
            "If slang (e.g., 'ㅈㄴ', '개-') is used solely to emphasize a compliment or skillful play (e.g., 'ㅈㄴ 잘해'), " +
            "assign a very low score as it indicates amazement, not malice.\n" +

            "2. **Excessive Slang with Praise (Score: 0.3 ~ 0.49)**: " +
            "If stronger slang (e.g., 'ㅆㅂ', 'Tq') is mixed with praise (e.g., '와 ㅆㅂ ㅈㄴ 잘해'), " +
            "increase the score due to vulgarity, but keep it BELOW 0.5 if the primary intent is still amazement.\n" +

            "3. **Emphasis of Criticism/Insult (Score: 0.5 ~ 0.7)**: " +
            "If slang is used to emphasize criticism, mocking, or blaming (e.g., 'ㅈㄴ 못하네', '개노답'), " +
            "assign a score ABOVE 0.5 to trigger censorship, as it serves to attack another player.\n" +

            "4. **Direct Hate & Harassment (Score: 0.8 ~ 1.0)**: " +
            "Direct insults, hate speech, or severe profanity intended to offend. \n\n" +

            $"Text to analyze: \"{text}\"";

        var payload = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
        string json = JsonConvert.SerializeObject(payload);

        using var request = CreateRequest(url, json);
        await request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonConvert.DeserializeObject<GeminiResponse>(request.downloadHandler.text);
            string result = response.candidates[0].content.parts[0].text.Trim();

            if (float.TryParse(result, out float score))
            {
                LogAnalysis(text, score);
                return score;
            }
        }
        else
        {
            Debug.LogError($"[AIChatSystem] API 에러: {request.downloadHandler.text}");
        }
        return 0f;
    }

    // ==================== 2단계: 번역 시스템 ====================

    public async UniTask<string> TranslateText(string text)
    {
        string url = $"https://generativelanguage.googleapis.com/{ApiVersion}/models/{ModelName}:generateContent";

        string prompt = $"Translate the following text into {targetLanguage}. Return ONLY the translated text. Text: \"{text}\"";

        var payload = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
        string json = JsonConvert.SerializeObject(payload);

        using var request = CreateRequest(url, json);
        await request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonConvert.DeserializeObject<GeminiResponse>(request.downloadHandler.text);
            return response.candidates[0].content.parts[0].text.Trim();
        }

        return "[Translation Error]";
    }

    private UnityWebRequest CreateRequest(string url, string json)
    {
        var request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("x-goog-api-key", apiConfig.geminiKey.Trim());
        return request;
    }

    private void LogAnalysis(string rawText, float score)
    {
        Debug.Log($"<color=#00FF00>[AI_LOG]</color> Toxicity: {score * 100:F1}% | Text: {rawText}");
    }

    // ========== JSON 파싱을 위한 보조 클래스들 (클래스 내부로 이동) ==========
    [Serializable]
    public class GeminiResponse { public GeminiCandidate[] candidates; }
    public class GeminiCandidate { public GeminiContent content; }
    public class GeminiContent { public GeminiPart[] parts; }
    public class GeminiPart { public string text; }
} // AIChatSystem 클래스 끝

// 메시는 클래스 외부에서 사용될 수 있으므로 별도 정의
[Serializable]
public struct ChatMessage
{
    public string Sender;
    public string RawContent;
    public string TranslatedContent;
    public float ToxicityScore;
    public bool IsFiltered;
}