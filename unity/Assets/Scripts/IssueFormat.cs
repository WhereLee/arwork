using System.Globalization;
using UnityEngine;

/// <summary>
/// 工单状态的展示映射。列表和详情共用一套，避免两处各写一份再走样。
/// </summary>
public static class IssueFormat
{
    private static readonly Color PendingColor = UiPalette.StatePending;
    private static readonly Color DoingColor = UiPalette.StateDoing;
    private static readonly Color ReviewColor = UiPalette.StateReview;
    private static readonly Color DoneColor = UiPalette.StateDone;

    public static string StatusLabel(string status)
    {
        switch (status)
        {
            case "open":
                return "待处理";
            case "in_progress":
                return "处理中";
            case "pending_review":
                return "待确认";
            case "resolved":
                return "已解决";
            default:
                return status;
        }
    }

    public static Color StatusColor(string status)
    {
        switch (status)
        {
            case "open":
                return PendingColor;
            case "in_progress":
                return DoingColor;
            case "pending_review":
                return ReviewColor;
            case "resolved":
                return DoneColor;
            default:
                return Color.gray;
        }
    }

    /// <summary>流转动作的中文名，时间线上用。</summary>
    public static string ActionLabel(string action)
    {
        switch (action)
        {
            case "created":
                return "提交";
            case "ai_dispatched":
                return "AI 派单";
            case "manual_dispatched":
                return "人工派单";
            case "returned":
                return "退回";
            case "reassigned":
                return "改派";
            case "submitted":
                return "提交处理";
            case "approved":
                return "验收通过";
            case "rejected":
                return "驳回";
            case "withdrawn":
                return "撤回";
            case "reopened":
                return "重开";
            case "closed":
                return "关闭";
            case "ai_failed":
                return "AI 未能判定";
            default:
                return action;
        }
    }

    public static string CategoryLabel(string category)
    {
        switch (category)
        {
            case "wall":
                return "墙面";
            case "device":
                return "设备";
            case "safety":
                return "安全";
            default:
                return "未判定";
        }
    }

    public static string PriorityLabel(string priority)
    {
        switch (priority)
        {
            case "high":
                return "高";
            case "medium":
                return "中";
            case "low":
                return "低";
            default:
                return priority;
        }
    }

    /// <summary>后端时间是 RFC3339（UTC），转成本地时区的月-日 时:分。</summary>
    public static string Time(string iso)
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
