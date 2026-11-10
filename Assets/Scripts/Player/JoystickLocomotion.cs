using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// 用右手柄摇杆推动整个 XR Rig 做水平移动（前后左右）。
    /// 只做平面位移，不处理转向、瞬移和碰撞。
    /// </summary>
    public sealed class JoystickLocomotion : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("通常是 CenterEyeAnchor；留空时自动使用 Camera.main。")]
        [SerializeField] private Transform head;

        [Header("移动")]
        [Tooltip("最大移动速度（米 / 秒）。")]
        [SerializeField] private float moveSpeed = 2f;

        [Tooltip("摇杆死区，避免静止时漂移。")]
        [SerializeField, Range(0f, 0.5f)]
        private float deadZone = 0.15f;

        private void Update()
        {
            Vector2 axis = OVRInput.Get(
                OVRInput.Axis2D.PrimaryThumbstick,
                OVRInput.Controller.RTouch
            );

            if (axis.sqrMagnitude < deadZone * deadZone)
                return;

            Transform view = head;

            if (view == null && Camera.main != null)
                view = Camera.main.transform;

            Vector3 forward = view != null
                ? view.forward
                : transform.forward;

            Vector3 right = view != null
                ? view.right
                : transform.right;

            // 只保留水平分量，避免摇杆把玩家往地里推。
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(right, Vector3.up).normalized;

            Vector3 direction = forward * axis.y + right * axis.x;

            if (direction.sqrMagnitude > 1f)
                direction.Normalize();

            transform.position += direction * (moveSpeed * Time.deltaTime);
        }
    }
}
