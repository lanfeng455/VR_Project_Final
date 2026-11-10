#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oculus.Interaction.Locomotion;
using Oculus.Interaction.Surfaces;
using Oculus.Interaction.DistanceReticles;

namespace EchoDev.EditorTools
{
    /// <summary>
    /// 一键搭建白盒测试场景：Tools → EchoDev → 一键搭建白盒场景
    /// 创建：EchoWave 材质、黑环境、EchoWaveManager、玩家 Rig（含脚下脉冲）、
    ///       地面、可抓取测试道具、场景发声物。
    /// 幂等：已存在的同名对象会跳过，可反复执行。
    /// </summary>
    public static class EchoDevSetup
    {
        const string ShaderName = "EchoDev/EchoWave";
        const string ShaderPath = "Assets/Shaders/EchoWave.shader";
        const string MatPath = "Assets/Materials/EchoWave.mat";

        [MenuItem("Tools/EchoDev/搭建核心（材质+管理器+玩家Rig）")]
        public static void BuildCore()
        {
            Material echoMat = GetOrCreateMaterial();

            // 环境变黑（EchoWave 材质不受灯光影响，这里保证非回声物体也全黑）
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;

            if (FindAny<EchoWaveManager>() == null)
            {
                var go = new GameObject("EchoWaveManager");
                Undo.RegisterCreatedObjectUndo(go, "EchoDev Setup");
                go.AddComponent<EchoWaveManager>();
            }
            if (GameObject.Find("DevPlayer") == null) BuildPlayer();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[EchoDev] 核心搭建完成：EchoWave 材质、EchoWaveManager、DevPlayer（含脚下脉冲）。"
                    + "请把 Assets/Materials/EchoWave 拖给你自己场景里的地面和物体。");
        }

        [MenuItem("Tools/EchoDev/一键搭建白盒场景（核心+测试关卡）")]
        public static void BuildAll()
        {
            BuildCore();

            Material echoMat = GetOrCreateMaterial();
            if (GameObject.Find("EchoGround") == null) BuildGround(echoMat);
            if (GameObject.Find("EchoProps") == null) BuildProps(echoMat);
            if (GameObject.Find("EchoEmitters") == null) BuildEmitters(echoMat);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[EchoDev] 白盒测试场景搭建完成（含地面/测试道具/发声柱）。");
        }

        // ---------- 材质 ----------
        static Material GetOrCreateMaterial()
        {
            // 1) 先强制导入 shader（外部工具改过文件时，Unity 可能还没来得及导入）
            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceUpdate);

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[EchoDev] 找不到 shader：{ShaderName}。"
                    + "若 Console 里有红色 shader 报错请先修复；材质未做任何改动。");
                return null;
            }

            Material m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");
                m = new Material(shader) { name = "EchoWave" };
                AssetDatabase.CreateAsset(m, MatPath);
                AssetDatabase.SaveAssets();
                return m;
            }

            // 2) 已存在但缺新版属性 → 原位重置为 shader 默认值（不删文件，场景引用不断链）
            if (!m.HasProperty("_TrailGap") || !m.HasProperty("_OutlineWidth"))
            {
                var fresh = new Material(shader);
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    string n = shader.GetPropertyName(i);
                    switch (shader.GetPropertyType(i))
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                            m.SetColor(n, fresh.GetColor(n)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                            m.SetFloat(n, fresh.GetFloat(n)); break;
                        case UnityEngine.Rendering.ShaderPropertyType.Vector:
                            m.SetVector(n, fresh.GetVector(n)); break;
                    }
                }
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssets();
                Debug.Log("[EchoDev] EchoWave 材质已原位更新为新版默认值（引用保持不断链）");
            }
            return m;
        }

        /// <summary>把丢失材质引用（粉色）的渲染器重新指回 EchoWave 材质。</summary>
        [MenuItem("Tools/EchoDev/修复丢失材质引用（粉色物体）")]
        public static void FixMissingMaterialRefs()
        {
            Material mat = GetOrCreateMaterial();
            if (mat == null) return;

            int fixedCount = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.sharedMaterial == null)
                {
                    Undo.RecordObject(r, "Fix EchoWave Material");
                    r.sharedMaterial = mat;
                    fixedCount++;
                }
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[EchoDev] 已为 {fixedCount} 个渲染器补上 EchoWave 材质");
        }

        // ---------- 演出相机 Rig ----------
        [MenuItem("Tools/EchoDev/搭建演出相机 Rig（Cinemachine）")]
        public static void BuildCinematicRig()
        {
            if (GameObject.Find("CinematicRig") != null)
            {
                // 增量修补：旧版 rig 的演出相机缺 AudioListener → 补上
                //（演出相机与玩家相机互斥激活，两个 Listener 永不同时工作，无双活冲突）
                var oldRig = GameObject.Find("CinematicRig");
                var oldCam = oldRig.GetComponentInChildren<Camera>(true);
                if (oldCam != null && oldCam.GetComponent<AudioListener>() == null)
                {
                    Undo.AddComponent<AudioListener>(oldCam.gameObject);
                    Debug.Log("[EchoDev] 已为演出相机补挂 AudioListener（演出期间才有声音）");
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                }
                Debug.Log("[EchoDev] CinematicRig 已存在，跳过");
                return;
            }

            var rig = new GameObject("CinematicRig");
            Undo.RegisterCreatedObjectUndo(rig, "EchoDev CinematicRig");
            var ctl = rig.AddComponent<CinematicRig>();

            // 演出相机 + Brain + AudioListener（与玩家相机互斥激活，无双活）
            var camGo = new GameObject("CinematicCamera");
            camGo.transform.SetParent(rig.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CinemachineBrain>();
            camGo.layer = LayerMask.NameToLayer("Ignore Raycast");

            // 虚拟相机 + 震动接收端（Listener 必须挂在 vcam 上）
            var vcamGo = new GameObject("CinematicVcam");
            vcamGo.transform.SetParent(rig.transform, false);
            vcamGo.layer = LayerMask.NameToLayer("Ignore Raycast");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcamGo.AddComponent<CinemachineImpulseListener>();

            // 震动源（rig 根）
            var source = rig.AddComponent<CinemachineImpulseSource>();

            ctl.cinematicCamera = cam;
            ctl.vcam = vcam;
            ctl.impulseSource = source;

            // 自动指认玩家相机
            var player = GameObject.Find("DevPlayer");
            if (player != null)
            {
                var pc = player.GetComponentInChildren<Camera>(true);
                if (pc != null) ctl.playerCamera = pc;
            }

            rig.SetActive(false); // 平时禁用，Play() 时自动启用
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[EchoDev] CinematicRig 搭建完成（默认禁用）。"
                    + "摆好 CinematicPoint 定位点后，在事件链里调 CinematicRig.Play 即可演出。");
        }

        // ---------- VR 模式切换（Meta XR SDK）----------
        [MenuItem("Tools/EchoDev/切换 VR 模式（Meta Camera Rig）")]
        public static void ToggleVRMode()
        {
            // DevPlayer 可能整体下线（GameObject.Find 找不到 inactive），用组件反查
            var input = Object.FindFirstObjectByType<DevPlayerInput>(FindObjectsInactive.Include);
            var devPlayer = input != null ? input.gameObject : null;
            if (devPlayer == null)
            {
                Debug.LogError("[EchoDev] 场景里没有 DevPlayer，先跑 搭建核心");
                return;
            }

            var rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            if (rig == null)
            {
                Debug.LogError("[EchoDev] 场景里没有 Camera Rig。请先在 Meta XR 窗口"
                    + "（Project 窗口旁的 Meta XR 标签 → Building Blocks）添加 Camera Rig，再执行本菜单。");
                return;
            }

            bool toVR = devPlayer.activeSelf;

            if (toVR)
            {
                // VR 模式：Camera Rig 就是玩家本体，独立于场景根。
                // DevPlayer（PC 键鼠：输入/传送/射线/抓取）整体下线，
                // 移动/传送/交互全部交给 Meta 的 Locomotor。
                if (rig.transform.parent != null) // 兼容旧结构：曾把 rig 挂在 DevPlayer 下
                    Undo.SetTransformParent(rig.transform, null, "EchoDev VR Mode");
                Undo.RecordObject(rig.gameObject, "EchoDev VR Mode");
                rig.gameObject.SetActive(true);
                Undo.RecordObject(devPlayer, "EchoDev VR Mode");
                devPlayer.SetActive(false);
                Debug.Log("[EchoDev] 已切换 VR 模式：Camera Rig 独立为玩家，DevPlayer（PC 键鼠系统）已下线。\n"
                    + "后续手动步骤：① 在 OVRComprehensiveInteractionRig/Locomotor/LocomotionSettings 把 Controller Movement 设为 Teleport；"
                    + "② 把 EchoTeleportRule 挂到 Locomotor 下的 BodyTeleportInteractor（传送校验：只能传到声波覆盖区）。");
            }
            else
            {
                // PC 模式：DevPlayer 上线（含旧版切换时被单独禁用的相机/转向），Camera Rig 下线留在场景里
                Undo.RecordObject(devPlayer, "EchoDev PC Mode");
                devPlayer.SetActive(true);
                Transform pcCam = devPlayer.transform.Find("Main Camera");
                if (pcCam != null)
                {
                    Undo.RecordObject(pcCam.gameObject, "EchoDev PC Mode");
                    pcCam.gameObject.SetActive(true);
                }
                var look = devPlayer.GetComponent<DevPlayerLook>();
                if (look != null && !look.enabled)
                {
                    Undo.RecordObject(look, "EchoDev PC Mode");
                    look.enabled = true;
                }
                Undo.RecordObject(rig.gameObject, "EchoDev PC Mode");
                rig.gameObject.SetActive(false);
                Debug.Log("[EchoDev] 已切回 PC 键鼠模式（Camera Rig 留在场景里但已禁用）。");
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        // ---------- VR 传送表面（Lab 4 标准结构，支持非平面地形）----------
        [MenuItem("Tools/EchoDev/搭建 VR 传送表面（Lab4 结构）")]
        public static void BuildVRTeleportSurface()
        {
            if (GameObject.Find("LocomotionInteractable") != null)
            {
                Debug.LogWarning("[EchoDev] 场景里已有 LocomotionInteractable，跳过。如需重建请先删除。");
                return;
            }

            // 用法：选中地面物体（自带 Collider，如山洞地形的 MeshCollider）再执行本菜单，
            //       弧线将沿真实地形命中；未选中则生成一块平地 BoxCollider（默认 20m 见方）。
            Collider userCollider = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInChildren<Collider>()
                : null;

            var root = new GameObject("LocomotionInteractable");
            var interactableGo = new GameObject("ColliderInteractable");
            interactableGo.transform.SetParent(root.transform, false);

            Collider surfaceCollider;
            if (userCollider != null)
            {
                // 非平面地形：直接引用地面已有的 Collider（MeshCollider/TerrainCollider 均可）
                surfaceCollider = userCollider;
            }
            else
            {
                // 平地方案（Lab 4 原结构）：薄盒子盖住可走区域。
                // 位置默认对齐玩家 rig 所在地（rig 原点即玩家脚下），而不是世界原点——
                // 否则地面不在 y=0 的场景里，落点标记会出现在"地下"。
                var rig = Object.FindFirstObjectByType<OVRCameraRig>();
                var colliderGo = new GameObject("Collider");
                colliderGo.transform.SetParent(root.transform, false);
                colliderGo.transform.position = rig != null ? rig.transform.position : Vector3.zero;
                var box = colliderGo.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = new Vector3(20f, 0.2f, 20f);
                surfaceCollider = box;
            }

            // ColliderInteractable：传送目标 + 落点 Reticle + 表面
            var surface = interactableGo.AddComponent<ColliderSurface>();
            var interactable = interactableGo.AddComponent<TeleportInteractable>();
            interactableGo.AddComponent<ReticleDataTeleport>();

            // 连引用：ColliderSurface ← Collider；TeleportInteractable.Surface ← ColliderSurface
            var soSurface = new SerializedObject(surface);
            soSurface.FindProperty("_collider").objectReferenceValue = surfaceCollider;
            soSurface.ApplyModifiedPropertiesWithoutUndo();

            var soInteract = new SerializedObject(interactable);
            soInteract.FindProperty("_surface").objectReferenceValue = surface;
            soInteract.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(root, "EchoDev VR Teleport Surface");
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            if (userCollider != null)
            {
                Debug.Log($"[EchoDev] VR 传送表面搭建完成，已引用选中物体 {userCollider.gameObject.name} 的 {userCollider.GetType().Name}。\n"
                        + "测试（Lab4）：推左摇杆瞄准（Reticle 落点）→ 松开传送；摇杆左右 = Snap Turn。\n"
                        + "EchoTeleportRule 生效时：声波覆盖区可传、区外被拒（Console 打印拒绝日志）。");
            }
            else
            {
                Debug.Log("[EchoDev] VR 传送表面搭建完成（平地方案：20m BoxCollider，中心已对齐 rig 所在地）。\n"
                        + "地面不是平面的话：删掉本物体，选中山洞地面（需先给它加 MeshCollider）重跑本菜单。\n"
                        + "测试（Lab4）：推左摇杆瞄准（Reticle 落点）→ 松开传送；摇杆左右 = Snap Turn。");
            }
        }

        // ---------- VR 传送弧线（抛物线视觉，挂到 TeleportInteractor）----------
        [MenuItem("Tools/EchoDev/搭建 VR 传送弧线（抛物线视觉）")]
        public static void BuildVRTeleportArc()
        {
            // TeleportArcVisual：SDK 自带的弧线渲染器（把 TeleportInteractor 的弧线点写进 LineRenderer）。
            // OVRComprehensiveInteractionRig 默认不带任何弧线视觉——传送功能正常但看不到抛物线，
            // 需要单独补这个组件。
            var interactor = Object.FindFirstObjectByType<TeleportInteractor>();
            if (interactor == null)
            {
                Debug.LogError("[EchoDev] 场景里没有 TeleportInteractor（应在 OVRComprehensiveInteractionRig/Locomotor/BodyTeleportInteractor 上）。先放好 rig 再跑本菜单。");
                return;
            }
            if (interactor.GetComponentInChildren<TeleportArcVisual>(true) != null)
            {
                Debug.LogWarning("[EchoDev] TeleportInteractor 下已有 TeleportArcVisual，跳过。");
                return;
            }

            var arcGo = new GameObject("TeleportArcVisual");
            arcGo.transform.SetParent(interactor.transform, false);
            var line = arcGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;   // 弧线点是世界坐标
            line.widthMultiplier = 0.02f;
            line.positionCount = 0;
            line.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Line.mat");

            var arc = arcGo.AddComponent<TeleportArcVisual>();
            var so = new SerializedObject(arc);
            so.FindProperty("_interactor").objectReferenceValue = interactor;
            so.FindProperty("_arcRenderer").objectReferenceValue = line;
            so.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(arcGo, "EchoDev VR Teleport Arc");
            Selection.activeGameObject = arcGo;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[EchoDev] 传送弧线已挂到 {interactor.gameObject.name}。\n"
                    + "瞄准时若弧线一直常显（不瞄准也在），告诉我，我再加显隐控制。\n"
                    + "想调弧线外观：选中 TeleportArcVisual 物体，改 LineRenderer 的宽度/材质/颜色。");
        }

        // ---------- 地面 ----------
        static void BuildGround(Material mat)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "EchoGround";
            Undo.RegisterCreatedObjectUndo(ground, "EchoDev Setup");
            ground.transform.position = new Vector3(0, -0.5f, 0);
            ground.transform.localScale = new Vector3(60, 1, 60); // 顶面 y=0
            if (mat != null) ground.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ---------- 玩家 Rig ----------
        static void BuildPlayer()
        {
            var rig = new GameObject("DevPlayer");
            Undo.RegisterCreatedObjectUndo(rig, "EchoDev Setup");
            int ignoreRay = LayerMask.NameToLayer("IgnoreRaycast");
            SetLayerRecursive(rig.transform, ignoreRay); // 防止弧线/抓取射线打到自己

            var cc = rig.AddComponent<UnityEngine.CharacterController>(); // 限定：与 Oculus.Interaction.Locomotion.CharacterController 同名
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.9f, 0); // 根物体原点 = 脚底

            rig.AddComponent<DevPlayerInput>();
            var look = rig.AddComponent<DevPlayerLook>();
            rig.AddComponent<DevPlayerLocomotion>(); // teleportMode 在 Inspector 里按需勾选
            rig.AddComponent<DevPlayerGrab>();
            var ray = rig.AddComponent<DevPlayerRay>();
            ray.waveColor = new Color(1f, 0.55f, 0.15f); // 射线声波：橙

            // 主相机（子物体）
            var camGo = new GameObject("Main Camera");
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.6f, 0);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
            camGo.layer = ignoreRay;
            look.cameraTransform = camGo.transform;

            // 停用场景里已有的其他相机，避免抢占 MainCamera
            foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (other == cam || other.transform.IsChildOf(rig.transform)) continue;
                other.gameObject.SetActive(false);
                Debug.Log($"[EchoDev] 已停用旧相机：{other.name}");
            }

            // 脚下声波脉冲（小范围、周期性）
            var pulseGo = new GameObject("FootPulse");
            pulseGo.transform.SetParent(rig.transform, false);
            pulseGo.transform.localPosition = new Vector3(0, 0.2f, 0);
            pulseGo.layer = ignoreRay;
            var pulse = pulseGo.AddComponent<EchoEmitter>();
            pulse.interval = 1.5f;
            pulse.speed = 6f;
            pulse.maxRadius = 4f;
            pulse.strength = 0.6f;
            pulse.waveColor = new Color(0.35f, 0.85f, 1f); // 脚步声波：青

            rig.transform.position = new Vector3(0, 0.05f, 0);
        }

        // ---------- 可抓取测试道具 ----------
        static void BuildProps(Material mat)
        {
            var root = new GameObject("EchoProps");
            Undo.RegisterCreatedObjectUndo(root, "EchoDev Setup");

            MakeProp(PrimitiveType.Cube,    mat, root.transform, new Vector3(1.5f, 0.25f, 2f),  0.5f);
            MakeProp(PrimitiveType.Sphere,  mat, root.transform, new Vector3(-2f, 0.4f, 1.5f),  0.4f);
            MakeProp(PrimitiveType.Cube,    mat, root.transform, new Vector3(3f, 0.3f, -3f),    0.6f);   // 声波外，需射线/发声物才能看见
            MakeProp(PrimitiveType.Capsule, mat, root.transform, new Vector3(-1.5f, 0.5f, -2.5f), 1f);
        }

        static void MakeProp(PrimitiveType type, Material mat, Transform parent, Vector3 pos, float scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "Prop_" + type;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * scale;
            var rb = go.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ---------- 场景发声物 ----------
        static void BuildEmitters(Material mat)
        {
            var root = new GameObject("EchoEmitters");
            Undo.RegisterCreatedObjectUndo(root, "EchoDev Setup");

            MakeEmitter(mat, root.transform, new Vector3(8f, 1.25f, -6f), 0.8f, 2.5f, 8f, new Color(0.3f, 1f, 0.5f));    // 近处柱子：绿
            MakeEmitter(mat, root.transform, new Vector3(-10f, 1.25f, 5f), 0.8f, 2.5f, 10f, new Color(0.75f, 0.4f, 1f)); // 远处柱子：紫
        }

        static void MakeEmitter(Material mat, Transform parent, Vector3 pos, float radius, float height, float waveRadius, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "EchoEmitter";
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(radius, height, radius);
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;

            var em = go.AddComponent<EchoEmitter>();
            em.interval = 4f;
            em.speed = 5f;
            em.maxRadius = waveRadius;
            em.strength = 0.8f;
            em.waveColor = color;
        }

        // ---------- 工具 ----------
        static T FindAny<T>() where T : Object
        {
            var found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return found.Length > 0 ? found[0] : null;
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }
    }
}
#endif
