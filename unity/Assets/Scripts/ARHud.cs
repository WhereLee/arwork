using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 屏幕提示与操作反馈：告诉用户当前扫描状态、已放置数量，并提供清除按钮。
/// 文案用英文，因为运行时字体走 Unity 内置字体，不保证包含中文字形。
/// </summary>
public class ARHud : MonoBehaviour
{
    [Tooltip("用于读取已识别平面数量")]
    public ARPlaneManager planeManager;

    [Tooltip("用于读取已放置标记数量并执行清除")]
    public PlaceMarker placer;

    [Tooltip("底部提示文字")]
    public Text hintText;

    [Tooltip("右下角清除按钮")]
    public Button clearButton;

    private float _logTimer;

    private void Start()
    {
        if (clearButton != null)
        {
            clearButton.onClick.AddListener(ClearMarkers);
        }
    }

    private void Update()
    {
        if (hintText == null)
        {
            return;
        }

        int planeCount = planeManager != null ? planeManager.trackables.count : 0;
        int markerCount = placer != null ? placer.PlacedCount : 0;

        if (planeCount == 0)
        {
            hintText.text = "Move your phone slowly to scan a flat surface";
        }
        else if (markerCount == 0)
        {
            hintText.text = "Planes: " + planeCount + " · Tap the surface to place a marker";
        }
        else
        {
            hintText.text = "Planes: " + planeCount + " · Markers: " + markerCount;
        }

        _logTimer += Time.deltaTime;
        if (_logTimer >= 2f)
        {
            _logTimer = 0f;
            LogDiagnostics(planeCount, markerCount);
        }
    }

    /// <summary>把平面与标记的真实状态打出来，排查渲染问题时靠日志而不是靠臆测。</summary>
    private void LogDiagnostics(int planeCount, int markerCount)
    {
        Debug.Log(string.Format(
            "[AR] state planes={0} markers={1} session={2} reason={3} camera={4} trackables={5}",
            planeCount, markerCount, ARSession.state, ARSession.notTrackingReason,
            Camera.main != null ? Camera.main.transform.position.ToString("F2") : "none",
            planeManager != null ? planeManager.trackables.count : -1));

        if (planeManager == null)
        {
            return;
        }

        foreach (ARPlane plane in planeManager.trackables)
        {
            var filter = plane.GetComponent<MeshFilter>();
            int vertices = filter != null && filter.sharedMesh != null ? filter.sharedMesh.vertexCount : -1;

            var renderer = plane.GetComponent<MeshRenderer>();
            string shaderName = renderer != null && renderer.sharedMaterial != null
                ? renderer.sharedMaterial.shader.name
                : "null";

            Debug.Log(string.Format(
                "[AR] plane id={0} pos=({1:F2},{2:F2},{3:F2}) size=({4:F2},{5:F2}) align={6} vertices={7} shader={8} visible={9}",
                plane.trackableId,
                plane.transform.position.x, plane.transform.position.y, plane.transform.position.z,
                plane.size.x, plane.size.y,
                plane.alignment,
                vertices,
                shaderName,
                renderer != null && renderer.isVisible));
        }
    }

    /// <summary>清除所有已放置的标记（按钮回调）。</summary>
    public void ClearMarkers()
    {
        if (placer != null)
        {
            placer.ClearAll();
        }
    }
}
