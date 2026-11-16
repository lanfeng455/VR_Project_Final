using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// 白板笔 / 擦子：只测笔尖（或擦子中心）这一个点与书写面的关系，
    /// 笔身碰到板面不会出墨。
    ///
    /// 接触判定是纯数学的点到书写面距离（不读 Collider、不做射线），
    /// 所以笔 / 板面用 Box 还是 Sphere、板面有没有碰撞体都不影响书写。
    ///
    /// 上一帧 UV 与本帧 UV 之间画一条带圆头的线段，
    /// 因此手柄快速挥动也不会出现断点。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Grabbable))]
    public sealed class WhiteboardTool : MonoBehaviour
    {
        public enum ToolMode
        {
            Pen = 0,
            Eraser = 1
        }

        [Header("Tool")]
        [SerializeField] private ToolMode mode = ToolMode.Pen;

        [Tooltip("笔尖 / 擦子中心；留空则用本物体的原点。")]
        [SerializeField] private Transform tip;

        [Tooltip("接触点微调（米）：从 Tip 位置沿 Tip 自己的本地轴偏移，" +
                 "用来把接触点对齐到真实笔尖。")]
        [SerializeField] private Vector3 tipOffset;

        [Tooltip("开始落笔的容差（米）：笔尖到书写面平面的距离小于它才算接触。")]
        [SerializeField, Min(0.001f)] private float contactRadius = 0.025f;

        [Tooltip("已落笔后的容差倍数（迟滞）：容差 = contactRadius × 它，" +
                 "比开始落笔更宽松，避免手抖断线。")]
        [SerializeField, Min(1f)] private float stayRadiusMultiplier = 1.6f;

        [Tooltip("留空时自动使用场景里的第一块书写面。")]
        [SerializeField] private WhiteboardSurface surface;

        [Tooltip("必须被 grip 抓住才生效，避免掉在板上的笔自己画线。")]
        [SerializeField] private bool requireHeld = true;

        [Header("Ink")]
        [SerializeField] private Color inkColor =
            new Color(0.07f, 0.08f, 0.11f, 1f);

        [Tooltip("笔刷半径（米）。")]
        [SerializeField, Min(0.0005f)] private float penRadius = 0.0035f;

        [Header("Eraser")]
        [Tooltip("擦子半径（米）。")]
        [SerializeField, Min(0.001f)] private float eraserRadius = 0.03f;

        [Header("Stroke")]
        [Tooltip("小于这个距离（米）的移动不重复落笔。")]
        [SerializeField, Min(0.0001f)] private float minStepMeters = 0.0015f;

        [Header("Debug")]
        [Tooltip("在 Console 打印笔尖到书写面的距离和命中结果（限频）。" +
                 "这是目前唯一能区分'没碰到平面'和'碰到了不画'的数据。")]
        [SerializeField] private bool debugLog = true;

        [Tooltip("调试日志最小间隔（秒），避免刷屏。")]
        [SerializeField, Min(0.05f)] private float debugLogInterval = 0.5f;

        private Grabbable grabbable;
        private bool hasLastUv;
        private Vector2 lastUv;
        private bool drawing;
        private float nextDebugTime;

        public Grabbable Grabbable => grabbable;
        public ToolMode Mode => mode;
        public Transform Tip => tip;

        /// <summary>实际参与接触判定的世界坐标（Tip 位置 + tipOffset）。</summary>
        public Vector3 ContactPoint
        {
            get
            {
                if (tip != null)
                    return tip.position + tip.rotation * tipOffset;

                return transform.position + transform.rotation * tipOffset;
            }
        }

        private void Awake()
        {
            grabbable = GetComponent<Grabbable>();
            ResolveSurface();

            if (tip == null)
            {
                Debug.LogWarning(
                    "[LUNA BOARD] '" + name + "' 没有指定 Tip，接触判定会退化到" +
                    "物体原点，很难写出字。请指到真实笔尖，或用 " +
                    "Tools/VR Project/LUNA/Whiteboard/Make Selected A Pen " +
                    "重新生成 Tip。",
                    this
                );
            }
        }

        private void OnEnable()
        {
            hasLastUv = false;
            drawing = false;
        }

        private void Update()
        {
            if (surface == null)
                ResolveSurface();

            if (surface == null)
                return;

            if (requireHeld && (grabbable == null || !grabbable.IsHeld))
            {
                drawing = false;
                hasLastUv = false;
                return;
            }

            Vector3 point = ContactPoint;

            if (!surface.TrySample(point, out WhiteboardSurface.SurfaceSample sample))
            {
                drawing = false;
                hasLastUv = false;
                return;
            }

            // 迟滞：落笔要更靠近，已经在写的时候容差放宽，手抖不断线。
            float tolerance = contactRadius *
                              (drawing ? stayRadiusMultiplier : 1f);

            bool touching = WhiteboardSurface.IsTouching(sample, tolerance);

            LogDebug(sample, tolerance, touching);

            if (!touching)
            {
                // 抬笔：下一笔重新起头，不会把两段连起来。
                drawing = false;
                hasLastUv = false;
                return;
            }

            drawing = true;

            Vector2 uv = sample.uv;
            float radius = mode == ToolMode.Pen ? penRadius : eraserRadius;
            Color color = mode == ToolMode.Pen
                ? inkColor
                : surface.BackgroundColor;

            if (!hasLastUv)
            {
                surface.PaintSegment(uv, uv, radius, color);
            }
            else
            {
                float distance = WorldDistance(lastUv, uv);

                if (distance >= minStepMeters)
                    surface.PaintSegment(lastUv, uv, radius, color);
            }

            lastUv = uv;
            hasLastUv = true;
        }

        private float WorldDistance(Vector2 a, Vector2 b)
        {
            float dx = (a.x - b.x) * surface.WorldWidth;
            float dy = (a.y - b.y) * surface.WorldHeight;

            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private void ResolveSurface()
        {
            surface = FindFirstObjectByType<WhiteboardSurface>();
        }

        private void LogDebug(
            WhiteboardSurface.SurfaceSample sample,
            float tolerance,
            bool touching)
        {
            if (!debugLog)
                return;

            if (Time.unscaledTime < nextDebugTime)
                return;

            nextDebugTime = Time.unscaledTime + debugLogInterval;

            Debug.Log(
                "[LUNA BOARD] " + name + " (" + mode + ") " +
                (touching ? "TOUCH" : "miss") +
                " plane=" + (sample.planeDistance * 1000f).ToString("0.0") +
                "mm inside=" + sample.inside +
                " outside=" +
                (sample.outsideDistance * 1000f).ToString("0.0") +
                "mm tol=" + (tolerance * 1000f).ToString("0.0") +
                "mm uv=" + sample.uv.ToString("F3"),
                this
            );
        }

        /// <summary>
        /// 选中笔 / 擦子时画出接触点球与到板面的距离，
        /// 方便在 Scene 视图里对齐真实笔尖。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            WhiteboardSurface board = surface != null
                ? surface
                : FindFirstObjectByType<WhiteboardSurface>();

            Vector3 point = ContactPoint;
            float tolerance = Mathf.Max(0.001f, contactRadius);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(point, tolerance);

            if (board == null ||
                !board.TrySample(point, out WhiteboardSurface.SurfaceSample sample))
            {
                return;
            }

            bool touching = WhiteboardSurface.IsTouching(sample, tolerance);

            Gizmos.color = touching ? Color.green : Color.red;
            Gizmos.DrawSphere(point, 0.006f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(point, sample.closestPoint);
            Gizmos.DrawWireSphere(sample.closestPoint, 0.004f);
        }
    }
}
