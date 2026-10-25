using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

/// <summary>
/// 把 AR 会话状态直接画在屏幕上，用于判断设备是否支持 ARCore。
/// 只做自检，不包含任何业务逻辑。
/// </summary>
public class ARSelfTest : MonoBehaviour
{
    private GUIStyle _title;
    private GUIStyle _value;
    private GUIStyle _hint;

    private void Awake()
    {
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    private void OnGUI()
    {
        if (_title == null)
        {
            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _title.normal.textColor = new Color(0.6f, 0.9f, 1f);

            _value = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _value.normal.textColor = Color.white;

            _hint = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                wordWrap = true
            };
            _hint.normal.textColor = new Color(1f, 0.85f, 0.4f);
        }

        // AR Foundation 5.1 中这两个是静态属性，直接从类型上取
        string state = ARSession.state.ToString();
        string reason = ARSession.notTrackingReason.ToString();
        string verdict = Describe(state);
        string xrInfo = DescribeXR();

        float w = Screen.width - 40f;
        GUI.Box(new Rect(20f, 20f, w, 300f), GUIContent.none);
        GUI.Label(new Rect(40f, 30f, w - 40f, 40f), "AR SESSION STATE", _title);
        GUI.Label(new Rect(40f, 80f, w - 40f, 60f), state, _value);
        GUI.Label(new Rect(40f, 150f, w - 40f, 40f), "Not tracking reason: " + reason, _hint);
        GUI.Label(new Rect(40f, 195f, w - 40f, 40f), verdict, _hint);
        GUI.Label(new Rect(40f, 235f, w - 40f, 80f), xrInfo, _hint);
    }

    /// <summary>
    /// XR 管理层是否加载了 loader，用于区分“设备不支持”与“插件没进包”。
    /// </summary>
    private static string DescribeXR()
    {
        XRGeneralSettings settings = XRGeneralSettings.Instance;
        if (settings == null)
        {
            return "XR: 没有 XRGeneralSettings（配置没进包）";
        }

        if (settings.Manager == null)
        {
            return "XR: Manager 为空";
        }

        return "XR: loaders=" + settings.Manager.activeLoaders.Count
            + " init=" + settings.Manager.isInitializationComplete;
    }

    /// <summary>
    /// 把状态翻译成一眼能看懂的结论。
    /// </summary>
    private static string Describe(string state)
    {
        switch (state)
        {
            case "SessionTracking":
                return "结论：设备支持 AR，追踪正常";
            case "SessionInitializing":
                return "正在初始化，等 2-3 秒再看";
            case "SessionTrackingInitializing":
                return "追踪初始化中，等 2-3 秒再看";
            case "Unsupported":
                return "结论：设备不支持 AR";
            case "NeedsInstall":
                return "缺少 ARCore 服务，系统应弹出安装提示";
            case "Installing":
                return "正在安装 ARCore 服务";
            case "CheckingAvailability":
                return "正在检查设备能力，若长时间停在这里说明 AR 服务异常";
            case "None":
                return "AR 会话尚未启动";
            default:
                return "未知状态：" + state;
        }
    }
}
