using System.Collections;
using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 开发用握物：按住空格，从画面中心 SphereCast 检测道具（带 Rigidbody）。
    /// 持物用"速度伺服"驱动刚体（非 kinematic 硬挂）→ 物体持续参与碰撞，天然不穿墙。
    /// 手持期间物体有冷白高亮描边；松开落地瞬间以落点激发一圈回声，高亮再保持几秒渐隐。
    /// </summary>
    public class DevPlayerGrab : MonoBehaviour
    {
        [Header("检测")]
        public float castRadius = 0.2f;
        public float castDistance = 4f;

        [Header("持物伺服（越大越跟手）")]
        public float posSpring = 14f;
        public float rotSpring = 9f;

        [Header("落地回声")]
        public float landWaveRadius = 5f;
        public float landWaveSpeed = 7f;
        public Color landWaveColor = new Color(0.6f, 0.85f, 1f);
        public float landHighlightTime = 3f;

        [Header("手部挂点（空则自动在相机下创建）")]
        public Transform handAttachPoint;

        DevPlayerInput input;
        Camera cam;
        Rigidbody heldRb;
        float heldRadius = 0.4f;
        bool prevHeld;

        void Awake()
        {
            input = GetComponent<DevPlayerInput>();
            cam = GetComponentInChildren<Camera>();
            if (handAttachPoint == null)
            {
                var go = new GameObject("HandAttachPoint");
                handAttachPoint = go.transform;
                handAttachPoint.SetParent(cam.transform, false);
                handAttachPoint.localPosition = new Vector3(0.45f, -0.35f, 0.9f);
            }
        }

        void Update()
        {
            bool held = input.GrabHeld;

            if (held && !prevHeld) TryGrab();   // 按下瞬间：尝试抓取
            if (!held && prevHeld) Drop();      // 松开瞬间：丢下
            prevHeld = held;

            // 持物期间持续写高亮（球心跟随物体）
            if (heldRb != null)
                EchoWaveManager.SetHighlight(heldRb.worldCenterOfMass, heldRadius, 1f);
        }

        void FixedUpdate()
        {
            if (heldRb == null) return;

            // 速度伺服：驱动刚体速度朝挂点收敛；旋转用角速度对齐
            Vector3 toTarget = handAttachPoint.position - heldRb.position;
            heldRb.linearVelocity = toTarget * posSpring;

            Quaternion dq = handAttachPoint.rotation * Quaternion.Inverse(heldRb.rotation);
            dq.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (angleDeg > 180f) angleDeg -= 360f;
            heldRb.angularVelocity = axis.normalized * (angleDeg * Mathf.Deg2Rad * rotSpring);
        }

        void OnDisable() => Drop();

        void TryGrab()
        {
            if (heldRb != null || cam == null) return;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.SphereCast(ray, castRadius, out RaycastHit hit, castDistance)) return;

            // 只抓带 Rigidbody 的道具（从碰撞体向上找刚体）
            Rigidbody rb = hit.collider.GetComponentInParent<Rigidbody>();
            if (rb == null || rb.isKinematic) return;

            heldRb = rb;
            heldRb.interpolation = RigidbodyInterpolation.Interpolate; // 手持时更顺滑
            heldRadius = hit.collider.bounds.extents.magnitude + 0.05f;
        }

        void Drop()
        {
            if (heldRb == null) return;
            Rigidbody rb = heldRb;
            heldRb = null;
            rb.interpolation = RigidbodyInterpolation.None;
            // 速度不清零：松手时带当前速度自然落地（无额外投掷力）
            if (isActiveAndEnabled) StartCoroutine(TrackLanding(rb, heldRadius));
        }

        /// <summary>等物体落地（速度归零）后以落点激发回声，高亮保持 landHighlightTime 秒渐隐。</summary>
        IEnumerator TrackLanding(Rigidbody rb, float radius)
        {
            // 等待落地：速度接近 0（超时保护，防止掉出地图等卡住）
            float timeout = 5f;
            while (timeout > 0f && rb != null && rb.linearVelocity.magnitude > 0.15f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (rb == null || EchoWaveManager.Instance == null) yield break;

            // 落地瞬间：以物体位置为波心激发一圈回声
            EchoWaveManager.Instance.Spawn(rb.worldCenterOfMass, landWaveSpeed, landWaveRadius, landWaveColor, 0.8f);

            // 高亮保持一段时间渐隐（期间抓了新东西则让位给持物高亮）
            float t = landHighlightTime;
            while (t > 0f)
            {
                t -= Time.deltaTime;
                if (heldRb != null) yield break; // 又抓了新东西
                if (rb == null) break;           // 物体被销毁
                EchoWaveManager.SetHighlight(rb.worldCenterOfMass, radius, t / landHighlightTime);
                yield return null;
            }
            if (heldRb == null) EchoWaveManager.ClearHighlight();
        }
    }
}
