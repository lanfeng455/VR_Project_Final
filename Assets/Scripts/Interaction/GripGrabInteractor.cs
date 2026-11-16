using System.Collections.Generic;
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// 手柄 grip（中指/无名指那个扳机，OVRInput.Button.PrimaryHandTrigger）拾取：
    /// 手靠近挂了 <see cref="Grabbable"/> 的物体时按下并保持即可抓住，
    /// 松手释放；左右手各一份状态，互不干扰。
    ///
    /// 用速度伺服跟随手部，而不是 SetParent：
    /// 被抓住的物体仍是场景里的独立刚体，松手后自然保留动量，
    /// 也不会把缩放和层级写进别的物体。
    /// </summary>
    public sealed class GripGrabInteractor : MonoBehaviour
    {
        private sealed class HandState
        {
            public Transform Anchor;
            public OVRInput.Controller Controller;
            public Grabbable Held;
            public Rigidbody Body;
            public Vector3 PositionOffset;
            public Quaternion RotationOffset;
            public float GripBlendTime;
            public bool WasHeldLastFrame;
        }

        [Header("手部锚点")]
        [Tooltip("左手锚点，通常为 LeftHandAnchor；留空则按名字自动查找。")]
        [SerializeField] private Transform leftHand;

        [Tooltip("右手锚点，通常为 RightHandAnchor；留空则按名字自动查找。")]
        [SerializeField] private Transform rightHand;

        [Header("抓取")]
        [Tooltip("以手掌为中心的搜索半径（米）。")]
        [SerializeField, Min(0.05f)] private float grabRadius = 0.28f;

        [Tooltip("手掌前方多少米再开始搜索，避免抓到身后。")]
        [SerializeField, Min(0f)] private float reachForward = 0.06f;

        [Tooltip("参与拾取的层。默认全部，可改成专用层。")]
        [SerializeField] private LayerMask grabLayers = ~0;

        [Header("跟随手感")]
        [Tooltip("位置跟随强度，越大越跟手。")]
        [SerializeField, Min(1f)] private float positionStrength = 16f;

        [Tooltip("旋转跟随强度，越大越贴身。")]
        [SerializeField, Min(1f)] private float rotationStrength = 10f;

        private readonly List<HandState> hands = new List<HandState>(2);
        private readonly Collider[] overlapResults = new Collider[16];

        private void Awake()
        {
            Transform left = leftHand != null
                ? leftHand
                : FindAnchor("LeftHandAnchor");

            Transform right = rightHand != null
                ? rightHand
                : FindAnchor("RightHandAnchor");

            if (left != null)
            {
                hands.Add(new HandState
                {
                    Anchor = left,
                    Controller = OVRInput.Controller.LTouch
                });
            }

            if (right != null)
            {
                hands.Add(new HandState
                {
                    Anchor = right,
                    Controller = OVRInput.Controller.RTouch
                });
            }

            if (hands.Count == 0)
            {
                Debug.LogWarning(
                    "GripGrabInteractor: 没找到 LeftHandAnchor / RightHandAnchor，" +
                    "请在 Inspector 里手动指定手部锚点。",
                    this
                );
            }
        }

        private static Transform FindAnchor(string name)
        {
            GameObject found = GameObject.Find(name);
            return found != null ? found.transform : null;
        }

        private void Update()
        {
            for (int i = 0; i < hands.Count; i++)
            {
                HandState hand = hands[i];

                if (hand.Anchor == null)
                    continue;

                bool pressed = OVRInput.Get(
                    OVRInput.Button.PrimaryHandTrigger,
                    hand.Controller
                );

                if (pressed && !hand.WasHeldLastFrame)
                {
                    TryGrab(hand);
                }
                else if (!pressed && hand.WasHeldLastFrame)
                {
                    Release(hand);
                }

                hand.WasHeldLastFrame = pressed;
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (dt <= 0f)
                return;

            for (int i = 0; i < hands.Count; i++)
            {
                HandState hand = hands[i];

                if (hand.Body == null || hand.Anchor == null)
                    continue;

                Vector3 target =
                    hand.Anchor.TransformPoint(hand.PositionOffset);

                Quaternion relative = hand.RotationOffset;

                // 锁姿态的物体（笔）：在 gripSnapDuration 内从"抓起瞬间的
                // 相对姿态"平滑过渡到 gripRotationEuler，之后保持该相对姿态。
                // 位置仍然走下面的速度伺服。
                if (hand.Held != null && hand.Held.LockGripOrientation)
                {
                    hand.GripBlendTime += dt;

                    Quaternion locked =
                        Quaternion.Euler(hand.Held.GripRotationEuler);

                    float duration = hand.Held.GripSnapDuration;

                    float t = duration <= 0.0001f
                        ? 1f
                        : Mathf.Clamp01(hand.GripBlendTime / duration);

                    // smoothstep，抓起瞬间不会猛甩。
                    t = t * t * (3f - 2f * t);

                    relative = Quaternion.Slerp(
                        hand.RotationOffset,
                        locked,
                        t
                    );
                }

                Quaternion targetRotation = hand.Anchor.rotation * relative;

                Quaternion delta =
                    targetRotation * Quaternion.Inverse(hand.Body.rotation);

                delta.ToAngleAxis(out float angle, out Vector3 axis);

                if (angle > 180f)
                    angle -= 360f;

                hand.Body.linearVelocity =
                    (target - hand.Body.position) * positionStrength;

                if (axis.sqrMagnitude > 1e-6f)
                {
                    hand.Body.angularVelocity =
                        axis * (angle * Mathf.Deg2Rad * rotationStrength);
                }
            }
        }

        private void TryGrab(HandState hand)
        {
            if (hand.Held != null)
                return;

            Vector3 center =
                hand.Anchor.position + hand.Anchor.forward * reachForward;

            int count = Physics.OverlapSphereNonAlloc(
                center,
                grabRadius,
                overlapResults,
                grabLayers,
                QueryTriggerInteraction.Ignore
            );

            Grabbable best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider col = overlapResults[i];

                if (col == null)
                    continue;

                Grabbable candidate = col.GetComponentInParent<Grabbable>();

                if (candidate == null || !candidate.GrabEnabled)
                    continue;

                if (IsHeldByOtherHand(candidate))
                    continue;

                float distance = (col.ClosestPoint(center) - center)
                    .sqrMagnitude;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            if (best == null)
                return;

            Rigidbody body = best.Body;

            if (body == null)
                return;

            if (body.isKinematic)
            {
                // 被磁铁吸住的日记是 kinematic：允许再次拿下，
                // 但要在抓取之前先解除吸附，否则伺服推不动它。
                if (best.SnappedAnchor == null)
                    return;

                best.SnappedAnchor.Detach();

                if (body.isKinematic)
                    return;
            }

            hand.Held = best;
            hand.Body = body;
            hand.GripBlendTime = 0f;

            hand.PositionOffset = Quaternion.Inverse(
                hand.Anchor.rotation
            ) * (body.position - hand.Anchor.position);

            hand.RotationOffset = Quaternion.Inverse(hand.Anchor.rotation)
                * body.rotation;

            if (best.HoldOffset != Vector3.zero)
            {
                hand.PositionOffset += Quaternion.Inverse(
                    hand.Anchor.rotation
                ) * best.HoldOffset;
            }

            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.useGravity = false;

            best.NotifyGrabbed();
        }

        private void Release(HandState hand)
        {
            Grabbable held = hand.Held;

            if (hand.Body != null)
            {
                // 不清速度：松手时保留这一刻的线速度与角速度，
                // 日记这类物体会带着手部动量继续飞出去然后受重力下落。
                hand.Body.interpolation = RigidbodyInterpolation.None;

                hand.Body.useGravity = held != null
                    ? held.UsesGravity
                    : true;

                hand.Body.WakeUp();
            }

            held?.NotifyReleased();

            hand.Held = null;
            hand.Body = null;
            hand.GripBlendTime = 0f;
        }

        private bool IsHeldByOtherHand(Grabbable target)
        {
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i].Held == target)
                    return true;
            }

            return false;
        }
    }
}
