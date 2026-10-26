using UnityEngine;

/// <summary>
/// 界面配色集中在这里。原则是界面本身只用中性灰阶，
/// 颜色只用来表达状态和危险操作，不做装饰性着色。
/// 深蓝底加亮蓝按钮那套是默认科技风的模板色，和巡检场景没有关系。
/// </summary>
public static class UiPalette
{
    // 层叠背景：越靠上的元素越亮一点，靠明度分层而不是靠色相
    public static readonly Color BgDeep = Hex(0x141416);
    public static readonly Color BgPanel = Hex(0x1E1E21);
    public static readonly Color BgCard = Hex(0x26262A);
    public static readonly Color BgField = Hex(0x303036);

    // 文字
    public static readonly Color TextPrimary = Hex(0xF2F2F4);
    public static readonly Color TextBody = Hex(0xC8C8CE);
    public static readonly Color TextMuted = Hex(0x9A9AA2);
    public static readonly Color TextOnLight = Hex(0x1A1A1C);

    // 按钮
    public static readonly Color BtnIdle = Hex(0x303036);
    public static readonly Color BtnSelected = Hex(0x53535C);
    public static readonly Color BtnPrimary = Hex(0xF2F2F4); // 白底深字，界面上唯一的高对比元素
    public static readonly Color BtnDanger = Hex(0x6E3232);

    // 状态色：保留语义，但压低饱和度，避免刺眼
    public static readonly Color StatePending = Hex(0xD9A03C);
    public static readonly Color StateDoing = Hex(0x5B8FD6);
    public static readonly Color StateReview = Hex(0xA687CE);
    public static readonly Color StateDone = Hex(0x5FA870);

    // 提示文字
    public static readonly Color Hint = Hex(0x9A9AA2);
    public static readonly Color Error = Hex(0xE0736B);
    public static readonly Color Success = Hex(0x6FBF80);

    /// <summary>AR 场景里必须让出摄像头画面，所以叠加面板用半透明纯黑。</summary>
    public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.72f);
    public static readonly Color OverlayLight = new Color(0f, 0f, 0f, 0.55f);

    private static Color Hex(int rgb)
    {
        return new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >> 8) & 0xFF) / 255f,
            (rgb & 0xFF) / 255f,
            1f);
    }
}
