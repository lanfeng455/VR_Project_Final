using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// 程序绘制的 LUNA 月亮图标：白色圆盘 + 低多边形黑色月牙 + 两道蓝色细环。
    /// SetSpeaking(true) 时在最外圈叠加向外扩散的波纹，用作"主持人正在说话"的声波提示。
    /// 单组件单对象，不依赖任何外部贴图。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LunaMoonGraphic : MaskableGraphic
    {
        [Header("颜色")]
        [SerializeField] private Color diskColor = Color.white;
        [SerializeField] private Color crescentColor =
            new Color(0.09f, 0.09f, 0.09f, 1f);
        [SerializeField] private Color ringColor =
            new Color(0.31f, 0.64f, 0.82f, 1f);
        [SerializeField] private Color rippleColor =
            new Color(0.31f, 0.64f, 0.82f, 1f);

        [Header("形状")]
        [Tooltip("圆盘半径占图标短边的比例。")]
        [SerializeField, Range(0.3f, 1f)] private float diskRadiusRatio = 0.80f;

        [Tooltip("月牙分段数。数值越小越接近多边形，贴近草图。")]
        [SerializeField, Range(6, 48)] private int crescentSteps = 14;

        [Header("波纹")]
        [SerializeField, Min(0.2f)] private float ripplePeriod = 2.2f;
        [SerializeField, Range(1, 6)] private int rippleCount = 3;
        [SerializeField, Min(0f)] private float rippleSpread = 0.55f;

        private bool speaking;

        public bool IsSpeaking => speaking;

        /// <summary>驱动波纹：主持人说话时开启，说完关闭。</summary>
        public void SetSpeaking(bool value)
        {
            if (speaking == value)
                return;

            speaking = value;
            SetVerticesDirty();
        }

        private void Update()
        {
            // 只有说话时才需要逐帧重绘。
            if (speaking)
                SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float half = Mathf.Min(rect.width, rect.height) * 0.5f;
            float radius = half * diskRadiusRatio;
            float scale = Mathf.Max(radius / 90f, 0.35f);

            AddDisc(vh, center, radius, diskColor, 64);
            AddCrescent(vh, center, radius, crescentColor);

            // 两道细环，略大于圆盘。
            AddRing(vh, center, radius * 1.10f, 1.8f * scale, ringColor, 72);
            AddRing(vh, center, radius * 1.21f, 1.4f * scale, ringColor, 72);

            if (!speaking || ripplePeriod <= 0f)
                return;

            float time = Time.unscaledTime;

            for (int i = 0; i < rippleCount; i++)
            {
                float phase = Mathf.Repeat(
                    time / ripplePeriod + i / (float)rippleCount,
                    1f
                );

                float ringRadius =
                    radius * (1.24f + phase * rippleSpread);

                float alpha = (1f - phase) * (1f - phase) * 0.55f;
                Color color = rippleColor;
                color.a *= alpha;

                AddRing(
                    vh,
                    center,
                    ringRadius,
                    Mathf.Lerp(2.6f, 0.8f, phase) * scale,
                    color,
                    72
                );
            }
        }

        private static void AddVert(
            VertexHelper vh,
            Vector2 point,
            Color color)
        {
            vh.AddVert(point, color, Vector2.zero);
        }

        private void AddDisc(
            VertexHelper vh,
            Vector2 center,
            float radius,
            Color color,
            int segments)
        {
            int start = vh.currentVertCount;
            AddVert(vh, center, color);

            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;

                AddVert(
                    vh,
                    center + new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle)) * radius,
                    color
                );
            }

            for (int i = 0; i < segments; i++)
                vh.AddTriangle(start, start + 1 + i, start + 2 + i);
        }

        private static void AddQuad(
            VertexHelper vh,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d,
            Color color)
        {
            int start = vh.currentVertCount;

            AddVert(vh, a, color);
            AddVert(vh, b, color);
            AddVert(vh, c, color);
            AddVert(vh, d, color);

            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private void AddRing(
            VertexHelper vh,
            Vector2 center,
            float radius,
            float width,
            Color color,
            int segments)
        {
            float inner = Mathf.Max(0f, radius - width);

            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;

                Vector2 dir0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 dir1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                AddQuad(
                    vh,
                    center + dir0 * inner,
                    center + dir0 * radius,
                    center + dir1 * radius,
                    center + dir1 * inner,
                    color
                );
            }
        }

        /// <summary>
        /// 用两个同心/偏心圆之间的夹角差生成月牙，低分段数带来"多边形"外观。
        /// 顶部留一段白色缺口，和草图里断开的两截月牙一致。
        /// </summary>
        private void AddCrescent(
            VertexHelper vh,
            Vector2 center,
            float radius,
            Color color)
        {
            float outerRadius = radius * 0.58f;

            Vector2 innerCenter =
                center + new Vector2(radius * 0.22f, radius * 0.06f);

            float innerRadius = radius * 0.36f;
            int steps = Mathf.Max(6, crescentSteps);

            for (int i = 0; i < steps; i++)
            {
                float t0 = i / (float)steps;
                float t1 = (i + 1) / (float)steps;

                // 顶部 ±14° 留白，形成草图里断开的两截。
                float midAngle = (t0 + t1) * 0.5f * 360f;

                if (midAngle > 76f && midAngle < 104f)
                    continue;

                float a0 = t0 * Mathf.PI * 2f;
                float a1 = t1 * Mathf.PI * 2f;

                Vector2 dir0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 dir1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                AddQuad(
                    vh,
                    center + dir0 * outerRadius,
                    innerCenter + dir0 * innerRadius,
                    innerCenter + dir1 * innerRadius,
                    center + dir1 * outerRadius,
                    color
                );
            }
        }
    }
}
