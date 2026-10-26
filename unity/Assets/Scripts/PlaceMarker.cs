using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 点击屏幕时沿屏幕中心的准星发射线，在命中位置放置标记。
/// 放置目标与高亮目标都基于同一个准星位置，避免“亮的地方和点的地方不一致”。
/// </summary>
public class PlaceMarker : MonoBehaviour
{
    [Tooltip("负责平面射线检测的组件")]
    public ARRaycastManager raycastManager;

    [Tooltip("放置在命中位置的标记预制体")]
    public GameObject markerPrefab;

    [Tooltip("流程控制，放置成功后由它弹出表单")]
    public ARFlowController flow;

    [Tooltip("平面管理器，用来判断命中的是哪一类表面")]
    public ARPlaneManager planeManager;

    /// <summary>未上报：牌面黄底。</summary>
    private static readonly Color PendingColor = new Color(1f, 0.82f, 0.08f);

    /// <summary>已上报：牌面换成绿底，黑边与感叹号不变。</summary>
    private static readonly Color ReportedColor = new Color(0.25f, 0.75f, 0.35f);

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();
    private readonly List<GameObject> _placed = new List<GameObject>();
    private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();

    private bool _interactive;
    private bool _vertical;

    /// <summary>只接受当前选中的表面类型，避免在没画网格的平面上放标记。</summary>
    public void SetSurface(bool vertical)
    {
        _vertical = vertical;
    }

    /// <summary>当前已放置的标记数量。</summary>
    public int PlacedCount
    {
        get { return _placed.Count; }
    }

    /// <summary>由流程控制调用，决定是否响应屏幕点击。</summary>
    public void SetInteractionEnabled(bool enabled)
    {
        _interactive = enabled;
    }

    private void Update()
    {
        if (!_interactive || Input.touchCount == 0)
        {
            return;
        }

        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began)
        {
            return;
        }

        // 点到按钮等 UI 时不放标记
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
        {
            return;
        }

        if (raycastManager == null || markerPrefab == null)
        {
            return;
        }

        // 放置与高亮都认屏幕中心这个准星
        Vector2 aim = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        TrackableType planeTypes = TrackableType.PlaneWithinPolygon
            | TrackableType.PlaneWithinBounds
            | TrackableType.PlaneEstimated;

        if (!raycastManager.Raycast(aim, Hits, planeTypes))
        {
            if (flow != null)
            {
                flow.ShowToast("准星没落在平面上，请对准网格再点");
            }

            return;
        }

        Pose hitPose = Hits[0].pose;

        // 命中的可能是另一类表面（网格没画出来），这种要拒绝，否则会“点空处出方块”
        if (planeManager != null
            && !ARPlaneFilter.MatchesSurface(planeManager.GetPlane(Hits[0].trackableId), _vertical))
        {
            if (flow != null)
            {
                flow.ShowToast(_vertical ? "这里不是墙面，换成「水平」再试" : "这里不是桌面或地面，换成「垂直」再试");
            }

            return;
        }

        // 预制体的底座本身贴着表面，根物体直接落在命中点，不再额外抬高
        Vector3 placePosition = hitPose.position;

        GameObject marker = Instantiate(markerPrefab, placePosition, hitPose.rotation);
        _placed.Add(marker);
        SetMarkerColor(marker, PendingColor);

        Debug.Log(string.Format("[AR] 放置标记 #{0} 位置({1:F2},{2:F2},{3:F2})",
            _placed.Count, placePosition.x, placePosition.y, placePosition.z));

        if (flow != null)
        {
            flow.NotifyMarkerPlaced(placePosition);
        }
    }

    /// <summary>把最近放置的标记标记为“已上报”。</summary>
    public void MarkLastAsReported()
    {
        if (_placed.Count == 0)
        {
            return;
        }

        GameObject last = _placed[_placed.Count - 1];
        if (last != null)
        {
            SetMarkerColor(last, ReportedColor);
        }
    }

    /// <summary>清除场景里所有标记。</summary>
    public void ClearAll()
    {
        foreach (GameObject marker in _placed)
        {
            if (marker != null)
            {
                Destroy(marker);
            }
        }

        _placed.Clear();
    }

    /// <summary>删除最近放置的一个标记（表单取消时使用）。</summary>
    public void RemoveLast()
    {
        if (_placed.Count == 0)
        {
            return;
        }

        GameObject last = _placed[_placed.Count - 1];
        _placed.RemoveAt(_placed.Count - 1);

        if (last != null)
        {
            Destroy(last);
        }
    }

    /// <summary>只换牌面的黄底：黑边、感叹号和白色立杆都不动。</summary>
    private void SetMarkerColor(GameObject marker, Color color)
    {
        Transform fill = marker.transform.Find("Plate/Fill");
        Renderer target = fill != null ? fill.GetComponent<Renderer>() : marker.GetComponentInChildren<Renderer>();

        if (target == null)
        {
            return;
        }

        _propertyBlock.Clear();
        _propertyBlock.SetColor(ColorId, color);
        target.SetPropertyBlock(_propertyBlock);
    }
}
