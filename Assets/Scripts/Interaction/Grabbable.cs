using System;
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// 可拾取标记：只有挂了本组件的物体才会被 <see cref="GripGrabInteractor"/> 抓住。
    /// 想控制场景里哪些东西能拿，只给它们挂这个组件即可。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Grabbable : MonoBehaviour
    {
        [Tooltip("取消勾选后暂时不可拾取，组件仍保留。")]
        [SerializeField] private bool grabEnabled = true;

        [Tooltip("抓在手里时相对手掌的额外偏移（米）。")]
        [SerializeField] private Vector3 holdOffset;

        [Header("抓取姿态")]
        [Tooltip("抓起后自动摆成固定的相对姿态（例如略向下倾斜的握笔姿势）。" +
                 "不勾就完全按抓起瞬间的姿态跟随。")]
        [SerializeField] private bool lockGripOrientation;

        [Tooltip("锁定姿态时，相对手部锚点的目标旋转（欧拉角，度）。" +
                 "X = 俯仰（正 = 笔尖往下压），Y = 绕手掌上下轴旋转，" +
                 "Z = 侧倾 / 翻滚。")]
        [SerializeField] private Vector3 gripRotationEuler;

        [Tooltip("从抓起瞬间平滑过渡到目标姿态的时长（秒）。")]
        [SerializeField, Min(0f)] private float gripSnapDuration = 0.15f;

        public bool GrabEnabled => grabEnabled;

        public Vector3 HoldOffset => holdOffset;

        public bool LockGripOrientation => lockGripOrientation;

        public Vector3 GripRotationEuler => gripRotationEuler;

        public float GripSnapDuration => gripSnapDuration;

        public Rigidbody Body => GetComponent<Rigidbody>();

        /// <summary>拾取前的重力设置，松手时按原样恢复。</summary>
        public bool UsesGravity { get; private set; } = true;

        /// <summary>当前是否被某只手抓着。</summary>
        public bool IsHeld { get; private set; }

        /// <summary>当前吸附它的磁铁锚点，没有则为 null。</summary>
        public MagnetAnchor SnappedAnchor { get; set; }

        public event Action<Grabbable> Grabbed;
        public event Action<Grabbable> Released;

        public void NotifyGrabbed()
        {
            IsHeld = true;
            Grabbed?.Invoke(this);
        }

        public void NotifyReleased()
        {
            IsHeld = false;
            Released?.Invoke(this);
        }

        private void Awake()
        {
            Rigidbody body = GetComponent<Rigidbody>();

            if (body != null)
                UsesGravity = body.useGravity;
        }
    }
}
