using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 点击已检测到的水平平面，在命中位置放置一个标记。
/// 手指落在 UI 上时不放置（作业任务书 §3.1 第 6 条）。
/// </summary>
public class PlaceMarker : MonoBehaviour
{
    [Tooltip("负责平面射线检测的组件")]
    public ARRaycastManager raycastManager;

    [Tooltip("放置在命中位置的标记预制体")]
    public GameObject markerPrefab;

    [Tooltip("放置后弹出的上报表单")]
    public IssueForm issueForm;

    private static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();
    private readonly List<GameObject> _placed = new List<GameObject>();

    private void Start()
    {
        Debug.Log(string.Format(
            "[AR] placer ready: raycast={0} prefab={1} eventSystem={2}",
            raycastManager != null, markerPrefab != null, EventSystem.current != null));
    }

    /// <summary>放置成功时触发，参数为命中位置。</summary>
    public event System.Action<Vector3> MarkerPlaced;

    /// <summary>当前已放置的标记数量。</summary>
    public int PlacedCount
    {
        get { return _placed.Count; }
    }

    private void Update()
    {
        if (Input.touchCount == 0)
        {
            return;
        }

        Touch touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began)
        {
            return;
        }

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId);
        Debug.Log(string.Format("[AR] touch began ({0:F0},{1:F0}) finger={2} overUI={3}",
            touch.position.x, touch.position.y, touch.fingerId, overUI));

        // 点 UI（按钮、输入框）时不能误放标记
        if (overUI)
        {
            return;
        }

        if (raycastManager == null || markerPrefab == null)
        {
            return;
        }

        // 平面刚被检测到时边界可能还没稳定，三种平面类型都接受
        TrackableType planeTypes = TrackableType.PlaneWithinPolygon
            | TrackableType.PlaneWithinBounds
            | TrackableType.PlaneEstimated;

        if (raycastManager.Raycast(touch.position, Hits, planeTypes))
        {
            Pose hitPose = Hits[0].pose;
            GameObject marker = Instantiate(markerPrefab, hitPose.position, hitPose.rotation);
            _placed.Add(marker);

            LogMarkerDiagnostics(hitPose.position, marker);

            if (issueForm != null)
            {
                issueForm.Show(hitPose.position);
            }
            else
            {
                Debug.LogWarning("[AR] issueForm not assigned");
            }

            if (MarkerPlaced != null)
            {
                MarkerPlaced(hitPose.position);
            }
        }
        else
        {
            Debug.Log("[AR] raycast miss: no plane hit at " + touch.position);
        }
    }

    /// <summary>标记是否需要能看到，靠日志而不是靠猜。</summary>
    private void LogMarkerDiagnostics(Vector3 worldPosition, GameObject marker)
    {
        Camera mainCamera = Camera.main;
        string screenInfo = "no-camera";

        if (mainCamera != null)
        {
            Vector3 screenPoint = mainCamera.WorldToScreenPoint(worldPosition);
            bool onScreen = screenPoint.z > 0f
                && screenPoint.x >= 0f && screenPoint.x <= Screen.width
                && screenPoint.y >= 0f && screenPoint.y <= Screen.height;

            screenInfo = string.Format("screen=({0:F0},{1:F0}) onScreen={2} screenSize={3}x{4}",
                screenPoint.x, screenPoint.y, onScreen, Screen.width, Screen.height);
        }

        var renderer = marker != null ? marker.GetComponentInChildren<Renderer>() : null;
        string materialInfo = renderer == null || renderer.sharedMaterial == null
            ? "null"
            : renderer.sharedMaterial.shader.name;

        Debug.Log(string.Format(
            "[AR] marker #{0} at ({1:F2},{2:F2},{3:F2}) scale={4:F2} shader={5} visible={6} {7}",
            _placed.Count,
            worldPosition.x, worldPosition.y, worldPosition.z,
            marker != null ? marker.transform.lossyScale.x : -1f,
            materialInfo,
            renderer != null && renderer.isVisible,
            screenInfo));
    }

    /// <summary>清除所有已放置的标记。</summary>
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
}
