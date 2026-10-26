using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 启动引导页：先给用户说明和开始按钮，点击后才进入 AR 画面。
/// </summary>
public class ARGuide : MonoBehaviour
{
    [Tooltip("整块引导面板")]
    public GameObject guidePanel;

    [Tooltip("开始按钮")]
    public Button startButton;

    private void Start()
    {
        if (startButton != null)
        {
            startButton.onClick.AddListener(EnterAR);
        }
    }

    /// <summary>隐藏引导页，进入 AR 取景。</summary>
    public void EnterAR()
    {
        if (guidePanel != null)
        {
            guidePanel.SetActive(false);
        }
    }
}
