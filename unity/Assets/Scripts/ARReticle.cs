using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 屏幕中心的放置指示器：射线命中平面时，方框贴在那块平面上并按法线转正；
/// 没命中就藏起来。使用者靠它判断“现在按下去会不会有东西”。
/// 注意与 PlaceMarker 使用同一个屏幕中心射线，指示的位置就是实际落点。
/// </summary>
public class ARReticle : MonoBehaviour
{
    [Tooltip("负责平面射线检测的组件")]
    public ARRaycastManager raycastManager;

    [Tooltip("平面管理器，用来判断命中的是哪一类表面")]
    public ARPlaneManager planeManager;

    [Tooltip("方框本体，显隐由这里控制")]
    public GameObject visual;

    [Tooltip("贴面时沿法线抬起的距离，避免与平面网格重叠闪烁")]
    public float surfaceOffset = 0.002f;

    private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();

    private bool _enabled;
    private bool _vertical;

    /// <summary>只指示当前选中的表面类型，另一类表面上方不显示方框。</summary>
    public void SetSurface(bool vertical)
    {
        _vertical = vertical;
    }

    /// <summary>由流程控制调用，只有检测状态才需要指示器。</summary>
    public void SetIndicatorEnabled(bool enabled)
    {
        _enabled = enabled;

        if (!enabled && visual != null)
        {
            visual.SetActive(false);
        }
    }

    private void Update()
    {
        if (!_enabled || raycastManager == null || visual == null)
        {
            return;
        }

        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        TrackableType planeTypes = TrackableType.PlaneWithinPolygon
            | TrackableType.PlaneWithinBounds
            | TrackableType.PlaneEstimated;

        if (!raycastManager.Raycast(center, Hits, planeTypes))
        {
            if (visual.activeSelf)
            {
                visual.SetActive(false);
            }

            return;
        }

        Pose hitPose = Hits[0].pose;

        // 只对当前选中的表面类型出框，否则会在看不见的平面上冒出一个方框
        if (planeManager != null
            && !ARPlaneFilter.MatchesSurface(planeManager.GetPlane(Hits[0].trackableId), _vertical))
        {
            if (visual.activeSelf)
            {
                visual.SetActive(false);
            }

            return;
        }

        transform.position = hitPose.position + hitPose.up * surfaceOffset;
        transform.rotation = Quaternion.FromToRotation(Vector3.up, hitPose.up);

        if (!visual.activeSelf)
        {
            visual.SetActive(true);
        }
    }
}
