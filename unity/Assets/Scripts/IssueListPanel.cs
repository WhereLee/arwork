using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 问题列表。巡检员看到自己报的，修复者看到派给自己的，管理员看到全部。
/// 这个区分由后端按角色做，前端只管展示。
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

    /// <summary>关闭时触发，由流程控制决定回到哪一页。</summary>
    public event Action Closed;

    /// <summary>点了某一条，由流程控制打开详情。</summary>
    public event Action<IssueSummary> RowClicked;

    private static readonly Color HintColor = UiPalette.Hint;
    private static readonly Color ErrorColor = UiPalette.Error;

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
            refreshButton.onClick.AddListener(Reload);
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
            headerText.text = TitleFor(Session.Role);
        }

        Reload();
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

    /// <summary>重新拉一次列表。详情里动过工单后也会调它。</summary>
    public void Reload()
    {
        if (_loading != null)
        {
            StopCoroutine(_loading);
        }

        _loading = StartCoroutine(Load());
    }

    private void Close()
    {
        Hide();

        if (Closed != null)
        {
            Closed();
        }
    }

    private IEnumerator Load()
    {
        ClearRows();
        SetStatus("正在读取…", HintColor);

        string baseUrl = ApiClient.LastBaseUrl;

        IssueSummary[] items = null;
        string error = null;

        yield return ApiClient.ListIssues(baseUrl, Session.Token,
            delegate (IssueSummary[] ok) { items = ok; },
            delegate (string message) { error = message; });

        _loading = null;

        if (error != null)
        {
            SetStatus("读取失败：" + error, ErrorColor);
            yield break;
        }

        if (items == null || items.Length == 0)
        {
            SetStatus(EmptyFor(Session.Role), HintColor);
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
        SetText(row.transform, "Meta", BuildMeta(issue));

        Transform badge = row.transform.Find("Badge");
        if (badge != null)
        {
            var image = badge.GetComponent<Image>();
            if (image != null)
            {
                image.color = IssueFormat.StatusColor(issue.status);
            }
        }

        // 每一行捕获自己的数据，点哪条开哪条。
        // Button 不依赖模板上是否序列化成功，这里拿不到就现加一个。
        var button = row.GetComponent<Button>();
        if (button == null)
        {
            button = row.AddComponent<Button>();
            button.targetGraphic = row.GetComponent<Image>();
        }

        IssueSummary captured = issue;
        button.onClick.AddListener(delegate
        {
            Debug.Log("[AR] 点击工单：" + captured.title);

            if (RowClicked != null)
            {
                RowClicked(captured);
            }
            else
            {
                Debug.LogWarning("[AR] RowClicked 没有订阅者");
            }
        });
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

    private static string BuildMeta(IssueSummary issue)
    {
        string meta = IssueFormat.StatusLabel(issue.status) + " · " + IssueFormat.Time(issue.createdAt);

        if (!string.IsNullOrEmpty(issue.category))
        {
            meta += " · " + IssueFormat.CategoryLabel(issue.category);
        }

        return meta;
    }

    private static string TitleFor(string role)
    {
        switch (role)
        {
            case "repairer":
                return "派给我的";
            case "admin":
                return "全部工单";
            default:
                return "我的上报";
        }
    }

    private static string EmptyFor(string role)
    {
        switch (role)
        {
            case "repairer":
                return "现在没有派给你的工单";
            case "admin":
                return "还没有任何工单";
            default:
                return "你还没有上报过问题";
        }
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
}
