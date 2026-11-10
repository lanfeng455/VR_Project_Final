using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace EchoDev
{
    [Serializable]
    public class SequenceStep
    {
        [Tooltip("距序列开始的延迟（秒）；相同延迟按列表顺序执行")]
        public float delay = 0f;

        [Tooltip("可选：到点时向此频道广播")]
        public GameEvent broadcast;

        [Tooltip("到点时执行的动作")]
        public UnityEvent action;
    }

    /// <summary>
    /// 动作序列：Play() 后按每步各自的 delay 调度执行（先广播后动作）。
    /// 任何 UnityEvent 都能触发它（LifecycleEvents / GameEventListener / TriggerDoor…），
    /// 也可勾 playOnEnable 在物体激活时自动播放。
    ///
    /// 例：物体激活时点亮火把、5 秒后熄灭 ——
    ///   挂本组件，playOnEnable = true：
    ///   step1 [delay 0] SetActive(火把, true)
    ///   step2 [delay 5] SetActive(火把, false)
    /// </summary>
    public class ActionSequence : MonoBehaviour
    {
        [Tooltip("物体激活时自动 Play")]
        public bool playOnEnable = false;

        [Tooltip("步骤列表，每步独立延迟")]
        public List<SequenceStep> steps = new List<SequenceStep>();

        Sequence seq;

        void OnEnable()
        {
            if (playOnEnable) Play();
        }

        [ContextMenu("Play")]
        public void Play()
        {
            Stop();
            seq = DOTween.Sequence();
            foreach (var step in steps)
            {
                var s = step; // 闭包捕获
                seq.InsertCallback(Mathf.Max(0f, s.delay), () =>
                {
                    if (!this) return; // 宿主已销毁
                    s.broadcast?.Raise();
                    s.action?.Invoke();
                });
            }
        }

        /// <summary>停止尚未执行的后继步骤（已执行的不撤回）。</summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            seq?.Kill();
            seq = null;
        }

        void OnDestroy() => Stop();
    }
}
