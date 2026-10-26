using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 标记放置后弹出的上报表单：标题（必填）、描述、优先级。
/// 服务器地址默认收起，只在需要排障时展开。
/// </summary>
public class IssueForm : MonoBehaviour
{
    [Tooltip("整个表单面板")]
    public GameObject panel;

    public InputField titleInput;
    public InputField descriptionInput;
    public InputField serverInput;

    [Tooltip("展开/收起服务器地址")]
    public Button settingsButton;

    [Tooltip("服务器地址所在行，默认收起")]
    public GameObject serverRow;

    public Button lowButton;
    public Button mediumButton;
    public Button highButton;
    public Button submitButton;
    public Button cancelButton;

    public Text coordText;
    public Text statusText;

    /// <summary>提交成功后触发。</summary>
    public event System.Action Submitted;

    /// <summary>取消后触发。</summary>
    public event System.Action Cancelled;

    private static readonly Color SelectedColor = new Color(0.14f, 0.39f, 0.92f, 1f);
    private static readonly Color NormalColor = new Color(0.22f, 0.25f, 0.31f, 1f);
    private static readonly Color SuccessColor = new Color(0.55f, 0.95f, 0.62f);
    private static readonly Color FailureColor = new Color(1f, 0.55f, 0.5f);

    private string _priority = "medium";
    private Vector3 _position;
    private bool _submitting;

    private void Awake()
    {
        if (lowButton != null)
        {
            lowButton.onClick.AddListener(delegate { SelectPriority("low"); });
        }

        if (mediumButton != null)
        {
            mediumButton.onClick.AddListener(delegate { SelectPriority("medium"); });
        }

        if (highButton != null)
        {
            highButton.onClick.AddListener(delegate { SelectPriority("high"); });
        }

        if (submitButton != null)
        {
            submitButton.onClick.AddListener(delegate { StartCoroutine(Submit()); });
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(Cancel);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(ToggleServerRow);
        }

        SelectPriority("medium");

        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    /// <summary>显示表单，清空上一轮残留的输入。</summary>
    public void Show(Vector3 position)
    {
        _position = position;
        _submitting = false;

        if (titleInput != null)
        {
            titleInput.text = string.Empty;
        }

        if (descriptionInput != null)
        {
            descriptionInput.text = string.Empty;
        }

        if (statusText != null)
        {
            statusText.text = string.Empty;
        }

        if (coordText != null)
        {
            coordText.text = "已记录标记位置";
        }

        // 服务器地址默认收起，不打扰使用者
        if (serverRow != null)
        {
            serverRow.SetActive(false);
        }

        if (panel != null)
        {
            panel.SetActive(true);
        }

        Debug.Log("[AR] 表单已打开，坐标 " + position);
    }

    public void Hide()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private void ToggleServerRow()
    {
        if (serverRow != null)
        {
            serverRow.SetActive(!serverRow.activeSelf);
        }
    }

    /// <summary>取消上报：关闭表单并通知外部删除这个标记。</summary>
    private void Cancel()
    {
        if (_submitting)
        {
            return;
        }

        Hide();

        if (Cancelled != null)
        {
            Cancelled();
        }
    }

    private void SelectPriority(string priority)
    {
        _priority = priority;

        if (lowButton != null)
        {
            lowButton.image.color = priority == "low" ? SelectedColor : NormalColor;
        }

        if (mediumButton != null)
        {
            mediumButton.image.color = priority == "medium" ? SelectedColor : NormalColor;
        }

        if (highButton != null)
        {
            highButton.image.color = priority == "high" ? SelectedColor : NormalColor;
        }
    }

    private IEnumerator Submit()
    {
        if (_submitting)
        {
            yield break;
        }

        string title = titleInput != null && titleInput.text != null ? titleInput.text.Trim() : string.Empty;
        if (title.Length == 0)
        {
            ShowStatus("标题不能为空", false);
            yield break;
        }

        _submitting = true;
        SetButtonsInteractable(false);
        ShowStatus("提交中，请稍候…", true);

        string error = null;
        string createdId = null;

        string baseUrl = serverInput != null && !string.IsNullOrEmpty(serverInput.text)
            ? serverInput.text.Trim()
            : ApiClient.DefaultBaseUrl;

        yield return ApiClient.CreateIssue(
            baseUrl,
            Session.Token,
            title,
            descriptionInput != null && descriptionInput.text != null ? descriptionInput.text : string.Empty,
            _priority,
            _position,
            delegate(string id) { createdId = id; },
            delegate(string message) { error = message; });

        _submitting = false;
        SetButtonsInteractable(true);

        if (error != null)
        {
            ShowStatus("提交失败：" + error, false);
            yield break;
        }

        string shortId = createdId != null && createdId.Length > 8 ? createdId.Substring(0, 8) : createdId;
        ShowStatus("提交成功，编号 " + shortId, true);

        // 让编号多停留一会儿，方便使用者记下或截图
        yield return new WaitForSeconds(2f);

        Hide();

        if (Submitted != null)
        {
            Submitted();
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (submitButton != null)
        {
            submitButton.interactable = interactable;
        }

        if (cancelButton != null)
        {
            cancelButton.interactable = interactable;
        }
    }

    private void ShowStatus(string message, bool success)
    {
        if (statusText == null)
        {
            return;
        }

        statusText.text = message;
        statusText.color = success ? SuccessColor : FailureColor;
    }
}
