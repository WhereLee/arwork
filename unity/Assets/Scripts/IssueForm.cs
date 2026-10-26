using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 标记放置后弹出的上报表单：标题（必填）、描述、优先级，提交到 Go 后端。
/// 标签使用英文，因为运行时字体走 Unity 内置字体，不保证包含中文字形。
/// </summary>
public class IssueForm : MonoBehaviour
{
    [Tooltip("整个表单面板")]
    public GameObject panel;

    public InputField titleInput;
    public InputField descriptionInput;
    public InputField serverInput;

    public Button lowButton;
    public Button mediumButton;
    public Button highButton;
    public Button submitButton;

    public Text coordText;
    public Text statusText;

    private static readonly Color SelectedColor = new Color(0.14f, 0.39f, 0.92f, 1f);
    private static readonly Color NormalColor = new Color(0.22f, 0.25f, 0.31f, 1f);
    private static readonly Color SuccessColor = new Color(0.55f, 0.95f, 0.62f);
    private static readonly Color FailureColor = new Color(1f, 0.55f, 0.5f);

    private string _priority = "medium";
    private Vector3 _position;
    private bool _submitting;

    private void Awake()
    {
        if (lowButton == null || mediumButton == null || highButton == null || submitButton == null)
        {
            Debug.LogError("[AR] form buttons missing");
        }
        else
        {
            lowButton.onClick.AddListener(delegate { SelectPriority("low"); });
            mediumButton.onClick.AddListener(delegate { SelectPriority("medium"); });
            highButton.onClick.AddListener(delegate { SelectPriority("high"); });
            submitButton.onClick.AddListener(delegate { StartCoroutine(Submit()); });
        }

        SelectPriority("medium");

        if (panel != null)
        {
            panel.SetActive(false);
        }

        Debug.Log(string.Format("[AR] form awake panel={0} title={1} server={2}",
            panel != null, titleInput != null, serverInput != null));
    }

    /// <summary>显示表单，并记录本次标记的位置。</summary>
    public void Show(Vector3 position)
    {
        _position = position;
        _submitting = false;

        if (statusText != null)
        {
            statusText.text = string.Empty;
        }

        if (coordText != null)
        {
            coordText.text = string.Format("x {0:F2}   y {1:F2}   z {2:F2}", position.x, position.y, position.z);
        }

        if (panel != null)
        {
            panel.SetActive(true);
        }

        Debug.Log("[AR] form shown at " + position);
    }

    public void Hide()
    {
        panel.SetActive(false);
    }

    private void SelectPriority(string priority)
    {
        _priority = priority;

        lowButton.image.color = priority == "low" ? SelectedColor : NormalColor;
        mediumButton.image.color = priority == "medium" ? SelectedColor : NormalColor;
        highButton.image.color = priority == "high" ? SelectedColor : NormalColor;
    }

    private IEnumerator Submit()
    {
        if (_submitting)
        {
            yield break;
        }

        string title = titleInput.text == null ? string.Empty : titleInput.text.Trim();
        if (title.Length == 0)
        {
            ShowStatus("Title is required", false);
            yield break;
        }

        _submitting = true;
        submitButton.interactable = false;
        ShowStatus("Submitting...", true);

        string error = null;
        string createdId = null;

        string baseUrl = string.IsNullOrEmpty(serverInput.text) ? ApiClient.DefaultBaseUrl : serverInput.text.Trim();

        yield return ApiClient.CreateIssue(
            baseUrl,
            title,
            descriptionInput.text == null ? string.Empty : descriptionInput.text,
            _priority,
            _position,
            delegate(string id) { createdId = id; },
            delegate(string message) { error = message; });

        _submitting = false;
        submitButton.interactable = true;

        if (error != null)
        {
            ShowStatus("Submit failed: " + error, false);
            yield break;
        }

        string shortId = createdId != null && createdId.Length > 8 ? createdId.Substring(0, 8) : createdId;
        ShowStatus("Submitted   id " + shortId, true);

        titleInput.text = string.Empty;
        descriptionInput.text = string.Empty;
    }

    private void ShowStatus(string message, bool success)
    {
        statusText.text = message;
        statusText.color = success ? SuccessColor : FailureColor;
    }
}
