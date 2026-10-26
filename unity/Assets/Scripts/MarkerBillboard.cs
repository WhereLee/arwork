using UnityEngine;

/// <summary>
/// 让标记牌始终正对镜头。牌子绕自身中心旋转，底部仍固定在杆顶。
/// Quad 的正面朝 -Z，所以 +Z 要背对相机。
/// </summary>
public class MarkerBillboard : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera target = Camera.main;
        if (target == null)
        {
            return;
        }

        Vector3 toCamera = transform.position - target.transform.position;
        if (toCamera.sqrMagnitude < 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
    }
}
