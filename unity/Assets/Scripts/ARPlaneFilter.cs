using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// 隐藏面积过小的平面。ARCore 会吐出面积接近零的零碎平面，
/// 显示出来只会在屏幕上闪，对用户没有意义。
/// </summary>
public class ARPlaneFilter : MonoBehaviour
{
    [Tooltip("小于该面积（平方米）的平面不显示")]
    public float minimumArea = 0.08f;

    [Tooltip("平面管理器")]
    public ARPlaneManager planeManager;

    private void Update()
    {
        if (planeManager == null)
        {
            return;
        }

        foreach (ARPlane plane in planeManager.trackables)
        {
            var renderer = plane.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                continue;
            }

            float area = plane.size.x * plane.size.y;
            renderer.enabled = area >= minimumArea;
        }
    }
}
