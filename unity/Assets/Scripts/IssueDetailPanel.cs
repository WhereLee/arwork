using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 工单详情：给修复者看清要修什么、经历过什么，并在这里交活、退回或撤回。
/// 巡检员也能打开，只是没有操作按钮。
/// </summary>
public class IssueDetailPanel : MonoBehaviour
{
    [Tooltip("整个详情面板")]
    public GameObject panel;

    public Text titleText;
    public Text statusText;
    public Text metaText;
    public Text descriptionText;

    [Tooltip("处理记录整块，没有记录时隐藏")]
    public GameObject repairSection;
    public Text repairText;

    [Tooltip("流转记录，把每条事件拼成一段多行文字")]
    public Text timelineText;

    public Button completeButton;
    public Button returnButton;
    public Button withdrawButton;
    public Button closeButton;
    public Text statusMessage;

    [Tooltip("输入处理记录 / 退回原因的弹窗")]
    public ReasonDialog reasonDialog;

    /// <summary>关闭详情时触发。</summary>
    public event Action Closed;

    /// <summary>动过工单后触发，让列表刷新。</summary>
    public event Action Changed;

    private static readonly Color ErrorColor = new Color(1f, 0.55f, 0.5f);
    private static readonly Color HintColor = new Color(0.75f, 0.8f, 0.88f);

    private IssueSummary _issue;
    private IssueEventItem[] _events;
    private string _pendingAction;
    private Coroutine _loading;

    private void Awake()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Close);
        }

        if (completeButton != null)
        {
            completeButton.onClick.AddListener(delegate { BeginAction("submit"); });
        }

        if (returnButton != null)
        {
            returnButton.onClick.AddListener(delegate { BeginAction("return"); });
        }

        if (withdrawButton != null)
        {
            // 撤回不需要填原因，直接发
            withdrawButton.onClick.AddListener(delegate { StartCoroutine(RunAction("withdraw", string.Empty)); });
        }

        if (reasonDialog != null)
        {
            reasonDialog.Confirmed += OnReasonConfirmed;
        }

        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (reasonDialog != null)
        {
            reasonDialog.Confirmed -= OnReasonConfirmed;
        }
    }

    public void Show(IssueSummary issue)
    {
        _issue = issue;
        _events = null;
        SetMessage(string.Empty, HintColor);

        if (panel != null)
        {
            panel.SetActive(true);
        }

        Refresh();

        // 列表里只有概要，流转记录要单独拉一次
        if (_loading != null)
        {
            StopCoroutine(_loading);
        }
        _loading = StartCoroutine(LoadDetail());
    }

    public void Close()
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

    private IEnumerator LoadDetail()
    {
        if (_issue == null)
        {
            yield break;
        }

        IssueDetailResponse detail = null;
        string error = null;

        yield return ApiClient.GetIssue(ApiClient.LastBaseUrl, Session.Token, _issue.id,
            delegate (IssueDetailResponse ok) { detail = ok; },
            delegate (string message) { error = message; });

        _loading = null;

        if (error != null)
        {
            if (timelineText != null)
            {
                timelineText.text = "流转记录加载失败：" + error;
            }

            yield break;
        }

        _issue = detail.issue;
        _events = detail.events;
        Refresh();
    }

    private void Refresh()
    {
        if (_issue == null)
        {
            return;
        }

        if (titleText != null)
        {
            titleText.text = _issue.title;
        }

        if (statusText != null)
        {
            statusText.text = IssueFormat.StatusLabel(_issue.status);
            statusText.color = IssueFormat.StatusColor(_issue.status);
        }

        if (metaText != null)
        {
            string meta = IssueFormat.CategoryLabel(_issue.category)
                + " · 优先级 " + IssueFormat.PriorityLabel(_issue.priority)
                + " · 上报人 " + (string.IsNullOrEmpty(_issue.reporter) ? "—" : _issue.reporter);

            if (!string.IsNullOrEmpty(_issue.assignee))
            {
                meta += " · 处理人 " + _issue.assignee;
            }

            metaText.text = meta;
        }

        if (descriptionText != null)
        {
            descriptionText.text = string.IsNullOrEmpty(_issue.description)
                ? "（没有填写描述）"
                : _issue.description;
        }

        bool hasRepair = !string.IsNullOrEmpty(_issue.repairNote);
        if (repairSection != null)
        {
            repairSection.SetActive(hasRepair);
        }

        if (hasRepair && repairText != null)
        {
            repairText.text = _issue.repairNote;
        }

        if (timelineText != null)
        {
            timelineText.text = BuildTimeline(_events);
        }

        RefreshButtons();
    }

    /// <summary>按当前状态和身份决定显示哪几个按钮。</summary>
    private void RefreshButtons()
    {
        bool mine = _issue != null
            && Session.Role == "repairer"
            && _issue.assignee == Session.Username;

        bool inProgress = mine && _issue.status == "in_progress";
        bool pendingReview = mine && _issue.status == "pending_review";

        if (completeButton != null)
        {
            completeButton.gameObject.SetActive(inProgress);
        }

        if (returnButton != null)
        {
            returnButton.gameObject.SetActive(inProgress);
        }

        if (withdrawButton != null)
        {
            withdrawButton.gameObject.SetActive(pendingReview);
        }
    }

    private void BeginAction(string action)
    {
        _pendingAction = action;

        if (reasonDialog == null)
        {
            return;
        }

        if (action == "submit")
        {
            reasonDialog.Show("填写处理记录", "做了什么、换过什么、现在是什么状态");
        }
        else
        {
            reasonDialog.Show("退回原因", "为什么这条不该由你来处理");
        }
    }

    private void OnReasonConfirmed(string note)
    {
        if (_issue == null || string.IsNullOrEmpty(_pendingAction))
        {
            return;
        }

        StartCoroutine(RunAction(_pendingAction, note));
    }

    private IEnumerator RunAction(string action, string note)
    {
        if (_issue == null)
        {
            yield break;
        }

        SetMessage("正在提交…", HintColor);
        SetButtonsInteractable(false);

        IssueDetailResponse result = null;
        string error = null;

        yield return ApiClient.PerformAction(
            ApiClient.LastBaseUrl, Session.Token, _issue.id, action,
            string.Empty, note,
            delegate (IssueDetailResponse ok) { result = ok; },
            delegate (string message) { error = message; });

        SetButtonsInteractable(true);

        if (error != null)
        {
            SetMessage("操作失败：" + error, ErrorColor);
            yield break;
        }

        _issue = result.issue;
        _events = result.events;
        _pendingAction = null;

        Refresh();
        SetMessage(SuccessMessage(action), HintColor);

        if (Changed != null)
        {
            Changed();
        }
    }

    private static string SuccessMessage(string action)
    {
        switch (action)
        {
            case "submit":
                return "已提交，等管理员确认";
            case "return":
                return "已退回，等管理员重新派单";
            case "withdraw":
                return "已撤回，可以重新处理";
            default:
                return "已更新";
        }
    }

    /// <summary>把事件流拼成一段可滚动的文字，手机上比画时间线更清楚。</summary>
    private static string BuildTimeline(IssueEventItem[] events)
    {
        if (events == null || events.Length == 0)
        {
            return "暂无流转记录";
        }

        var builder = new StringBuilder();

        for (int i = 0; i < events.Length; i++)
        {
            IssueEventItem e = events[i];

            if (i > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append("[").Append(IssueFormat.ActionLabel(e.action)).Append("] ")
                .Append(string.IsNullOrEmpty(e.actor) ? "—" : e.actor)
                .Append("   ")
                .Append(IssueFormat.Time(e.createdAt));

            if (!string.IsNullOrEmpty(e.note))
            {
                builder.Append("\n").Append(e.note);
            }
        }

        return builder.ToString();
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (completeButton != null)
        {
            completeButton.interactable = interactable;
        }

        if (returnButton != null)
        {
            returnButton.interactable = interactable;
        }

        if (withdrawButton != null)
        {
            withdrawButton.interactable = interactable;
        }
    }

    private void SetMessage(string message, Color color)
    {
        if (statusMessage == null)
        {
            return;
        }

        statusMessage.text = message;
        statusMessage.color = color;
    }
}
