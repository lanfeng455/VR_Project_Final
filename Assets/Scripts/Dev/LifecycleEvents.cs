using UnityEngine;
using UnityEngine.Events;
using DG.Tweening;

namespace EchoDev
{
    /// <summary>单个时机的动作配置：延迟 → 广播频道(可选) + UnityEvent 响应</summary>
    [System.Serializable]
    public class LifecycleHook
    {
        [Tooltip("延迟秒数，0 = 立即")]
        public float delay = 0f;

        [Tooltip("可选：同时向此频道广播（全局事件，任意监听者响应）")]
        public GameEvent broadcast;

        [Tooltip("本地响应：拖物体选 SetActive / 任意 public 方法")]
        public UnityEvent response;
    }

    /// <summary>
    /// 场景编排 A+B 混合的"时机"侧：
    ///   - 本地响应用 UnityEvent（点对点，够简单）
    ///   - 全局广播用 GameEvent 频道（解耦，跨场景/跨系统）
    ///
    /// 注意：
    ///  - 物体被销毁时 Unity 会先触发 OnDisable 再 OnDestroy，disable 钩子也会执行
    ///  - 延迟用 DOTween 实现（不依赖协程，物体禁用后延迟依然有效）
    /// </summary>
    public class LifecycleEvents : MonoBehaviour
    {
        public LifecycleHook onEnabled  = new();  // OnEnable 时机
        public LifecycleHook onDisabled = new();  // OnDisable 时机
        public LifecycleHook onStarted  = new();  // Start 时机（仅一次）

        void OnEnable()  => Fire(onEnabled, requireActive: true);
        void OnDisable() => Fire(onDisabled, requireActive: false);
        void Start()     => Fire(onStarted, requireActive: false);

        void Fire(LifecycleHook hook, bool requireActive)
        {
            if (hook == null) return;

            void Exec()
            {
                if (!this) return;                                  // 物体已销毁
                if (requireActive && !isActiveAndEnabled) return;   // enable 延迟期间又被禁用
                hook.broadcast?.Raise();
                hook.response?.Invoke();
            }

            if (hook.delay <= 0f) Exec();
            else DOVirtual.DelayedCall(hook.delay, Exec);
        }
    }
}
