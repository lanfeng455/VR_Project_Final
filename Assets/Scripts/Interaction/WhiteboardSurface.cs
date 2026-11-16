using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Interaction
{
    /// <summary>
    /// 白板书写面：笔迹只存在一张 RenderTexture 里，
    /// 表面材质每帧采样这张 RT，不在 Shader 里保存任何笔画。
    ///
    /// 约定：书写面的本地 +Z 是朝外的正面，本地 X/Y 对应笔迹的 U/V。
    /// 把这块薄长方体贴在板面上时，必须让它 +Z 朝向玩家；
    /// 朝向偏了会被 Start 的自检日志报出来，但不会自动改朝向。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WhiteboardSurface : MonoBehaviour
    {
        [Header("Stroke Map")]
        [Tooltip("笔迹贴图宽度，2048 足够一块普通白板。")]
        [SerializeField, Min(256)] private int textureWidth = 2048;

        [SerializeField, Min(256)] private int textureHeight = 1024;

        [Tooltip("板面底色，擦子就是用它盖掉笔迹。")]
        [SerializeField] private Color backgroundColor = Color.white;

        [Tooltip("默认墨水颜色，笔上如果没单独设颜色就用它。")]
        [SerializeField] private Color inkColor =
            new Color(0.07f, 0.08f, 0.11f, 1f);

        [Tooltip("兜底开关：只有在换平台 / 特殊约束下上下反了才勾。" +
                 "正常路径不做任何翻转，默认不勾。")]
        [SerializeField] private bool flipVertical;

        [Tooltip("兜底开关：只有左右反了才勾。正常路径不做任何翻转，默认不勾。" +
                 "与 flipVertical 相互独立。")]
        [SerializeField] private bool flipHorizontal;

        [Header("Size")]
        [Tooltip("书写面世界尺寸（米），填 0 表示从 Renderer/Collider 自动取。")]
        [SerializeField] private Vector2 worldSizeOverride;

        private Renderer surfaceRenderer;
        private Material surfaceMaterial;
        private Material brushMaterial;
        private RenderTexture strokeMap;
        private CommandBuffer paintBuffer;
        private Vector3 localCenter;
        private Vector3 localSize = new Vector3(1f, 1f, 0.006f);
        private bool sizeResolved;
        private bool ready;

        public bool IsReady => ready;
        public RenderTexture StrokeMap => strokeMap;
        public Color BackgroundColor => backgroundColor;
        public Color InkColor => inkColor;
        public float WorldWidth { get; private set; }
        public float WorldHeight { get; private set; }
        public float Thickness => Mathf.Abs(localSize.z * transform.lossyScale.z);

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            LogFacingCheck();
        }

        /// <summary>
        /// 启动自检：只报告书写面 +Z 是否背离相机，不改朝向
        /// （朝向是玩家在场景里手调的，自动改会和手调打架）。
        /// </summary>
        private void LogFacingCheck()
        {
            Camera camera = ResolveCamera();

            if (camera == null)
            {
                Debug.Log(
                    "[LUNA BOARD] facing check skipped: no active camera.",
                    this
                );

                return;
            }

            Vector3 toCamera = camera.transform.position - transform.position;

            if (toCamera.sqrMagnitude < 1e-6f)
                return;

            float angle = Vector3.Angle(transform.forward, toCamera);

            if (angle > 90f)
            {
                Debug.LogWarning(
                    "[LUNA BOARD] '" + name + "' 的本地 +Z 背对相机（夹角 " +
                    angle.ToString("0") + "°，> 90°）。" +
                    "笔迹可能上下左右反向：把它绕 Y 轴转 180°（或重跑 " +
                    "Tools/VR Project/LUNA/Whiteboard/Add Writing Surface " +
                    "To Selected Board），或按需勾选 flipHorizontal / " +
                    "flipVertical。",
                    this
                );
            }
        }

        private static Camera ResolveCamera()
        {
            Camera camera = Camera.main;

            if (camera != null)
                return camera;

            Camera[] cameras = Camera.allCameras;

            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                    return cameras[i];
            }

            return null;
        }

        private void OnDestroy()
        {
            if (surfaceMaterial != null)
                Destroy(surfaceMaterial);

            if (brushMaterial != null)
                Destroy(brushMaterial);

            if (strokeMap != null)
            {
                strokeMap.Release();
                Destroy(strokeMap);
            }
        }

        private void Initialize()
        {
            if (ready)
                return;

            surfaceRenderer = GetComponent<Renderer>();

            if (surfaceRenderer == null)
            {
                Debug.LogError(
                    "[LUNA BOARD] WhiteboardSurface 需要挂在带 Renderer 的物体上。",
                    this
                );

                return;
            }

            ResolveSize();
            sizeResolved = true;
            CreateStrokeMap();
            CreateMaterials();
            BindMaterial();

            if (surfaceMaterial == null || brushMaterial == null)
                return;

            ready = true;
            ClearBoard();

            Debug.Log(
                "[LUNA BOARD] ready: " + textureWidth + "x" + textureHeight +
                " world=" + WorldWidth.ToString("0.00") + "x" +
                WorldHeight.ToString("0.00") + "m" +
                " flipV=" + flipVertical + " flipH=" + flipHorizontal
            );

            if (flipVertical || flipHorizontal)
            {
                Debug.LogWarning(
                    "[LUNA BOARD] flipVertical / flipHorizontal 当前是勾上的。" +
                    "正常路径已经让绘制与显示共用同一套本地坐标，" +
                    "请先把两个都取消，再判断是朝向问题还是别的。",
                    this
                );
            }
        }

        /// <summary>
        /// 只保证板面尺寸可用：编辑器里（没进 Play）也能做纯数学判定，
        /// 不会创建 RT / 材质。
        /// </summary>
        public bool EnsureSize()
        {
            if (sizeResolved)
                return true;

            if (surfaceRenderer == null)
                surfaceRenderer = GetComponent<Renderer>();

            if (surfaceRenderer == null)
                return false;

            ResolveSize();
            sizeResolved = true;

            return true;
        }

        private void ResolveSize()
        {
            if (surfaceRenderer != null)
            {
                Bounds bounds = surfaceRenderer.localBounds;
                localCenter = bounds.center;
                localSize = bounds.size;
            }
            else
            {
                BoxCollider box = GetComponent<BoxCollider>();

                if (box != null)
                {
                    localCenter = box.center;
                    localSize = box.size;
                }
            }

            if (worldSizeOverride.x > 0f && worldSizeOverride.y > 0f)
            {
                Vector3 scale = transform.lossyScale;

                localSize.x = worldSizeOverride.x /
                              Mathf.Max(1e-4f, Mathf.Abs(scale.x));

                localSize.y = worldSizeOverride.y /
                              Mathf.Max(1e-4f, Mathf.Abs(scale.y));
            }

            Vector3 lossy = transform.lossyScale;

            WorldWidth = Mathf.Abs(localSize.x * lossy.x);
            WorldHeight = Mathf.Abs(localSize.y * lossy.y);
        }

        private void CreateStrokeMap()
        {
            strokeMap = new RenderTexture(
                textureWidth,
                textureHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            )
            {
                name = "Whiteboard Strokes",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false
            };

            strokeMap.Create();
            paintBuffer = new CommandBuffer { name = "Whiteboard Paint" };
        }

        private void CreateMaterials()
        {
            Shader surfaceShader = Resources.Load<Shader>(
                "Shaders/WhiteboardSurface"
            );

            if (surfaceShader == null)
                surfaceShader = Shader.Find("Luna/WhiteboardSurface");

            Shader brushShader = Resources.Load<Shader>(
                "Shaders/WhiteboardBrush"
            );

            if (brushShader == null)
                brushShader = Shader.Find("Luna/WhiteboardBrush");

            if (surfaceShader == null || brushShader == null)
            {
                Debug.LogError(
                    "[LUNA BOARD] 找不到 Luna/WhiteboardSurface 或 " +
                    "Luna/WhiteboardBrush Shader。",
                    this
                );

                return;
            }

            surfaceMaterial = new Material(surfaceShader)
            {
                name = "Whiteboard Surface (Runtime)"
            };

            surfaceMaterial.SetTexture("_MainTex", strokeMap);

            // 显示端按物体本地坐标采样，必须知道这块板在本地空间里的
            // 中心与尺寸（与笔迹 UV 用的是同一组数）。
            ApplySurfaceUniforms();

            brushMaterial = new Material(brushShader)
            {
                name = "Whiteboard Brush (Runtime)"
            };
        }

        private void ApplySurfaceUniforms()
        {
            if (surfaceMaterial == null)
                return;

            surfaceMaterial.SetVector(
                "_SurfaceCenter",
                new Vector4(localCenter.x, localCenter.y, localCenter.z, 0f)
            );

            surfaceMaterial.SetVector(
                "_SurfaceSize",
                new Vector4(localSize.x, localSize.y, localSize.z, 0f)
            );
        }

        private void BindMaterial()
        {
            if (surfaceMaterial == null)
                return;

            // 这个材质是运行时新建的实例，直接用 sharedMaterial 赋值，
            // 避免 Unity 再克隆一份、导致 RT 引用对不上。
            surfaceRenderer.sharedMaterial = surfaceMaterial;
        }

        /// <summary>清空整块板（只在这里清 RT，平时不碰）。</summary>
        public void ClearBoard()
        {
            if (!EnsureReady())
                return;

            RenderTexture previous = RenderTexture.active;

            paintBuffer.Clear();
            paintBuffer.SetRenderTarget(strokeMap);
            paintBuffer.ClearRenderTarget(false, true, backgroundColor);

            Graphics.ExecuteCommandBuffer(paintBuffer);

            RestoreTarget(previous);
        }

        /// <summary>
        /// 笔尖/擦子接触判定：以 tipRadius 为半径的球与板面相交，
        /// 且投影点落在板面矩形内。只测笔尖点，笔身不会出墨。
        /// 纯数学判定，不读任何 Collider，也不做射线。
        /// </summary>
        public bool TryGetContact(
            Vector3 worldPoint,
            float tipRadius,
            out Vector2 uv)
        {
            if (!TrySample(worldPoint, out SurfaceSample sample))
            {
                uv = Vector2.zero;

                return false;
            }

            uv = sample.uv;

            return IsTouching(sample, tipRadius);
        }

        /// <summary>
        /// 笔尖到书写面的数学距离，用于接触判定 / 迟滞 / 调试。
        /// 完全不依赖 Collider 或射线，板面没有碰撞体也能用。
        ///
        /// UV 由物体本地坐标算出（不读网格 UV）：
        ///   u = local.x / size.x + 0.5
        ///   v = local.y / size.y + 0.5
        /// 与显示端 Shader 的公式完全一致。
        /// </summary>
        public bool TrySample(Vector3 worldPoint, out SurfaceSample sample)
        {
            sample = default;

            if (!EnsureSize())
                return false;

            Vector3 local =
                transform.InverseTransformPoint(worldPoint) - localCenter;

            // 统一换算到世界尺寸，这样书写面被缩放成薄片后照样有效。
            Vector3 lossy = transform.lossyScale;
            float scaleX = Mathf.Max(1e-4f, Mathf.Abs(lossy.x));
            float scaleY = Mathf.Max(1e-4f, Mathf.Abs(lossy.y));
            float scaleZ = Mathf.Max(1e-4f, Mathf.Abs(lossy.z));

            float halfDepth =
                Mathf.Max(1e-4f, Mathf.Abs(localSize.z) * scaleZ) * 0.5f;

            // 厚度方向：笔从侧面 / 大角度戳过来也只用垂直距离判定，
            // 只要离平面够近就算接触。
            sample.planeDistance = Mathf.Max(
                0f,
                Mathf.Abs(local.z * scaleZ) - halfDepth
            );

            Vector2 size = new Vector2(localSize.x, localSize.y);

            sample.uv = LocalToUv(local, size);

            sample.inside = sample.uv.x >= 0f && sample.uv.x <= 1f &&
                            sample.uv.y >= 0f && sample.uv.y <= 1f;

            float overflowX = Mathf.Max(
                0f,
                Mathf.Abs(local.x) - Mathf.Abs(size.x) * 0.5f
            ) * scaleX;

            float overflowY = Mathf.Max(
                0f,
                Mathf.Abs(local.y) - Mathf.Abs(size.y) * 0.5f
            ) * scaleY;

            sample.outsideDistance = Mathf.Sqrt(
                overflowX * overflowX + overflowY * overflowY
            );

            // 板上的最近点（世界），调试 Gizmo / 吸附校准用。
            Vector3 closestLocal = new Vector3(
                Mathf.Clamp(
                    local.x,
                    -Mathf.Abs(localSize.x) * 0.5f,
                    Mathf.Abs(localSize.x) * 0.5f
                ),
                Mathf.Clamp(
                    local.y,
                    -Mathf.Abs(localSize.y) * 0.5f,
                    Mathf.Abs(localSize.y) * 0.5f
                ),
                0f
            ) + localCenter;

            sample.closestPoint = transform.TransformPoint(closestLocal);

            return true;
        }

        /// <summary>
        /// 本地坐标 → 板面 UV。绘制与显示共用这一套公式，
        /// 两边都不再使用网格 UV，也不再默认翻转。
        /// </summary>
        public static Vector2 LocalToUv(Vector3 localOffset, Vector2 size)
        {
            return new Vector2(
                localOffset.x / Mathf.Max(1e-4f, size.x) + 0.5f,
                localOffset.y / Mathf.Max(1e-4f, size.y) + 0.5f
            );
        }

        /// <summary>
        /// 接触 = 投影点在板面矩形内，且到平面的距离在容差内。
        /// 出界按"本地坐标超出矩形"裁掉，不做 UV 夹紧。
        /// </summary>
        public static bool IsTouching(SurfaceSample sample, float tolerance)
        {
            return sample.inside && sample.planeDistance <= tolerance;
        }

        /// <summary>一次采样的结果（距离用米）。</summary>
        public struct SurfaceSample
        {
            /// <summary>点到板面平面的垂直距离（已扣掉薄片厚度）。</summary>
            public float planeDistance;

            /// <summary>超出板面矩形的距离，0 表示落在板面范围内。</summary>
            public float outsideDistance;

            /// <summary>投影点是否落在板面矩形内（出界不画）。</summary>
            public bool inside;

            /// <summary>板上的最近点（世界坐标）。</summary>
            public Vector3 closestPoint;

            /// <summary>笔迹 UV（未夹紧，可能小于 0 或大于 1）。</summary>
            public Vector2 uv;
        }

        /// <summary>
        /// 画一段带圆头的线段（UV 空间）。a == b 时就是一个圆点。
        /// 只有真正落笔 / 擦除时才调用，不会每帧清空 RT。
        /// </summary>
        public void PaintSegment(
            Vector2 from,
            Vector2 to,
            float worldRadius,
            Color color)
        {
            if (!EnsureReady() || paintBuffer == null || brushMaterial == null)
                return;

            // 正常路径不做任何翻转：绘制与显示共用 LocalToUv 这一套坐标，
            // RT 的 Y 方向由 Brush Shader 里的 UNITY_UV_STARTS_AT_TOP 处理。
            // flipHorizontal / flipVertical 只作为兜底，默认都不勾。
            if (flipHorizontal)
            {
                from.x = 1f - from.x;
                to.x = 1f - to.x;
            }

            if (flipVertical)
            {
                from.y = 1f - from.y;
                to.y = 1f - to.y;
            }

            float radiusU = worldRadius / Mathf.Max(1e-4f, WorldWidth);
            float radiusV = worldRadius / Mathf.Max(1e-4f, WorldHeight);

            Vector2 min = Vector2.Min(from, to) -
                          new Vector2(radiusU, radiusV);

            Vector2 max = Vector2.Max(from, to) +
                          new Vector2(radiusU, radiusV);

            brushMaterial.SetVector("_BrushA", from);
            brushMaterial.SetVector("_BrushB", to);
            brushMaterial.SetVector("_BrushMin", min);
            brushMaterial.SetVector("_BrushMax", max);
            brushMaterial.SetVector(
                "_BrushRadius",
                new Vector2(
                    Mathf.Max(1e-5f, radiusU),
                    Mathf.Max(1e-5f, radiusV)
                )
            );
            brushMaterial.SetColor("_BrushColor", color);

            RenderTexture previous = RenderTexture.active;

            paintBuffer.Clear();
            paintBuffer.SetRenderTarget(strokeMap);
            paintBuffer.DrawProcedural(
                Matrix4x4.identity,
                brushMaterial,
                0,
                MeshTopology.Triangles,
                6,
                1
            );

            Graphics.ExecuteCommandBuffer(paintBuffer);

            RestoreTarget(previous);
        }

        private bool EnsureReady()
        {
            if (!ready && strokeMap == null)
                Initialize();

            return ready;
        }

        private static void RestoreTarget(RenderTexture previous)
        {
            if (RenderTexture.active != previous)
                Graphics.SetRenderTarget(previous);
        }
    }
}
