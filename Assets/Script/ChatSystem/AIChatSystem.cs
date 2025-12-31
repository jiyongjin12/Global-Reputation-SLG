using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System.Text;
using System;

public class AIChatSystem : MonoBehaviour
{
    [Header("API Configuration")]
    public APIConfig apiConfig;

    [Header("Settings")]
    public string targetLanguage = "English";

    // ★ Flash-Lite = 무료 한도 1,000/일 (가장 높음)
    // 2025.12 기준 2.5 Flash는 20/일로 줄었음
    private const string ModelName = "gemini-2.5-flash-lite";
    private const string ApiVersion = "v1beta";

    // [1단계: 독성 검사]
    public async UniTask<float> CheckToxicity(string text)
    {
        // 공식 문서 형식: v1beta/models/{model}:generateContent
        string url = $"https://generativelanguage.googleapis.com/{ApiVersion}/models/{ModelName}:generateContent";

        string prompt = $"Analyze the toxicity of the following text and return only a float number between 0.0 and 1.0. Text: \"{text}\"";

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
            Debug.LogError($"[AIChatSystem] API 에러!\nURL: {url}\n응답: {request.downloadHandler.text}");
        }
        return 0f;
    }

    // [2단계: 번역]
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

        Debug.LogError($"[AIChatSystem] 번역 에러: {request.downloadHandler.text}");
        return "[Translation Error]";
    }

    private UnityWebRequest CreateRequest(string url, string json)
    {
        var request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        // ★ 공식 문서 방식: 헤더에 API 키 전달
        request.SetRequestHeader("x-goog-api-key", apiConfig.geminiKey.Trim());
        return request;
    }

    private void LogAnalysis(string rawText, float score)
    {
        Debug.Log($"<color=#00FF00>[AI_LOG]</color> Toxicity: {score * 100:F1}% | Text: {rawText}");
    }
}

[Serializable]
public struct ChatMessage
{
    public string Sender;
    public string RawContent;
    public string TranslatedContent;
    public float ToxicityScore;
    public bool IsFiltered;
}

public class GeminiResultData { public string translation; public float toxicity; }
public class GeminiResponse { public GeminiCandidate[] candidates; }
public class GeminiCandidate { public GeminiContent content; }
public class GeminiContent { public GeminiPart[] parts; }
public class GeminiPart { public string text; }