using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行时把界面文字换成系统中文字体。
/// Unity 内置字体不含中文字形，构建时写入的字体名在 Android 上也不存在，
/// 所以统一在启动时从系统字体里挑一个能用的。
/// </summary>
public class ChineseFontApplier : MonoBehaviour
{
    private static readonly string[] Candidates =
    {
        "Noto Sans CJK SC",
        "Noto Sans SC",
        "Source Han Sans SC",
        "Droid Sans Fallback",
        "Microsoft YaHei",
        "SimHei",
        "sans-serif",
    };

    private void Awake()
    {
        Font font = Font.CreateDynamicFontFromOSFont(Candidates, 32);
        if (font == null)
        {
            Debug.LogWarning("[AR] 未找到系统中文字体，界面可能显示为方块");
            return;
        }

        Debug.Log("[AR] 中文字体已应用: " + font.name);

        foreach (Text text in GetComponentsInChildren<Text>(true))
        {
            text.font = font;
        }
    }
}
