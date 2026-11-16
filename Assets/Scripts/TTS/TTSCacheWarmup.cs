using System.Collections;
using UnityEngine;

namespace Game.TTS
{
    /// <summary>
    /// 临时测试用：进入 Play 后在后台把几句固定短语合成进缓存。
    /// 只写缓存，不显示气泡、不播放、不写电视屏和对话历史。
    /// 交付前取消勾选 enableWarmup（或删掉本组件）即可。
    /// </summary>
    public sealed class TTSCacheWarmup : MonoBehaviour
    {
        [Tooltip("关掉后完全不预热。交付时取消勾选即可。")]
        [SerializeField] private bool enableWarmup = true;

        [Tooltip("留空时使用场景里的 LunaTTSController.Instance。")]
        [SerializeField] private LunaTTSController tts;

        [Tooltip("逐条预热；每条都是独立缓存条目。")]
        [SerializeField]
        private string[] phrases = { "Yes", "No", "Irrelevant" };

        [Tooltip("进入 Play 后等多久开始，给 STT 与配置留出初始化时间。")]
        [SerializeField, Min(0f)] private float startDelaySeconds = 1.5f;

        [Tooltip("每条之间的间隔，避免同时打满并发配额。")]
        [SerializeField, Min(0f)] private float gapSeconds = 0.35f;

        [Tooltip("找不到 LunaTTSController 时最多等多久。")]
        [SerializeField, Min(0f)] private float waitForControllerSeconds = 15f;

        private void Start()
        {
            if (!enableWarmup)
                return;

            StartCoroutine(WarmRoutine());
        }

        private IEnumerator WarmRoutine()
        {
            if (startDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(
                    startDelaySeconds
                );
            }

            LunaTTSController controller = null;
            float deadline =
                Time.unscaledTime + waitForControllerSeconds;

            while (controller == null && Time.unscaledTime < deadline)
            {
                controller = tts != null ? tts : LunaTTSController.Instance;

                if (controller == null)
                    yield return null;
            }

            if (controller == null)
            {
                Debug.LogWarning(
                    "[LUNA WARMUP] 场景里没有 LunaTTSController，跳过预热。"
                );

                yield break;
            }

            if (controller.IsPlaceholderProvider)
            {
                Debug.Log(
                    "[LUNA WARMUP] 当前离线占位 provider，跳过预热。"
                );

                yield break;
            }

            int hit = 0;
            int synthesized = 0;
            int failed = 0;

            for (int i = 0; i < phrases.Length; i++)
            {
                string phrase = phrases[i];

                if (string.IsNullOrWhiteSpace(phrase))
                    continue;

                bool fromCache = false;
                bool ok = false;

                yield return controller.WarmCache(
                    phrase.Trim(),
                    (cached, success) =>
                    {
                        fromCache = cached;
                        ok = success;
                    }
                );

                if (!ok)
                    failed++;
                else if (fromCache)
                    hit++;
                else
                    synthesized++;

                if (gapSeconds > 0f)
                    yield return new WaitForSecondsRealtime(gapSeconds);
            }

            Debug.Log(
                "[LUNA WARMUP] done: cached=" + hit +
                " synthesized=" + synthesized +
                " failed=" + failed
            );
        }
    }
}
