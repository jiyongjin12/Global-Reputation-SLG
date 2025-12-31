using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System.Text;
using System;

public class AIChatSystem : MonoBehaviour
{
    [Header("API Configuration")]
    public APIConfig apiConfig; // .gitignore로 보호된 ScriptableObject 연결

    [Header("Settings")]
    public string targetLanguage = "English";

    public async UniTask<ChatMessage> ProcessChatMessage(string sender, string message)
    {
        return await ProcessWithGemini(sender, message);
    }

    private async UniTask<ChatMessage> ProcessWithGemini(string sender, string text)
    {
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiConfig.geminiKey}";

        // JSON 응답을 강제하고 번역/독성 검사를 동시에 요청하는 프롬프트
        string prompt = $"Analyze the following text. " +
                        $"1. Translate it into {targetLanguage}. " +
                        $"2. Rate its toxicity from 0.0 to 1.0. " +
                        $"Return ONLY a JSON object exactly in this format: {{\"translation\": \"...\", \"toxicity\": 0.0}}. " +
                        $"Text: \"{text}\"";

        var payload = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
        string jsonPayload = JsonConvert.SerializeObject(payload);

        using var request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        await request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            try
            {
                string rawJson = JsonConvert.DeserializeObject<GeminiResponse>(request.downloadHandler.text).candidates[0].content.parts[0].text.Trim();

                // 마크다운 형식 제거 (AI가 ```json 식으로 응답할 경우 대비)
                if (rawJson.StartsWith("```json")) rawJson = rawJson.Replace("```json", "").Replace("```", "");

                var resultData = JsonConvert.DeserializeObject<GeminiResultData>(rawJson);

                return new ChatMessage
                {
                    Sender = sender,
                    RawContent = text,
                    TranslatedContent = resultData.translation,
                    ToxicityScore = resultData.toxicity,
                    IsFiltered = resultData.toxicity > 0.5f
                };
            }
            catch (Exception e)
            {
                Debug.LogError($"[AIChatSystem] Parsing Error: {e.Message}");
            }
        }
        return new ChatMessage { Sender = sender, RawContent = text, TranslatedContent = "[Error]", ToxicityScore = 0, IsFiltered = false };
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