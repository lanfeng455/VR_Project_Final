using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 触发器 → 事件频道中继：指定名称的物体进入 Trigger 时向 GameEvent 频道广播。
    /// 典型用法：手持木棍伸进火堆碰撞体 → 广播频道 → 别处的门（挂 GameEventListener）响应。
    /// 触发器可以是本物体或任意子物体上的 Collider（勾 Is Trigger）。
    /// 与 TriggerDoor 的区别：本脚本只负责"读名字 → 广播"，不做任何演出。
    /// </summary>
    public class TriggerEventRelay : MonoBehaviour
    {
        [Tooltip("进入 Trigger 的物体名（匹配碰撞体名或其刚体名）")]
        public string targetObjectName;

        [Tooltip("匹配成功时广播的频道资产（Create → EchoDev → Game Event）")]
        public GameEvent channel;

        [Tooltip("只触发一次（重复进出不再广播）")]
        public bool triggerOnce = true;

        bool fired;

        void OnTriggerEnter(Collider other)
        {
            if (triggerOnce && fired) return;
            if (channel == null || string.IsNullOrEmpty(targetObjectName)) return;

            bool match = other.name == targetObjectName
                      || (other.attachedRigidbody != null && other.attachedRigidbody.name == targetObjectName);
            if (!match) return;

            fired = true;
            Debug.Log($"[TriggerEventRelay] {other.name} 进入 {name}，广播事件：{channel.name}");
            channel.Raise();
        }
    }
}
