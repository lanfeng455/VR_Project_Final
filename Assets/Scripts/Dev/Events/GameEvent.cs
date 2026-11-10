using System.Collections.Generic;
using UnityEngine;

namespace EchoDev
{
    /// <summary> 
    /// 全局事件频道（ScriptableObject 资产）。
    /// 任何代码可调用 Raise() 广播；GameEventListener（或代码订阅）响应。
    /// 频道是资产：跨场景、跨系统解耦，VR 正式版可直接沿用。
    ///
    /// 用法：Project 右键 Create → EchoDev → Game Event 创建频道资产。
    /// 需要带参数的频道（如波的位置）时，复制本类改成 GameEventVector3 即可，模式相同。
    /// </summary>
    [CreateAssetMenu(fileName = "GameEvent", menuName = "EchoDev/Game Event")]
    public class GameEvent : ScriptableObject
    {
        readonly List<GameEventListener> listeners = new();

        public void Raise()
        {
            // 倒序遍历：响应中注销自己也不会跳过元素
            for (int i = listeners.Count - 1; i >= 0; i--)
                listeners[i].OnRaised();
        }

        public void Register(GameEventListener l)   { if (!listeners.Contains(l)) listeners.Add(l); }
        public void Unregister(GameEventListener l) { listeners.Remove(l); }
    }
}
