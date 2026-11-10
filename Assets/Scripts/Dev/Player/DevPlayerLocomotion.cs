using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 开发用移动：Inspector 勾选 teleportMode 决定模式（仅编辑器设置，运行时不切换）。
    /// Walk：WASD 相对朝向平滑移动 + 重力。
    /// Teleport：从相机抛出弧线，右键传送到落点（仅限声波覆盖区域内），有冷却。
    /// 注意：玩家根物体需放在 Ignore Raycast 层，避免弧线打到自己。
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(DevPlayerInput))]
    public class DevPlayerLocomotion : MonoBehaviour
    {
        [Header("模式（仅编辑器勾选，运行时不切换）")]
        public bool teleportMode = false;

        [Header("Walk 模式")]
        public float walkSpeed = 4f;
        public float acceleration = 12f;
        public float gravity = -20f;

        [Header("Teleport 模式")]
        public float launchSpeed = 10f;
        public float arcGravity = -15f;
        public float maxFlightTime = 2.2f;
        [Range(8, 64)] public int arcSegments = 36;
        public float teleportCooldown = 2f;

        CharacterController cc;
        DevPlayerInput input;
        Camera cam;
        LineRenderer arcLine;
        Transform marker;
        Material markerMat;
        Vector3[] arcPoints;

        Vector3 horizontalVel;
        float verticalVel;
        float cdRemaining;

        Vector3 landingPoint;
        bool hasLanding;
        bool landingValid;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            input = GetComponent<DevPlayerInput>();
            cam = GetComponentInChildren<Camera>();
            arcPoints = new Vector3[arcSegments + 2];
            BuildVisuals();
        }

        void Update()
        {
            if (cdRemaining > 0f) cdRemaining -= Time.deltaTime;
            ApplyGravity();
            if (teleportMode) UpdateTeleport();
            else UpdateWalk();
        }

        // ---------- 共用：重力贴地 ----------
        void ApplyGravity()
        {
            if (cc.isGrounded && verticalVel < 0f) verticalVel = -2f;
            verticalVel += gravity * Time.deltaTime;
            cc.Move(Vector3.up * verticalVel * Time.deltaTime);
        }

        // ---------- Walk ----------
        void UpdateWalk()
        {
            SetArcVisible(false);

            Vector2 m = input.Move;
            Vector3 target = transform.right * m.x + transform.forward * m.y;
            if (target.sqrMagnitude > 1f) target.Normalize();
            target *= walkSpeed;
            horizontalVel = Vector3.MoveTowards(horizontalVel, target, acceleration * Time.deltaTime);

            cc.Move(horizontalVel * Time.deltaTime);
        }

        // ---------- Teleport ----------
        void UpdateTeleport()
        {
            SimulateArc();
            if (input.TeleportPressed) TryTeleport();
        }

        void SimulateArc()
        {
            SetArcVisible(true);
            hasLanding = false;
            landingValid = false;

            Vector3 pos = cam.transform.position;
            Vector3 vel = cam.transform.forward * launchSpeed;
            float dt = maxFlightTime / arcSegments;
            int count = 0;

            for (int i = 0; i < arcSegments; i++)
            {
                Vector3 next = pos + vel * dt;
                vel.y += arcGravity * dt;
                arcPoints[count++] = pos;

                Vector3 seg = next - pos;
                if (Physics.Raycast(pos, seg, out RaycastHit hit, seg.magnitude))
                {
                    landingPoint = hit.point;
                    hasLanding = true;
                    arcPoints[count++] = hit.point;
                    break;
                }
                pos = next;
            }

            // 写入 LineRenderer
            arcLine.positionCount = count;
            for (int i = 0; i < count; i++) arcLine.SetPosition(i, arcPoints[i]);

            // 落点合法性：必须被活动声波覆盖
            if (hasLanding && EchoWaveManager.Instance != null)
                landingValid = EchoWaveManager.Instance.IsPointCovered(landingPoint);

            UpdateMarker();
        }

        void UpdateMarker()
        {
            if (marker == null) return;
            marker.gameObject.SetActive(hasLanding);
            if (!hasLanding) return;

            marker.position = landingPoint + Vector3.up * 0.03f;

            Color c;
            if (cdRemaining > 0f) c = new Color(0.5f, 0.5f, 0.5f, 0.5f);        // 冷却中
            else if (landingValid) c = new Color(0.2f, 1f, 0.3f, 0.6f);          // 可传送
            else c = new Color(1f, 0.25f, 0.2f, 0.6f);                           // 声波未覆盖
            if (markerMat != null) markerMat.color = c;
        }

        void TryTeleport()
        {
            if (cdRemaining > 0f) return;
            if (!hasLanding || !landingValid) return;

            cc.enabled = false;
            transform.position = landingPoint + Vector3.up * 0.1f; // 根物体原点在脚底
            cc.enabled = true;
            horizontalVel = Vector3.zero;
            verticalVel = 0f;
            cdRemaining = teleportCooldown;
        }

        // ---------- 视觉件（代码自建，免手动配置） ----------
        void SetArcVisible(bool v)
        {
            if (arcLine != null) arcLine.enabled = v;
            if (!v && marker != null) marker.gameObject.SetActive(false);
        }

        void BuildVisuals()
        {
            // 弧线（挂在独立子物体上：同一物体不允许重复挂 LineRenderer，AddComponent 会返回 null）
            var arcGo = new GameObject("TeleportArc");
            arcGo.transform.SetParent(transform, false);
            arcGo.layer = EchoLayers.IgnoreRaycast;
            arcLine = arcGo.AddComponent<LineRenderer>();
            arcLine.widthMultiplier = 0.03f;
            arcLine.positionCount = 0;
            arcLine.useWorldSpace = true;
            arcLine.material = MakeDevMat(new Color(0.4f, 0.8f, 1f, 0.9f));

            // 落点标记（扁平圆盘）
            var markerGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            markerGo.name = "TeleportMarker";
            Destroy(markerGo.GetComponent<Collider>()); // 防止挡住射线
            markerGo.layer = EchoLayers.IgnoreRaycast;
            markerGo.transform.SetParent(transform, false);
            markerGo.transform.localScale = new Vector3(1.2f, 0.02f, 1.2f);
            markerMat = MakeDevMat(Color.white);
            markerGo.GetComponent<Renderer>().material = markerMat;
            markerGo.SetActive(false);
            marker = markerGo.transform;
        }

        static Material MakeDevMat(Color c)
        {
            // 开发用简易材质：优先内置 Sprites/Default（支持透明）
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = new Material(s);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }
    }
}
