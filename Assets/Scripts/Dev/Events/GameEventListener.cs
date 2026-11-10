using UnityEngine;
using UnityEngine.Events;

namespace EchoDev
{
    /// <summary>
    /// 频道监听器：订阅 GameEvent 资产，收到广播时执行 UnityEvent 响应。
    /// 与 LifecycleEvents 用同一套 UnityEvent 配置方式（拖物体 → SetActive / 任意 public 方法）。
    /// </summary>
    public class GameEventListener : MonoBehaviour
    {
        [Tooltip("要订阅的频道资产")]
        public GameEvent channel;
        public UnityEvent response;

        void OnEnable()  { if (channel != null) channel.Register(this); }
        void OnDisable() { if (channel != null) channel.Unregister(this); }

        /// <summary>由 GameEvent.Raise() 调用</summary>
        public void OnRaised() => response?.Invoke();
    }
}
