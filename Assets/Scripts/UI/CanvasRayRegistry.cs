using System.Collections.Generic;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// 运行时注册「可被手柄射线 / 手戳命中」的 World Space Canvas。
    /// 这样电视屏之外的界面（例如日记页，场景里可能有多个 prefab 实例）
    /// 不需要在射线组件上逐个拖引用，也不会多出一套并行输入。
    /// </summary>
    public static class CanvasRayRegistry
    {
        private static readonly List<Canvas> canvases = new List<Canvas>();

        public static void Register(Canvas canvas)
        {
            if (canvas == null || canvases.Contains(canvas))
                return;

            canvases.Add(canvas);
        }

        public static void Unregister(Canvas canvas)
        {
            if (canvas == null)
                return;

            canvases.Remove(canvas);
        }

        /// <summary>把当前有效的注册项追加到目标列表（去重，顺便清掉已销毁的项）。</summary>
        public static void Collect(List<Canvas> into)
        {
            if (into == null)
                return;

            for (int i = canvases.Count - 1; i >= 0; i--)
            {
                Canvas canvas = canvases[i];

                if (canvas == null)
                {
                    canvases.RemoveAt(i);
                    continue;
                }

                if (!into.Contains(canvas))
                    into.Add(canvas);
            }
        }
    }
}
