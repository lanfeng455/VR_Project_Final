using UnityEngine;

namespace EchoDev
{
    /// <summary>
    /// 场景内自发声物体：按周期从自身位置（贴地）发出声波。
    /// 例如：瀑布、机器、能发声的装置等。
    /// </summary>
    public class EchoEmitter : MonoBehaviour
    {
        [Header("发声周期")]
        [Tooltip("每隔多少秒发一圈波")]
        public float interval = 4f;
        [Tooltip("启动后延迟多少秒发第一圈")]
        public float startDelay = 0f;

        [Header("波参数")]
        public float speed = 5f;
        public float maxRadius = 8f;
        [Range(0f, 1f)] public float strength = 0.8f;
        public Color waveColor = new Color(0.3f, 1f, 0.5f); // 场景声源默认绿

        float timer;

        void OnEnable() => timer = -startDelay;

        void Update()
        {
            timer += Time.deltaTime;
            if (timer < interval) return;
            timer = 0f;
            Emit();
        }

        /// <summary>立刻发一圈波（也可被外部调用，比如碰撞发声）。波心 = 物体实际位置，y 轴生效。</summary>
        public void Emit()
        {
            if (EchoWaveManager.Instance == null) return;
            EchoWaveManager.Instance.Spawn(transform.position, speed, maxRadius, waveColor, strength);
        }
    }
}
