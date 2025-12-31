using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class ChatUIItem : MonoBehaviour
{
    public TextMeshProUGUI senderText;
    public TextMeshProUGUI contentText;
    public Button translateBtn;
    public TextMeshProUGUI btnLabel;

    private string _original;
    private string _translated;
    private bool _isShowingTranslation = true;

    public void SetMessage(string sender, string original, string translated)
    {
        senderText.text = sender;
        _original = original;
        _translated = translated;

        UpdateUI();

        translateBtn.onClick.RemoveAllListeners();
        translateBtn.onClick.AddListener(() => {
            _isShowingTranslation = !_isShowingTranslation;
            UpdateUI();
        });
    }

    private void UpdateUI()
    {
        contentText.text = _isShowingTranslation ? _translated : _original;
        btnLabel.text = _isShowingTranslation ? "<원본보기>" : "<번역하기>";
    }
}