using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 问题列表：显示当前账号能看到的问题和状态。
/// 巡检员拿到的是自己报的，管理员拿到全部——这个区分由后端按角色做，前端只管展示。
/// </summary>
public class IssueListPanel : MonoBehaviour
{
    [Tooltip("整个列表面板")]
    public GameObject panel;

    [Tooltip("列表内容容器，挂了 VerticalLayoutGroup")]
    public RectTransform content;

    [Tooltip("条目模板，默认隐藏")]
    public GameObject rowTemplate;

    public Text headerText;
    public Text statusText;
    public Button closeButton;
    public Button refreshButton;
    public InputField serverInput;

    /// <summary>关闭时触发，由流程控制决定回到哪一页。</summary>
    public event System.Action Closed;

    private static readonly Color PendingColor = new Color(0.95f, 0.62f, 0.18f);
    private static readonly Color DoingColor = new Color(0.28f, 0.58f, 0.95f);
    private static readonly Color DoneColor = new Color(0.3f, 0.78f, 0.45f);
    private static readonly Color HintColor = new Color(0.75f, 0.8f, 0.88f);

    private readonly List<GameObject> _rows = new List<GameObject>();
    private Coroutine _loading;

    private void Awake()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Close);
        }

        if (refreshButton != null)
        {
            refreshButton.onClick.AddListener(delegate { Reload(); });
        }

        if (rowTemplate != null)
        {
            rowTemplate.SetActive(false);
        }

        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    public void Show()
    {
        if (panel != null)
        {
            panel.SetActive(true);
        }

        if (headerText != null)
        {
            headerText.text = Session.IsAdmin ? "全部问题" : "我的上报";
        }

        Reload();
    }

    private void Close()
    {
        Hide();

        if (Closed != null)
        {
            Closed();
        }
    }

    /// <summary>只收起面板，不触发 Closed。切换页面时由流程控制调用。</summary>
    public void Hide()
    {
        if (_loading != null)
        {
            StopCoroutine(_loading);
            _loading = null;
        }

        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private void Reload()
    {
        if (_loading != null)
        {
            StopCoroutine(_loading);
        }

        _loading = StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        ClearRows();
        SetStatus("正在读取…", HintColor);

        string baseUrl = serverInput != null && !string.IsNullOrEmpty(serverInput.text)
            ? serverInput.text.Trim()
            : ApiClient.DefaultBaseUrl;

        IssueSummary[] items = null;
        string error = null;

        yield return ApiClient.ListIssues(baseUrl, Session.Token,
            delegate (IssueSummary[] ok) { items = ok; },
            delegate (string message) { error = message; });

        _loading = null;

        if (error != null)
        {
            SetStatus("读取失败：" + error, new Color(1f, 0.55f, 0.5f));
            yield break;
        }

        if (items == null || items.Length == 0)
        {
            SetStatus(Session.IsAdmin ? "还没有任何记录" : "你还没有上报过问题", HintColor);
            yield break;
        }

        for (int i = 0; i < items.Length; i++)
        {
            AddRow(items[i]);
        }

        SetStatus("共 " + items.Length + " 条", HintColor);
    }

    private void AddRow(IssueSummary issue)
    {
        if (rowTemplate == null || content == null)
        {
            return;
        }

        GameObject row = Instantiate(rowTemplate, content);
        row.name = "Row_" + issue.id;
        row.SetActive(true);
        _rows.Add(row);

        SetText(row.transform, "Title", issue.title);
        SetText(row.transform, "Meta", StatusLabel(issue.status) + " · " + FormatTime(issue.createdAt));

        Transform badge = row.transform.Find("Badge");
        if (badge != null)
        {
            var image = badge.GetComponent<Image>();
            if (image != null)
            {
                image.color = StatusColor(issue.status);
            }
        }
    }

    private void ClearRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] != null)
            {
                Destroy(_rows[i]);
            }
        }

        _rows.Clear();
    }

    private static void SetText(Transform row, string childName, string value)
    {
        Transform child = row.Find(childName);
        if (child == null)
        {
            return;
        }

        var text = child.GetComponent<Text>();
        if (text != null)
        {
            text.text = value;
        }
    }

    private void SetStatus(string message, Color color)
    {
        if (statusText == null)
        {
            return;
        }

        statusText.text = message;
        statusText.color = color;
    }

    private static string StatusLabel(string status)
    {
        switch (status)
        {
            case "open":
                return "待处理";
            case "in_progress":
                return "处理中";
            case "resolved":
                return "已解决";
            default:
                return status;
        }
    }

    private static Color StatusColor(string status)
    {
        switch (status)
        {
            case "open":
                return PendingColor;
            case "in_progress":
                return DoingColor;
            case "resolved":
                return DoneColor;
            default:
                return Color.gray;
        }
    }

    /// <summary>后端时间是 RFC3339，转成本地时区的月-日 时:分。</summary>
    private static string FormatTime(string iso)
    {
        if (string.IsNullOrEmpty(iso))
        {
            return string.Empty;
        }

        System.DateTime parsed;
        if (System.DateTime.TryParse(
            iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
        {
            return parsed.ToLocalTime().ToString("MM-dd HH:mm");
        }

        return iso;
    }
}
