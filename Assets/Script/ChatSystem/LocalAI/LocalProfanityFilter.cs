using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

public class LocalProfanityFilter : MonoBehaviour
{
    private HashSet<string> _profanitySet = new HashSet<string>();
    private string _regexPattern;

    void Awake()
    {
        LoadProfanityData();
    }

    private void LoadProfanityData()
    {
        // 1. Resources/Profanity 폴더의 모든 텍스트 파일 로드
        TextAsset[] dataFiles = Resources.LoadAll<TextAsset>("Profanity");
        foreach (var file in dataFiles)
        {
            // 줄바꿈으로 단어 분리 (Trim으로 공백 제거)
            string[] lines = file.text.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                _profanitySet.Add(line.Trim());
            }
        }

        // 2. 변칙 욕설(예: 바 보, 바*보) 탐지를 위한 정규표현식 패턴 생성
        if (_profanitySet.Count > 0)
        {
            _regexPattern = string.Join("|", _profanitySet.Select(Regex.Escape));
        }

        Debug.Log($"<color=cyan>[LocalAI]</color> {_profanitySet.Count}개의 비속어 로드 완료.");
    }

    public bool IsBlacklisted(string text)
    {
        // 공백 제거 후 비교 (단순 포함 여부)
        string cleanText = text.Replace(" ", "");
        if (_profanitySet.Any(word => cleanText.Contains(word))) return true;

        // 정규표현식 패턴 매칭
        if (!string.IsNullOrEmpty(_regexPattern) && Regex.IsMatch(text, _regexPattern)) return true;

        return false;
    }
}