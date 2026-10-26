using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// AR 交互流程的唯一状态源。
/// 引导页选择表面类型 → 检测（可随时切换表面）→ 放置 → 上报 → 退出（需确认）。
/// 面板显隐、点击开关、平面清理与高亮都由这里统一控制。
/// </summary>
public class ARFlowController : MonoBehaviour
{
    private enum Mode
    {
        Guide,
        Detecting,
        Form,
        Confirm,
    }

    [Header("组件")]
    public ARPlaneManager planeManager;
    public ARPlaneFilter planeFilter;
    public ARRaycastManager raycastManager;
    public PlaceMarker placer;
    public ARReticle reticle;
    public IssueForm form;

    [Header("界面")]
    public GameObject guidePanel;
    public GameObject hudPanel;
    public GameObject confirmPanel;
    public Text statusText;
    public Text toastText;
    public Button exitButton;
    public Button horizontalButton;
    public Button verticalButton;
    public Button hudHorizontalButton;
    public Button hudVerticalButton;
    public Button confirmYesButton;
    public Button confirmNoButton;

    /// <summary>屏幕中心指着的那块平面，提亮显示。</summary>
    private static readonly Color FocusColor = new Color(1f, 1f, 1f, 0.45f);

    /// <summary>其余平面保持很淡。</summary>
    private static readonly Color IdleColor = new Color(1f, 1f, 1f, 0.08f);

    private static readonly Color SwitchActiveColor = new Color(0.14f, 0.39f, 0.92f, 1f);
    private static readonly Color SwitchIdleColor = new Color(0.25f, 0.28f, 0.34f, 0.95f);

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
    private readonly List<ARRaycastHit> _screenHits = new List<ARRaycastHit>();

    private Mode _mode = Mode.Guide;
    private bool _vertical;
    private float _logTimer;
    private float _toastTimer;

    private void Awake()
    {
        // 按钮回调必须在运行时绑定，编辑器里绑的不会被序列化
        if (exitButton != null)
        {
            exitButton.onClick.AddListener(RequestExit);
        }

        if (horizontalButton != null)
        {
            horizontalButton.onClick.AddListener(SelectHorizontal);
        }

        if (verticalButton != null)
        {
            verticalButton.onClick.AddListener(SelectVertical);
        }

        if (hudHorizontalButton != null)
        {
            hudHorizontalButton.onClick.AddListener(SwitchToHorizontal);
        }

        if (hudVerticalButton != null)
        {
            hudVerticalButton.onClick.AddListener(SwitchToVertical);
        }

        if (confirmYesButton != null)
        {
            confirmYesButton.onClick.AddListener(DoExit);
        }

        if (confirmNoButton != null)
        {
            confirmNoButton.onClick.AddListener(CancelExit);
        }

        if (form != null)
        {
            form.Submitted += OnSubmitted;
            form.Cancelled += OnCancelled;
        }
    }

    private void OnDestroy()
    {
        if (form != null)
        {
            form.Submitted -= OnSubmitted;
            form.Cancelled -= OnCancelled;
        }
    }

    private void Start()
    {
        SetMode(Mode.Guide);
    }

    // ------------------------------------------------------------ 入口

    /// <summary>引导页：选择水平面（桌面/地面）。</summary>
    public void SelectHorizontal()
    {
        EnterDetection(false);
    }

    /// <summary>引导页：选择垂直面（墙面）。</summary>
    public void SelectVertical()
    {
        EnterDetection(true);
    }

    /// <summary>检测中：直接切到水平面，不用退出重进。</summary>
    public void SwitchToHorizontal()
    {
        if (_mode == Mode.Detecting)
        {
            EnterDetection(false);
        }
    }

    /// <summary>检测中：直接切到垂直面。</summary>
    public void SwitchToVertical()
    {
        if (_mode == Mode.Detecting)
        {
            EnterDetection(true);
        }
    }

    // ------------------------------------------------------------ 状态流转

    private void EnterDetection(bool vertical)
    {
        _vertical = vertical;

        if (planeManager != null)
        {
            planeManager.enabled = true;
        }

        // 两类平面一直检测，切换只改“哪一类可见”，不重建也不销毁任何对象
        if (planeFilter != null)
        {
            planeFilter.SetSurface(vertical);
            planeFilter.SetDetectionActive(true);
        }

        if (placer != null)
        {
            placer.SetSurface(vertical);
        }

        if (reticle != null)
        {
            reticle.SetSurface(vertical);
        }

        SetMode(Mode.Detecting);
        RefreshSwitchButtons();

        Debug.Log(string.Format("[AR] 进入检测：{0} 检测={1}",
            vertical ? "垂直面" : "水平面",
            planeManager != null ? planeManager.requestedDetectionMode.ToString() : "null"));
    }

    /// <summary>点“退出”先弹确认，避免一键清空现场。</summary>
    private void RequestExit()
    {
        if (_mode != Mode.Detecting)
        {
            return;
        }

        SetMode(Mode.Confirm);
    }

    /// <summary>确认退出：停止检测、隐藏平面、清理标记、回到引导页。</summary>
    private void DoExit()
    {
        if (planeManager != null)
        {
            planeManager.enabled = false;
        }

        // 平面对象交给 AR Foundation 自己管，这里只负责不让它们再画出来
        if (planeFilter != null)
        {
            planeFilter.SetDetectionActive(false);
        }

        if (placer != null)
        {
            placer.ClearAll();
        }

        SetMode(Mode.Guide);

        Debug.Log("[AR] 退出检测");
    }

    private void CancelExit()
    {
        SetMode(Mode.Detecting);
    }

    /// <summary>标记放置后由 PlaceMarker 通知，弹出上报表单。</summary>
    public void NotifyMarkerPlaced(Vector3 position)
    {
        if (form != null)
        {
            form.Show(position);
        }

        SetMode(Mode.Form);
    }

    private void OnSubmitted()
    {
        if (placer != null)
        {
            placer.MarkLastAsReported();
        }

        SetMode(Mode.Detecting);
        ShowToast("提交成功，标记已变绿");
    }

    private void OnCancelled()
    {
        if (placer != null)
        {
            placer.RemoveLast();
        }

        SetMode(Mode.Detecting);
        ShowToast("已取消，该标记已删除");
    }

    /// <summary>模式切换的唯一入口。</summary>
    private void SetMode(Mode next)
    {
        _mode = next;

        if (guidePanel != null)
        {
            guidePanel.SetActive(next == Mode.Guide);
        }

        if (hudPanel != null)
        {
            hudPanel.SetActive(next != Mode.Guide);
        }

        if (confirmPanel != null)
        {
            confirmPanel.SetActive(next == Mode.Confirm);
        }

        if (placer != null)
        {
            placer.SetInteractionEnabled(next == Mode.Detecting);
        }

        if (reticle != null)
        {
            reticle.SetIndicatorEnabled(next == Mode.Detecting);
        }

        if (next != Mode.Form && form != null)
        {
            form.Hide();
        }

        Debug.Log("[AR] 模式 → " + next);
    }

    private void RefreshSwitchButtons()
    {
        if (hudHorizontalButton != null)
        {
            hudHorizontalButton.image.color = _vertical ? SwitchIdleColor : SwitchActiveColor;
        }

        if (hudVerticalButton != null)
        {
            hudVerticalButton.image.color = _vertical ? SwitchActiveColor : SwitchIdleColor;
        }
    }

    // ------------------------------------------------------------ 平面

    /// <summary>只统计当前可见的平面，避免“说有 3 个却只看见半个桌面”。</summary>
    private int CountVisiblePlanes()
    {
        if (planeManager == null)
        {
            return 0;
        }

        float minArea = planeFilter != null ? planeFilter.minimumArea : 0f;
        int count = 0;

        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane == null || !ARPlaneFilter.MatchesSurface(plane, _vertical))
            {
                continue;
            }

            if (plane.size.x * plane.size.y >= minArea)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>只提亮屏幕中心所指的那块平面，其余保持很淡。</summary>
    private void RefreshPlaneHighlight()
    {
        if (raycastManager == null || planeManager == null)
        {
            return;
        }

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        bool hasFocus = raycastManager.Raycast(
            screenCenter,
            _screenHits,
            TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds);

        TrackableId focusId = hasFocus ? _screenHits[0].trackableId : default;

        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane == null || !ARPlaneFilter.MatchesSurface(plane, _vertical))
            {
                continue;
            }

            var renderer = plane.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                continue;
            }

            bool focused = hasFocus && plane.trackableId.Equals(focusId);
            _propertyBlock.Clear();
            _propertyBlock.SetColor(ColorId, focused ? FocusColor : IdleColor);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    // ------------------------------------------------------------ 提示

    /// <summary>在屏幕中央短暂显示一句提示。</summary>
    public void ShowToast(string message)
    {
        if (toastText == null)
        {
            return;
        }

        toastText.text = message;
        toastText.gameObject.SetActive(true);
        _toastTimer = 2.5f;
    }

    private void TickToast()
    {
        if (_toastTimer <= 0f)
        {
            return;
        }

        _toastTimer -= Time.deltaTime;
        if (_toastTimer <= 0f && toastText != null)
        {
            toastText.gameObject.SetActive(false);
        }
    }

    /// <summary>把未检测到平面的原因翻译成可执行的中文提示。</summary>
    private string DescribeScanIssue()
    {
        if (ARSession.state == ARSessionState.Unsupported)
        {
            return "这台设备不支持 AR，请换一台支持 ARCore 的手机";
        }

        if (ARSession.state != ARSessionState.SessionTracking)
        {
            switch (ARSession.notTrackingReason)
            {
                case NotTrackingReason.InsufficientLight:
                    return "光线太暗，请移到亮处";
                case NotTrackingReason.InsufficientFeatures:
                    return "表面纹理太少，换个角度再扫";
                case NotTrackingReason.ExcessiveMotion:
                    return "移动太快，请放慢";
                case NotTrackingReason.Initializing:
                    return "AR 正在初始化，请稍候";
                case NotTrackingReason.Relocalizing:
                    return "正在找回位置，请稍候";
            }
        }

        return "慢慢移动手机，方框出现后点屏幕";
    }

    private void Update()
    {
        TickToast();

        if (_mode != Mode.Detecting || planeManager == null)
        {
            return;
        }

        RefreshPlaneHighlight();

        if (statusText == null)
        {
            return;
        }

        int planeCount = CountVisiblePlanes();
        int markerCount = placer != null ? placer.PlacedCount : 0;
        string surface = _vertical ? "垂直面" : "水平面";

        if (planeCount == 0)
        {
            statusText.text = "正在扫描" + surface + "，" + DescribeScanIssue();
        }
        else if (markerCount == 0)
        {
            statusText.text = "已识别 " + planeCount + " 个" + surface + "，把方框对准目标后点屏幕";
        }
        else
        {
            statusText.text = "已识别 " + planeCount + " 个" + surface + " · 已放置 " + markerCount + " 个标记";
        }

        _logTimer += Time.deltaTime;
        if (_logTimer >= 2f)
        {
            _logTimer = 0f;
            Debug.Log(string.Format("[AR] 状态 平面={0} 标记={1} 会话={2} 原因={3} 模式={4}",
                planeCount, markerCount, ARSession.state, ARSession.notTrackingReason,
                planeManager.requestedDetectionMode));
        }
    }
}
