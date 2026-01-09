using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Text;
using System;

public class AIChatSystem : MonoBehaviour
{
    [Header("API Configuration")]
    public APIConfig apiConfig;

    [Header("Local AI Engines")]
    public LocalProfanityFilter localFilter;

    [Header("Settings")]
    public string targetLanguage = "English";

    private const string ModelName = "gemini-2.5-flash-lite";
    private const string ApiVersion = "v1beta";

    // [캐시 시스템] 중복 작업 방지
    private Dictionary<string, float> _toxicityCache = new Dictionary<string, float>();
    private Dictionary<string, string> _translationCache = new Dictionary<string, string>();

    // ==================== 1단계: 하이브리드 독성 검사 ====================
    public async UniTask<float> CheckToxicity(string text)
    {
        string normalizedText = text.Trim().Replace(" ", "");

        // 1. 캐시 확인
        if (_toxicityCache.TryGetValue(normalizedText, out float cachedScore))
        {
            Debug.Log($"<color=cyan>[AI_CHECK]</color> <b>캐시 재사용</b> | Score: {cachedScore}");
            return cachedScore;
        }

        // 2. 로컬 필터 확인
        if (localFilter != null && localFilter.IsBlacklisted(text))
        {
            Debug.Log("<color=yellow>[AI_CHECK]</color> <b>로컬 필터 차단</b> | Score: 1.0");
            _toxicityCache[normalizedText] = 1.0f;
            return 1.0f;
        }

        // 3. Gemini API 분석 (요청하신 강화된 프롬프트 적용)
        Debug.Log($"<color=#FF00FF>[AI_CHECK]</color> <b>Gemini API 독성 분석 중...</b>");

        string url = $"https://generativelanguage.googleapis.com/{ApiVersion}/models/{ModelName}:generateContent";

        // ★ 사용자 요청 지침 반영 프롬프트
        string prompt = "You are a professional game community moderator analyzing chat toxicity. " +
                        "Analyze the following Korean text and return ONLY a float number between 0.0 and 1.0. " +
                        "Use the following strict guidelines to determine the score:\n\n" +

                        "1. **Emphasis of Praise (Score: 0.1 ~ 0.2)**: " +
                        "If slang (e.g., 'ㅈㄴ', '개-') is used solely to emphasize a compliment (e.g., 'ㅈㄴ 잘해'), " +
                        "assign a very low score as it indicates amazement, not malice.\n" +

                        "2. **Slang with Praise (Score: 0.3 ~ 0.7)**: " +
                        "If stronger slang (e.g., 'ㅆㅂ', 'Tq') is mixed with praise (e.g., '와 ㅆㅂ ㅈㄴ 잘해'), " +
                        "assign a score between 0.3 and 0.7 depending on the vulgarity of the slang used, " +
                        "even if the intent is amazement.\n" +

                        "3. **Emphasis of Criticism/Insult (Score: 0.5 ~ 1.0)**: " +
                        "If slang is used to emphasize criticism, mocking, or blaming (e.g., 'ㅈㄴ 못하네', '개노답'), " +
                        "assign a score ABOVE 0.5 to trigger censorship.\n\n" +

                        $"Text to analyze: \"{text}\"";

        float apiScore = await SendGeminiRequest<float>(url, prompt, (result) => {
            return float.TryParse(result.Trim(), out float score) ? score : 0f;
        });

        _toxicityCache[normalizedText] = apiScore;
        LogAnalysis(text, apiScore);
        return apiScore;
    }

    // ==================== 2단계: 번역 시스템 (캐싱 적용) ====================
    public async UniTask<string> TranslateText(string text)
    {
        string normalizedText = text.Trim();

        if (_translationCache.TryGetValue(normalizedText, out string cachedTranslation))
        {
            Debug.Log($"<color=cyan>[AI_TRANS]</color> <b>번역 캐시 재사용</b>");
            return cachedTranslation;
        }

        Debug.Log($"<color=#FF00FF>[AI_TRANS]</color> <b>Gemini API 번역 중...</b>");
        string url = $"https://generativelanguage.googleapis.com/{ApiVersion}/models/{ModelName}:generateContent";

        string prompt = $"Translate the following text into {targetLanguage}. " +
                        "Maintain the original gaming nuance and tone. " +
                        $"Return ONLY the translated text. Text: \"{text}\"";

        string translated = await SendGeminiRequest<string>(url, prompt, (result) => result.Trim());

        if (!string.IsNullOrEmpty(translated))
        {
            _translationCache[normalizedText] = translated;
            return translated;
        }

        return "[Translation Error]";
    }

    // ==================== 공통 유틸리티 ====================
    private async UniTask<T> SendGeminiRequest<T>(string url, string prompt, Func<string, T> parser)
    {
        var payload = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
        string json = JsonConvert.SerializeObject(payload);

        using var request = CreateRequest(url, json);
        await request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonConvert.DeserializeObject<GeminiResponse>(request.downloadHandler.text);
            if (response?.candidates != null && response.candidates.Length > 0)
            {
                string resultText = response.candidates[0].content.parts[0].text;
                return parser(resultText);
            }
        }

        Debug.LogError($"[AIChatSystem] API 에러: {request.downloadHandler.text}");
        return default;
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
        string color = score >= 0.5f ? "red" : "green";
        Debug.Log($"<color=#00FF00>[AI_LOG]</color> Toxicity: <color={color}>{score * 100:F1}%</color> | Text: {rawText}");
    }

    [Serializable]
    public class GeminiResponse { public GeminiCandidate[] candidates; }
    public class GeminiCandidate { public GeminiContent content; }
    public class GeminiContent { public GeminiPart[] parts; }
    public class GeminiPart { public string text; }
}




/*
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
 */