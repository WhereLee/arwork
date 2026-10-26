using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 控制平面网格的可见性：只画当前选中那一类表面（水平或垂直），面积过小的碎平面也不画。
/// 可见性用 Renderer.enabled 控制，不销毁平面对象——AR Foundation 内部的 trackable 表
/// 不会因为对象被销毁而移除条目，下一帧它仍会去更新已销毁的对象，导致 MissingReferenceException。
/// </summary>
public class ARPlaneFilter : MonoBehaviour
{
    [Tooltip("小于该面积（平方米）的平面不显示")]
    public float minimumArea = 0.03f;

    [Tooltip("平面管理器")]
    public ARPlaneManager planeManager;

    private bool _detecting;
    private bool _vertical;

    /// <summary>是否处于检测状态。退出后整片网格都不画。</summary>
    public void SetDetectionActive(bool active)
    {
        _detecting = active;
    }

    /// <summary>只显示这一类表面：true 垂直，false 水平。</summary>
    public void SetSurface(bool vertical)
    {
        _vertical = vertical;
    }

    /// <summary>判断某个平面是否属于当前选中的表面类型。</summary>
    public static bool MatchesSurface(ARPlane plane, bool vertical)
    {
        if (plane == null)
        {
            return false;
        }

        return vertical
            ? plane.alignment == PlaneAlignment.Vertical
            : plane.alignment == PlaneAlignment.HorizontalUp;
    }

    private void Update()
    {
        if (planeManager == null)
        {
            return;
        }

        foreach (ARPlane plane in planeManager.trackables)
        {
            if (plane == null)
            {
                continue;
            }

            var renderer = plane.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                continue;
            }

            bool show = _detecting
                && MatchesSurface(plane, _vertical)
                && plane.size.x * plane.size.y >= minimumArea;

            renderer.enabled = show;
        }
    }
}
