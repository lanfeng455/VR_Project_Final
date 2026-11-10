using DG.Tweening;
using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 竖直滑动门：Open() 时用 DOTween 从 closedY 缓动到 openY。
    /// 典型接法：门物体挂本脚本 + GameEventListener（订阅同一频道），
    /// Listener 的 UnityEvent 里选 SlidingDoor → Open()。
    /// </summary>
    public class SlidingDoor : MonoBehaviour
    {
        [Tooltip("关闭时门的世界 Y。Awake 会把门摆到这个高度（起点）")]
        public float closedY = 0f;

        [Tooltip("打开后门停在世界 Y（终点）")]
        public float openY = 3f;

        [Tooltip("缓动时长（秒）")]
        public float duration = 2f;

        [Tooltip("缓动曲线（InOutQuad 起终点都柔和；Linear 匀速）")]
        public Ease ease = Ease.InOutQuad;

        Tween tween;

        void Awake()
        {
            // 起点自定义：进入 Play 时门强制归位到 closedY
            Vector3 p = transform.position;
            p.y = closedY;
            transform.position = p;
        }

        /// <summary>开门：滑到 openY。可重复调用；缓动中再调用会平滑接续。</summary>
        public void Open()
        {
            tween?.Kill();
            tween = transform.DOMoveY(openY, duration).SetEase(ease);
        }

        /// <summary>关门：滑回 closedY（备而不用，UnityEvent 里可选它做复位）。</summary>
        public void Close()
        {
            tween?.Kill();
            tween = transform.DOMoveY(closedY, duration).SetEase(ease);
        }

        void OnDestroy() => tween?.Kill();
    }
}
