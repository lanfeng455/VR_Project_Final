using System.Collections.Generic;
using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 声波管理器：维护所有活动波（上限 16，满了淘汰最老的），
    /// 每帧把波数据写入全局 shader 数组，并提供传送合法性查询（只依赖数据层，与渲染无关）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class EchoWaveManager : MonoBehaviour
    {
        public const int MaxWaves = 16;

        public static EchoWaveManager Instance { get; private set; }

        static readonly int IDOrigins = Shader.PropertyToID("_EchoOrigins");
        static readonly int IDData = Shader.PropertyToID("_EchoData");
        static readonly int IDColors = Shader.PropertyToID("_EchoColors");
        static readonly int IDCount = Shader.PropertyToID("_EchoCount");

        readonly List<EchoWave> waves = new List<EchoWave>(MaxWaves);
        readonly Vector4[] originBuf = new Vector4[MaxWaves];
        readonly Vector4[] dataBuf = new Vector4[MaxWaves];
        readonly Vector4[] colorBuf = new Vector4[MaxWaves];

        public IReadOnlyList<EchoWave> Waves => waves;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                waves[i].Tick(dt);
                if (!waves[i].IsAlive) waves.RemoveAt(i);
            }
            PushShaderGlobals();
        }

        /// <summary>在 origin 处生成一圈带颜色的声波。</summary>
        public EchoWave Spawn(Vector3 origin, float speed, float maxRadius, Color color, float strength = 1f)
        {
            if (waves.Count >= MaxWaves) waves.RemoveAt(0); // 淘汰最老的
            var w = new EchoWave(origin, speed, maxRadius, strength, color);
            waves.Add(w);
            return w;
        }

        /// <summary>
        /// 传送合法性核心：该点是否被任意活动波覆盖。
        /// 三维球面判定，与视觉表现一致。
        /// </summary>
        public bool IsPointCovered(Vector3 point)
        {
            for (int i = 0; i < waves.Count; i++)
            {
                var w = waves[i];
                float dx = point.x - w.Origin.x;
                float dy = point.y - w.Origin.y;
                float dz = point.z - w.Origin.z;
                float r = w.CurrentRadius;
                if (dx * dx + dy * dy + dz * dz <= r * r) return true;
            }
            return false;
        }

        static readonly int IDHighlightPos = Shader.PropertyToID("_HighlightPos");
        static readonly int IDHighlightIntensity = Shader.PropertyToID("_HighlightIntensity");

        /// <summary>抓取/落地高亮：让单个物体无视波照明获得描边与边缘光（radius 传包围球半径）。</summary>
        public static void SetHighlight(Vector3 center, float radius, float intensity)
        {
            Shader.SetGlobalVector(IDHighlightPos, new Vector4(center.x, center.y, center.z, radius));
            Shader.SetGlobalFloat(IDHighlightIntensity, intensity);
        }

        public static void ClearHighlight() => SetHighlight(Vector3.zero, 1f, 0f);

        void PushShaderGlobals()
        {
            for (int i = 0; i < MaxWaves; i++)
            {
                if (i < waves.Count)
                {
                    var w = waves[i];
                    originBuf[i] = new Vector4(w.Origin.x, w.Origin.y, w.Origin.z, w.Strength);
                    dataBuf[i] = new Vector4(w.CurrentRadius, w.MaxRadius, w.Speed, w.Elapsed);
                    colorBuf[i] = w.Color;
                }
                else
                {
                    originBuf[i] = Vector4.zero;
                    dataBuf[i] = Vector4.zero;
                    colorBuf[i] = Vector4.zero;
                }
            }
            Shader.SetGlobalVectorArray(IDOrigins, originBuf);
            Shader.SetGlobalVectorArray(IDData, dataBuf);
            Shader.SetGlobalVectorArray(IDColors, colorBuf);
            Shader.SetGlobalInt(IDCount, waves.Count);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waves.Count; i++)
                Gizmos.DrawWireSphere(waves[i].Origin, waves[i].CurrentRadius);
        }
#endif
    }
}
