using Game.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    /// <summary>
    /// 白板 / 日记装配辅助。
    /// 只做「加组件 + 建一个必要的薄片或触发球」，
    /// 位置和尺寸仍由玩家在场景里摆。
    /// </summary>
    public static class LunaWhiteboardSetup
    {
        private const string SurfaceName = "Writing Surface";
        private const string TipName = "Tip";
        private const string ZoneName = "Snap Zone";
        private const string MenuRoot = "Tools/VR Project/LUNA/Whiteboard/";

        private const float SurfaceThickness = 0.006f;
        private const float SurfaceGap = 0.0015f;

        [MenuItem(MenuRoot + "Add Writing Surface To Selected Board")]
        public static void AddWritingSurface()
        {
            GameObject board = Selection.activeGameObject;

            if (board == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "Select the whiteboard model first.",
                    "OK"
                );

                return;
            }

            if (board.transform.Find(SurfaceName) != null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "'" + SurfaceName + "' already exists under " +
                    board.name + ".",
                    "OK"
                );

                return;
            }

            Vector3 localSize = new Vector3(1.2f, 0.8f, 0.05f);
            Vector3 localCenter = Vector3.zero;

            Renderer renderer = board.GetComponent<Renderer>();

            if (renderer != null)
            {
                localCenter = renderer.localBounds.center;
                localSize = renderer.localBounds.size;
            }
            else
            {
                BoxCollider box = board.GetComponent<BoxCollider>();

                if (box != null)
                {
                    localCenter = box.center;
                    localSize = box.size;
                }
                else
                {
                    Debug.LogWarning(
                        "[LUNA BOARD] " + board.name + " has no Renderer or " +
                        "BoxCollider; using a default 1.2 x 0.8 m surface."
                    );
                }
            }

            // 最薄的那个本地轴就是板面法线：把书写面贴在它朝外的一侧。
            int thinAxis = ThinnestAxis(localSize);

            float[] extents =
            {
                Mathf.Abs(localSize.x),
                Mathf.Abs(localSize.y),
                Mathf.Abs(localSize.z)
            };

            Vector3 localNormal = AxisVector(thinAxis);

            bool outwardFromCamera;
            int thinSign = ResolveOutwardSign(
                board.transform,
                localCenter,
                thinAxis,
                out outwardFromCamera
            );

            Vector3 surfaceZ = localNormal * thinSign;
            Vector3 surfaceY = PickSurfaceUp(board.transform, thinAxis);

            Vector3 surfaceX = Vector3.Cross(surfaceY, surfaceZ);

            float scaleAlongThin = Mathf.Max(
                1e-4f,
                Mathf.Abs(board.transform.lossyScale[thinAxis])
            );

            float localThickness = SurfaceThickness / scaleAlongThin;
            float localGap = SurfaceGap / scaleAlongThin;

            float offset = extents[thinAxis] * 0.5f +
                           localThickness * 0.5f +
                           localGap;

            GameObject surface = GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

            surface.name = SurfaceName;
            Undo.RegisterCreatedObjectUndo(
                surface,
                "Add whiteboard writing surface"
            );

            surface.transform.SetParent(board.transform, false);

            // 贴在包围盒最薄轴朝外的一面，抬高 1.5mm 防 z-fighting。
            surface.transform.localPosition =
                localCenter + localNormal * (thinSign * offset);

            // 本地 +Z 必须朝外，这样看到的是正面、笔迹不会左右反。
            surface.transform.localRotation = Quaternion.LookRotation(
                surfaceZ,
                surfaceY
            );

            surface.transform.localScale = new Vector3(
                extents[AxisIndex(surfaceX)],
                extents[AxisIndex(surfaceY)],
                localThickness
            );

            // 书写面不参与碰撞：笔和擦子不会被它顶住。
            Object.DestroyImmediate(surface.GetComponent<BoxCollider>());

            surface.AddComponent<WhiteboardSurface>();

            MarkSceneDirty();
            Selection.activeGameObject = surface;

            float runnerUp = extents[SecondThinnestAxis(localSize)];

            if (extents[thinAxis] > runnerUp * 0.75f)
            {
                Debug.LogWarning(
                    "[LUNA BOARD] " + board.name + " 的包围盒看不出明显的" +
                    "薄边（最薄轴 = " + AxisName(thinAxis) + "），已按最薄轴" +
                    "贴面。如果盖住了错误的那个面，请手动调整。"
                );
            }

            Debug.Log(
                "[LUNA BOARD] Added '" + SurfaceName + "' to " + board.name +
                " on local " + AxisName(thinAxis) +
                (thinSign > 0 ? "+" : "-") + " side. Surface +Z = " +
                surface.transform.forward.ToString("F2") +
                (outwardFromCamera
                    ? " (facing the camera that was used to pick 'outward')"
                    : " (no camera found; defaulted to +" +
                      AxisName(thinAxis) + ")") +
                ". 目视确认书写面朝房间里；如果朝向反了，绕 Y 轴转 180°。"
            );
        }

        private static int ThinnestAxis(Vector3 size)
        {
            Vector3 extents = new Vector3(
                Mathf.Abs(size.x),
                Mathf.Abs(size.y),
                Mathf.Abs(size.z)
            );

            if (extents.x <= extents.y && extents.x <= extents.z)
                return 0;

            return extents.y <= extents.z ? 1 : 2;
        }

        private static int SecondThinnestAxis(Vector3 size)
        {
            int thin = ThinnestAxis(size);

            Vector3 extents = new Vector3(
                Mathf.Abs(size.x),
                Mathf.Abs(size.y),
                Mathf.Abs(size.z)
            );

            int best = -1;

            for (int i = 0; i < 3; i++)
            {
                if (i == thin)
                    continue;

                if (best < 0 || extents[i] < extents[best])
                    best = i;
            }

            return best;
        }

        private static Vector3 AxisVector(int axis)
        {
            if (axis == 0)
                return Vector3.right;

            return axis == 1 ? Vector3.up : Vector3.forward;
        }

        private static string AxisName(int axis)
        {
            if (axis == 0)
                return "X";

            return axis == 1 ? "Y" : "Z";
        }

        private static int AxisIndex(Vector3 axis)
        {
            Vector3 abs = new Vector3(
                Mathf.Abs(axis.x),
                Mathf.Abs(axis.y),
                Mathf.Abs(axis.z)
            );

            if (abs.x >= abs.y && abs.x >= abs.z)
                return 0;

            return abs.y >= abs.z ? 1 : 2;
        }

        /// <summary>
        /// 用相机位置判断包围盒最薄轴的哪一侧朝外（朝观察者）。
        /// 找不到相机时返回 +1，并由调用方在日志里提示目视确认。
        /// </summary>
        private static int ResolveOutwardSign(
            Transform board,
            Vector3 localCenter,
            int thinAxis,
            out bool fromCamera)
        {
            fromCamera = false;

            Camera camera = ResolveEditorCamera();
            Vector3 worldAxis = board.TransformVector(AxisVector(thinAxis));

            if (camera == null || worldAxis.sqrMagnitude < 1e-8f)
                return 1;

            Vector3 toCamera =
                camera.transform.position - board.TransformPoint(localCenter);

            if (toCamera.sqrMagnitude < 1e-6f)
                return 1;

            fromCamera = true;

            return Vector3.Dot(toCamera, worldAxis.normalized) >= 0f ? 1 : -1;
        }

        /// <summary>
        /// 在剩下的两个轴里挑更接近世界竖直方向的那根作为书写面的 Y，
        /// 让板上的字是正的。
        /// </summary>
        private static Vector3 PickSurfaceUp(Transform board, int thinAxis)
        {
            Vector3 a = AxisVector((thinAxis + 1) % 3);
            Vector3 b = AxisVector((thinAxis + 2) % 3);

            float upA = Mathf.Abs(
                Vector3.Dot(board.TransformDirection(a).normalized, Vector3.up)
            );

            float upB = Mathf.Abs(
                Vector3.Dot(board.TransformDirection(b).normalized, Vector3.up)
            );

            Vector3 candidate = upA >= upB ? a : b;

            return Vector3.Dot(board.TransformDirection(candidate), Vector3.up) < 0f
                ? -candidate
                : candidate;
        }

        private static Camera ResolveEditorCamera()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;

            if (sceneView != null && sceneView.camera != null)
                return sceneView.camera;

            if (Camera.main != null)
                return Camera.main;

            Camera[] cameras = Camera.allCameras;

            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                    return cameras[i];
            }

            return null;
        }

        [MenuItem(MenuRoot + "Make Selected A Pen")]
        public static void MakePen()
        {
            MakeTool(WhiteboardTool.ToolMode.Pen);
        }

        [MenuItem(MenuRoot + "Make Selected An Eraser")]
        public static void MakeToolEraser()
        {
            MakeTool(WhiteboardTool.ToolMode.Eraser);
        }

        private static void MakeTool(WhiteboardTool.ToolMode mode)
        {
            GameObject target = Selection.activeGameObject;

            if (target == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "Select the pen / eraser object first.",
                    "OK"
                );

                return;
            }

            if (target.GetComponent<Grabbable>() == null)
                Undo.AddComponent<Grabbable>(target);

            Transform tip = target.transform.Find(TipName);

            if (tip == null)
            {
                var tipGo = new GameObject(TipName);

                Undo.RegisterCreatedObjectUndo(tipGo, "Add tool tip");
                tipGo.transform.SetParent(target.transform, false);

                string report;
                tipGo.transform.localPosition = ComputeTipLocalPosition(
                    target.transform,
                    out report
                );

                Debug.Log(
                    "[LUNA BOARD] '" + target.name + "' tip candidates:\n" +
                    report + "\n  chosen local=" +
                    tipGo.transform.localPosition.ToString("F3")
                );

                tip = tipGo.transform;
            }

            WhiteboardTool tool = target.GetComponent<WhiteboardTool>();
            bool freshTool = tool == null;

            if (tool == null)
                tool = Undo.AddComponent<WhiteboardTool>(target);

            var serialized = new SerializedObject(tool);
            serialized.FindProperty("mode").enumValueIndex = (int)mode;

            serialized.FindProperty("tip").objectReferenceValue = tip;

            serialized.FindProperty("surface").objectReferenceValue =
                Object.FindFirstObjectByType<WhiteboardSurface>(
                    FindObjectsInactive.Include
                );

            // 旧的 12mm 容差太紧，重跑菜单时放宽到 25mm；
            // 玩家自己调过的更大的值不动。
            SerializedProperty radius =
                serialized.FindProperty("contactRadius");

            if (radius.floatValue <= 0.0125f)
                radius.floatValue = 0.025f;

            SerializedProperty stay =
                serialized.FindProperty("stayRadiusMultiplier");

            if (stay.floatValue < 1f)
                stay.floatValue = 1.6f;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 只有笔默认锁定握笔姿态；日记、磁铁、擦子保持原样。
            Grabbable grabbable = target.GetComponent<Grabbable>();

            if (mode == WhiteboardTool.ToolMode.Pen &&
                freshTool &&
                grabbable != null)
            {
                var grabSerialized = new SerializedObject(grabbable);
                grabSerialized.FindProperty("lockGripOrientation")
                    .boolValue = true;
                grabSerialized.FindProperty("gripRotationEuler")
                    .vector3Value = new Vector3(25f, 0f, 0f);
                grabSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            MarkSceneDirty();
            Selection.activeGameObject = target;

            Debug.Log(
                "[LUNA BOARD] " + target.name + " is now a " +
                (mode == WhiteboardTool.ToolMode.Pen ? "pen" : "eraser") +
                ". Tip placed at one bounding-box endpoint; contactRadius = " +
                radius.floatValue.ToString("0.000") + "m (stay x" +
                stay.floatValue.ToString("0.0") + "). " +
                (mode == WhiteboardTool.ToolMode.Pen
                    ? "lockGripOrientation = " +
                      (grabbable != null && grabbable.LockGripOrientation
                          ? "on, gripRotationEuler = " +
                            grabbable.GripRotationEuler.ToString("F0")
                          : "off (勾上或跑 Record Pen Grip Rotation)") +
                      ". "
                    : string.Empty) +
                "Tip 不对就选中笔跑 Fix Pen Tip To Bounds Endpoint / " +
                "Snap Pen Tip To Writing Surface."
            );
        }

        /// <summary>
        /// 把笔当前的世界姿态记录成"相对手部锚点的握姿"，
        /// 写进 Grabbable.gripRotationEuler 并打开 lockGripOrientation。
        /// 用法：在 Scene 里把笔摆成想要的自然握姿，选中笔，跑这条菜单。
        /// </summary>
        [MenuItem(MenuRoot + "Record Pen Grip Rotation")]
        public static void RecordPenGripRotation()
        {
            GameObject selected = Selection.activeGameObject;
            Grabbable grabbable = selected != null
                ? selected.GetComponentInChildren<Grabbable>(true)
                : null;

            if (grabbable == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "Select the pen (the object with Grabbable) first.",
                    "OK"
                );

                return;
            }

            Transform anchor = FindHandAnchor();

            if (anchor == null)
            {
                Debug.LogWarning(
                    "[LUNA BOARD] 没找到 RightHandAnchor / LeftHandAnchor，" +
                    "无法换算相对姿态。当前姿态：localEuler=" +
                    grabbable.transform.localEulerAngles.ToString("F1") +
                    " worldEuler=" +
                    grabbable.transform.eulerAngles.ToString("F1") +
                    "。可把 worldEuler 减去手部锚点的欧拉角后手填到 " +
                    "gripRotationEuler。"
                );

                return;
            }

            Quaternion relative = Quaternion.Inverse(anchor.rotation) *
                                  grabbable.transform.rotation;

            Vector3 euler = relative.eulerAngles;

            var serialized = new SerializedObject(grabbable);
            serialized.FindProperty("gripRotationEuler").vector3Value = euler;
            serialized.FindProperty("lockGripOrientation").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            MarkSceneDirty();

            Debug.Log(
                "[LUNA BOARD] recorded grip pose of '" + grabbable.name +
                "' relative to " + anchor.name + ": gripRotationEuler = " +
                euler.ToString("F1") + " (lockGripOrientation = on). " +
                "X = 笔尖俯仰，Y = 绕手掌竖轴转，Z = 侧倾。"
            );
        }

        private static Transform FindHandAnchor()
        {
            GameObject right = GameObject.Find("RightHandAnchor");

            if (right != null)
                return right.transform;

            GameObject left = GameObject.Find("LeftHandAnchor");

            return left != null ? left.transform : null;
        }

        /// <summary>
        /// 对已经做好的笔重新算一次 Tip：不动别的东西，不必重建笔物体。
        /// </summary>
        [MenuItem(MenuRoot + "Fix Pen Tip To Bounds Endpoint")]
        public static void FixPenTip()
        {
            GameObject selected = Selection.activeGameObject;

            WhiteboardTool tool = selected != null
                ? selected.GetComponentInChildren<WhiteboardTool>(true)
                : null;

            if (tool == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "Select the pen (the object with WhiteboardTool) first.",
                    "OK"
                );

                return;
            }

            Transform target = tool.transform;
            Transform tip = target.Find(TipName);

            if (tip == null)
            {
                var tipGo = new GameObject(TipName);

                Undo.RegisterCreatedObjectUndo(tipGo, "Add tool tip");
                tipGo.transform.SetParent(target, false);
                tip = tipGo.transform;
            }

            string report;
            Vector3 localTip = ComputeTipLocalPosition(target, out report);

            Undo.RecordObject(tip, "Fix pen tip");
            tip.localPosition = localTip;

            var serialized = new SerializedObject(tool);
            serialized.FindProperty("tip").objectReferenceValue = tip;

            // 顺手把旧的 12mm 容差放宽到 25mm（只在它还是旧默认值时）。
            SerializedProperty radius =
                serialized.FindProperty("contactRadius");

            if (radius.floatValue <= 0.0125f)
                radius.floatValue = 0.025f;

            SerializedProperty stay =
                serialized.FindProperty("stayRadiusMultiplier");

            if (stay.floatValue < 1f)
                stay.floatValue = 1.6f;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            MarkSceneDirty();
            Selection.activeGameObject = target.gameObject;

            Debug.Log(
                "[LUNA BOARD] '" + target.name + "' tip candidates:\n" +
                report + "\n  chosen local=" + localTip.ToString("F3") +
                ", contactRadius = " + radius.floatValue.ToString("0.000") +
                "m. 选错了就在 Scene 里直接把 Tip 拖到真实笔尖（立即生效），" +
                "再跑一次 Snap Pen Tip To Writing Surface 校准。"
            );
        }

        /// 把当前 Tip 的世界位置钉到书写面上的最近点，用来快速校准。
        [MenuItem(MenuRoot + "Snap Pen Tip To Writing Surface")]
        public static void SnapPenTipToSurface()
        {
            GameObject selected = Selection.activeGameObject;

            WhiteboardTool tool = selected != null
                ? selected.GetComponentInChildren<WhiteboardTool>(true)
                : null;

            if (tool == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "Select the pen (the object with WhiteboardTool) first.",
                    "OK"
                );

                return;
            }

            WhiteboardSurface surface = Object.FindFirstObjectByType<
                WhiteboardSurface
            >(FindObjectsInactive.Include);

            if (surface == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "No WhiteboardSurface in the scene.",
                    "OK"
                );

                return;
            }

            Vector3 point = tool.ContactPoint;

            if (!surface.TrySample(
                    point,
                    out WhiteboardSurface.SurfaceSample sample))
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "The writing surface has no Renderer / size.",
                    "OK"
                );

                return;
            }

            Transform target = tool.transform;
            Transform tip = target.Find(TipName);

            if (tip == null)
            {
                var tipGo = new GameObject(TipName);

                Undo.RegisterCreatedObjectUndo(tipGo, "Add tool tip");
                tipGo.transform.SetParent(target, false);
                tip = tipGo.transform;

                var created = new SerializedObject(tool);
                created.FindProperty("tip").objectReferenceValue = tip;
                created.ApplyModifiedPropertiesWithoutUndo();
            }

            Undo.RecordObject(tip, "Snap pen tip");

            Vector3 before = tip.position;
            tip.position = sample.closestPoint;

            MarkSceneDirty();
            Selection.activeGameObject = target.gameObject;

            Debug.Log(
                "[LUNA BOARD] '" + target.name + "' tip snapped to the " +
                "writing surface: moved " +
                (Vector3.Distance(before, sample.closestPoint) * 1000f)
                .ToString("0.0") + "mm, plane distance is now 0 (" +
                "local=" +
                target.InverseTransformPoint(sample.closestPoint)
                    .ToString("F3") + ")."
            );
        }

        /// <summary>
        /// 纯数学自检：不进入 Play、不使用 GPU。
        /// 用已知本地坐标断言 u,v 落在预期位置，确保绘制与显示共用同一套公式。
        /// </summary>
        [MenuItem(MenuRoot + "Verify Stroke Mapping (Offline)")]
        public static void VerifyStrokeMapping()
        {
            Vector2 size = new Vector2(1.32f, 0.88f);

            int failures = 0;

            failures += AssertUv(
                "中心",
                WhiteboardSurface.LocalToUv(Vector3.zero, size),
                new Vector2(0.5f, 0.5f)
            );

            failures += AssertUv(
                "左下角",
                WhiteboardSurface.LocalToUv(
                    new Vector3(-size.x * 0.5f, -size.y * 0.5f, 0f),
                    size
                ),
                new Vector2(0f, 0f)
            );

            failures += AssertUv(
                "右上角",
                WhiteboardSurface.LocalToUv(
                    new Vector3(size.x * 0.5f, size.y * 0.5f, 0f),
                    size
                ),
                new Vector2(1f, 1f)
            );

            failures += AssertUv(
                "左中",
                WhiteboardSurface.LocalToUv(
                    new Vector3(-size.x * 0.25f, 0f, 0f),
                    size
                ),
                new Vector2(0.25f, 0.5f)
            );

            failures += AssertUv(
                "上中",
                WhiteboardSurface.LocalToUv(
                    new Vector3(0f, size.y * 0.25f, 0f),
                    size
                ),
                new Vector2(0.5f, 0.75f)
            );

            failures += AssertUv(
                "右下象限",
                WhiteboardSurface.LocalToUv(
                    new Vector3(size.x * 0.25f, -size.y * 0.25f, 0f),
                    size
                ),
                new Vector2(0.75f, 0.25f)
            );

            failures += AssertUv(
                "右中（与左中对称，证明没有镜像）",
                WhiteboardSurface.LocalToUv(
                    new Vector3(size.x * 0.25f, 0f, 0f),
                    size
                ),
                new Vector2(0.75f, 0.5f)
            );

            // 出界：本地坐标超出矩形时 u/v 必须落在 0~1 之外（交给 RT 裁掉），
            // 而不是被夹紧到边缘拖一条线。
            Vector2 outside = WhiteboardSurface.LocalToUv(
                new Vector3(size.x * 0.75f, 0f, 0f),
                size
            );

            if (outside.x <= 1f)
            {
                failures++;

                Debug.LogError(
                    "[LUNA BOARD] 自检失败：出界点应给出 u > 1，实际 " +
                    outside.ToString("F4")
                );
            }
            else
            {
                Debug.Log(
                    "[LUNA BOARD] 自检通过：出界点 u = " +
                    outside.x.ToString("F4") + " (> 1，会被 RT 裁掉)"
                );
            }

            if (failures == 0)
            {
                Debug.Log(
                    "[LUNA BOARD] 笔迹坐标自检全部通过（8/8）：" +
                    "u = local.x / 1.32 + 0.5，v = local.y / 0.88 + 0.5，" +
                    "绘制与显示使用同一公式、均不翻转。"
                );
            }
            else
            {
                Debug.LogError(
                    "[LUNA BOARD] 笔迹坐标自检失败 " + failures + " 项。"
                );
            }
        }

        private static int AssertUv(
            string label,
            Vector2 actual,
            Vector2 expected)
        {
            bool ok = Mathf.Abs(actual.x - expected.x) < 1e-4f &&
                      Mathf.Abs(actual.y - expected.y) < 1e-4f;

            if (ok)
            {
                Debug.Log(
                    "[LUNA BOARD] 自检通过：" + label + " -> " +
                    actual.ToString("F4")
                );

                return 0;
            }

            Debug.LogError(
                "[LUNA BOARD] 自检失败：" + label + " 期望 " +
                expected.ToString("F4") + "，实际 " + actual.ToString("F4")
            );

            return 1;
        }

        /// <summary>
        /// 按笔模型（含子物体渲染器）在本地空间的包围盒，枚举 6 个轴端点作为
        /// 笔尖候选，选离书写面最近的一个（没有书写面时取世界上最低的端点）。
        /// report 里列出每个候选的本地坐标与得分，供玩家核对。
        /// </summary>
        private static Vector3 ComputeTipLocalPosition(
            Transform target,
            out string report)
        {
            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);

            bool hasBounds = false;
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];

                if (renderer == null)
                    continue;

                Bounds worldBounds = renderer.bounds;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = new Vector3(
                        (corner & 1) == 0
                            ? worldBounds.min.x
                            : worldBounds.max.x,
                        (corner & 2) == 0
                            ? worldBounds.min.y
                            : worldBounds.max.y,
                        (corner & 4) == 0
                            ? worldBounds.min.z
                            : worldBounds.max.z
                    );

                    Vector3 local = target.InverseTransformPoint(world);

                    if (!hasBounds)
                    {
                        localBounds = new Bounds(local, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(local);
                    }
                }
            }

            if (!hasBounds)
            {
                report = "  没有找到 Renderer，退回 local (0, -0.5, 0)。";

                return new Vector3(0f, -0.5f, 0f);
            }

            Vector3 center = localBounds.center;
            Vector3 size = localBounds.size;

            WhiteboardSurface surface = Object.FindFirstObjectByType<
                WhiteboardSurface
            >(FindObjectsInactive.Include);

            var builder = new System.Text.StringBuilder();

            builder.Append("  bounds center=").Append(center.ToString("F3"))
                .Append(" size=").Append(size.ToString("F3"));

            builder.Append(surface != null
                ? " | 得分 = 到书写面的距离"
                : " | 场景里没有书写面，得分 = -worldY");

            Vector3 best = center;
            float bestScore = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 candidate = center;
                    candidate[axis] +=
                        sign * Mathf.Abs(size[axis]) * 0.5f;

                    Vector3 world = target.TransformPoint(candidate);

                    float score = surface != null
                        ? Vector3.Distance(world, surface.transform.position)
                        : -world.y;

                    builder.Append("\n    ").Append(AxisName(axis))
                        .Append(sign > 0 ? "+" : "-")
                        .Append(" local=").Append(candidate.ToString("F3"))
                        .Append(" world=").Append(world.ToString("F3"))
                        .Append(" score=").Append(score.ToString("F3"));

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            report = builder.ToString();

            return best;
        }

        [MenuItem(MenuRoot + "Make Selected A Magnet")]
        public static void MakeMagnet()
        {
            GameObject target = Selection.activeGameObject;

            if (target == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "Select the magnet object first.",
                    "OK"
                );

                return;
            }

            if (target.GetComponent<Grabbable>() == null)
                Undo.AddComponent<Grabbable>(target);

            Transform existing = target.transform.Find(ZoneName);

            if (existing == null)
            {
                var zoneGo = new GameObject(ZoneName);

                Undo.RegisterCreatedObjectUndo(zoneGo, "Add magnet zone");
                zoneGo.transform.SetParent(target.transform, false);

                SphereCollider sphere =
                    zoneGo.AddComponent<SphereCollider>();

                sphere.isTrigger = true;

                Renderer renderer = target.GetComponent<Renderer>();
                float radius = renderer != null
                    ? renderer.bounds.extents.magnitude
                    : 0.1f;

                sphere.radius = Mathf.Max(0.06f, radius * 2f);
                existing = zoneGo.transform;
            }

            MagnetAnchor anchor = target.GetComponent<MagnetAnchor>();

            if (anchor == null)
                anchor = Undo.AddComponent<MagnetAnchor>(target);

            MarkSceneDirty();
            Selection.activeGameObject = target;

            Debug.Log(
                "[LUNA BOARD] " + target.name +
                " is now a magnet anchor. Tune '" + ZoneName +
                "' sphere radius for the pickup distance."
            );
        }

        [MenuItem(MenuRoot + "Fix Diary Gravity And Grab")]
        public static void FixDiary()
        {
            DiaryPage page = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<DiaryPage>()
                : null;

            if (page == null)
            {
                page = Object.FindFirstObjectByType<DiaryPage>(
                    FindObjectsInactive.Include
                );
            }

            if (page == null)
            {
                EditorUtility.DisplayDialog(
                    "LUNA Whiteboard",
                    "No DiaryPage found. Select the diary object first.",
                    "OK"
                );

                return;
            }

            GameObject target = page.gameObject;

            Rigidbody body = target.GetComponent<Rigidbody>();

            if (body == null)
                body = Undo.AddComponent<Rigidbody>(target);

            body.useGravity = true;
            body.isKinematic = false;
            body.mass = 0.15f;

            if (target.GetComponent<Grabbable>() == null)
                Undo.AddComponent<Grabbable>(target);

            MarkSceneDirty();
            Selection.activeGameObject = target;

            Debug.Log(
                "[LUNA BOARD] " + target.name +
                " now falls after release (Rigidbody.useGravity = true)."
            );
        }

        private static void MarkSceneDirty()
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
