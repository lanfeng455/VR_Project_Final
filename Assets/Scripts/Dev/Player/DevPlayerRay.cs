using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 开发用声波射线：左键从画面中心发射，命中表面处激发一片大范围声波。
    /// 短暂显示射线轨迹，有冷却。没命中任何物体则不激发声波（回声需要表面）。
    /// </summary>
    public class DevPlayerRay : MonoBehaviour
    {
        [Header("射线")]
        public float maxDistance = 40f;
        public float rayCooldown = 3f;
        public float beamDuration = 0.15f;

        [Header("激发的声波参数")]
        public float waveSpeed = 8f;
        public float waveRadius = 12f;
        [Range(0f, 1f)] public float waveStrength = 1f;
        public Color waveColor = new Color(1f, 0.55f, 0.15f); // 射线声波默认橙

        DevPlayerInput input;
        Camera cam;
        LineRenderer beam;
        float cdRemaining;
        float beamTimer;

        void Awake()
        {
            input = GetComponent<DevPlayerInput>();
            cam = GetComponentInChildren<Camera>();

            // 射线轨迹（挂在独立子物体上，避免与其他 LineRenderer 冲突）
            var beamGo = new GameObject("EchoBeam");
            beamGo.transform.SetParent(transform, false);
            beamGo.layer = EchoLayers.IgnoreRaycast;
            beam = beamGo.AddComponent<LineRenderer>();
            beam.widthMultiplier = 0.02f;
            beam.positionCount = 2;
            beam.enabled = false;
            beam.useWorldSpace = true;
            beam.material = MakeDevMat(new Color(0.5f, 0.9f, 1f, 0.9f));
        }

        void Update()
        {
            if (cdRemaining > 0f) cdRemaining -= Time.deltaTime;
            if (beamTimer > 0f)
            {
                beamTimer -= Time.deltaTime;
                if (beamTimer <= 0f) beam.enabled = false;
            }

            if (input.RayPressed && cdRemaining <= 0f)
                Fire();
        }

        void Fire()
        {
            cdRemaining = rayCooldown;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance)) return;

            // 命中表面 → 激发大范围声波
            if (EchoWaveManager.Instance != null)
                EchoWaveManager.Instance.Spawn(hit.point, waveSpeed, waveRadius, waveColor, waveStrength);

            // 射线轨迹（从枪口位置到命中点，稍微偏移避免贴脸）
            Vector3 muzzle = cam.transform.position + cam.transform.right * 0.2f - cam.transform.up * 0.15f;
            beam.SetPosition(0, muzzle);
            beam.SetPosition(1, hit.point);
            beam.enabled = true;
            beamTimer = beamDuration;
        }

        static Material MakeDevMat(Color c)
        {
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = new Material(s);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }
    }
}
