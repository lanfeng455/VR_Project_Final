using System.Collections;
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// 磁铁吸附锚点：日记页（或其它挂了 Grabbable 的物体）进入触发范围后
    /// 被吸到锚点上，转为 kinematic 不再下落；再次被 grip 抓住时自动脱离。
    ///
    /// 触发范围的 SphereCollider 建议放在子物体上并勾 isTrigger，
    /// 这样磁铁本身仍保留一个普通碰撞体供 grip 拾取。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MagnetAnchor : MonoBehaviour
    {
        [Header("Snap Target")]
        [Tooltip("吸附到的 Transform；留空表示吸附到本物体。")]
        [SerializeField] private Transform snapTarget;

        [Tooltip("相对吸附目标的额外位置偏移（米）。")]
        [SerializeField] private Vector3 positionOffset;

        [Tooltip("吸附完成时的额外欧拉角偏移。")]
        [SerializeField] private Vector3 eulerOffset;

        [Tooltip("吸附动画时长（秒），0 表示瞬间吸附。")]
        [SerializeField, Min(0f)] private float snapDuration = 0.12f;

        [Header("Filter")]
        [Tooltip("只有这些层上的物体才会被吸。")]
        [SerializeField] private LayerMask candidateLayers = ~0;

        [Tooltip("可选：只吸这个 Tag 的物体，留空表示不限。")]
        [SerializeField] private string requiredTag = string.Empty;

        private SphereCollider zone;
        private Grabbable attached;
        private Coroutine snapRoutine;

        public bool HasAttached => attached != null;
        public Grabbable Attached => attached;

        private void Awake()
        {
            ResolveZone();
        }

        private void OnDisable()
        {
            // 磁铁被关掉 / 删除时把日记放开，避免它永远停在 kinematic。
            Detach();
        }

        private void ResolveZone()
        {
            SphereCollider[] zones =
                GetComponentsInChildren<SphereCollider>(true);

            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i].isTrigger)
                {
                    zone = zones[i];
                    break;
                }
            }

            if (zone == null)
            {
                Debug.LogWarning(
                    "[LUNA MAGNET] 没找到 isTrigger 的 SphereCollider。" +
                    "请用菜单添加吸附范围，或手动在子物体上加一个 " +
                    "SphereCollider 并勾选 Is Trigger。",
                    this
                );
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryAttach(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryAttach(other);
        }

        private void TryAttach(Collider other)
        {
            if (other == null || snapRoutine != null || attached != null)
                return;

            if (((1 << other.gameObject.layer) & candidateLayers) == 0)
                return;

            if (!string.IsNullOrEmpty(requiredTag) &&
                !other.CompareTag(requiredTag))
            {
                return;
            }

            Grabbable candidate = other.GetComponentInParent<Grabbable>();

            if (candidate == null ||
                candidate.IsHeld ||
                candidate.Body == null)
            {
                return;
            }

            if (candidate.SnappedAnchor != null &&
                candidate.SnappedAnchor != this)
            {
                return;
            }

            snapRoutine = StartCoroutine(SnapRoutine(candidate));
        }

        private IEnumerator SnapRoutine(Grabbable target)
        {
            attached = target;
            target.SnappedAnchor = this;
            target.Grabbed += OnTargetGrabbed;

            Rigidbody body = target.Body;

            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            Vector3 fromPosition = body.position;
            Quaternion fromRotation = body.rotation;
            Vector3 toPosition = TargetPosition();
            Quaternion toRotation = TargetRotation();

            if (snapDuration > 0.001f)
            {
                float t = 0f;

                while (t < 1f && attached == target)
                {
                    t += Time.deltaTime / snapDuration;

                    float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

                    body.position = Vector3.Lerp(
                        fromPosition,
                        toPosition,
                        eased
                    );

                    body.rotation = Quaternion.Slerp(
                        fromRotation,
                        toRotation,
                        eased
                    );

                    yield return null;
                }
            }

            if (attached == target)
            {
                body.position = toPosition;
                body.rotation = toRotation;
            }

            snapRoutine = null;

            Debug.Log("[LUNA MAGNET] snapped: " + target.name);
        }

        private void OnTargetGrabbed(Grabbable target)
        {
            Detach();
        }

        /// <summary>解除吸附：恢复非 kinematic 与原有重力，日记可以再次被拿走后下落。</summary>
        public void Detach()
        {
            if (snapRoutine != null)
            {
                StopCoroutine(snapRoutine);
                snapRoutine = null;
            }

            Grabbable target = attached;

            if (target == null)
                return;

            attached = null;
            target.Grabbed -= OnTargetGrabbed;
            target.SnappedAnchor = null;

            Rigidbody body = target.Body;

            if (body != null)
            {
                body.isKinematic = false;
                body.useGravity = target.UsesGravity;
                body.WakeUp();
            }

            Debug.Log("[LUNA MAGNET] detached: " + target.name);
        }

        private Transform Target =>
            snapTarget != null ? snapTarget : transform;

        private Vector3 TargetPosition()
        {
            return Target.TransformPoint(positionOffset);
        }

        private Quaternion TargetRotation()
        {
            return Target.rotation * Quaternion.Euler(eulerOffset);
        }
    }
}
