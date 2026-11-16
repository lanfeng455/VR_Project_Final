using System;
using System.Collections;
using System.Collections.Generic;
using Game.LLM;
using Game.TTS;
using Game.UI;
using TMPro;
using UnityEngine;

namespace Game.Interaction
{
    /// <summary>
    /// 可拾取的日记页：正文直接在 Inspector 里填，编辑模式即可改。
    /// 朗读时先用 LLM 按语义与情绪切段（并给出每段后的停顿），再逐段送 TTS，
    /// 用分段与停顿体现情绪——不依赖 Ronald 不支持的语气指令。
    /// LLM 不可用时退回现有切句逻辑，直接朗读原文。
    /// </summary>
    public sealed class DiaryPage : MonoBehaviour
    {
        private const string BeatSystemPrompt =
            "You prepare a diary entry for a text-to-speech reader. " +
            "Split the entry into 2 to 6 beats. A beat is one spoken " +
            "chunk that ends where a pause belongs. Decide each pause " +
            "to carry emotion: 1.2 to 2.0 seconds after a heavy, " +
            "shocking or final line; 0.25 to 0.5 seconds between lines " +
            "that belong together. Keep the original English wording " +
            "exactly: do not translate, summarise, add or remove words. " +
            "Never merge separate sentences into one beat. " +
            "Return only compact JSON, no markdown, no explanation: " +
            "{\"items\":[{\"text\":\"...\",\"pause\":0.6}]}";

        [Serializable]
        private sealed class Beat
        {
            public string text;
            public float pause;
        }

        [Serializable]
        private sealed class BeatList
        {
            public Beat[] items;
        }

        [Header("正文")]
        [Tooltip("多段正文直接写在这里，编辑模式下改完即可预览。")]
        [SerializeField, TextArea(10, 40)]
        private string body =
            "Day ten.\n\n" +
            "The clock in the hall stopped at 3:17. No one wound it " +
            "again.\n\n" +
            "If you are reading this, I did not make it to the shore.";

        [Tooltip("把正文显示到纸面上的文本，可留空。")]
        [SerializeField] private TMP_Text bodyText;

        [Header("朗读")]
        [Tooltip("留空时使用场景里的 LunaTTSController.Instance。")]
        [SerializeField] private LunaTTSController tts;

        [Tooltip("LLM 配置的 Resources 路径，不含 .json 后缀。")]
        [SerializeField] private string llmConfigResourcePath =
            "LLM/llm-config";

        [Tooltip("LLM 没有给出停顿时的默认间隔（秒）。")]
        [SerializeField, Min(0f)] private float defaultPauseSeconds = 0.45f;

        [Tooltip("单段停顿的上限，防止模型给出过长静默。")]
        [SerializeField, Min(0.2f)] private float maxPauseSeconds = 3f;

        [Header("临时测试按钮")]
        [Tooltip("关掉后角落按钮整体隐藏，交付前取消勾选即可。")]
        [SerializeField] private bool enableTestButton = true;

        [Tooltip("页面角落的临时测试按钮根节点。")]
        [SerializeField] private GameObject testButtonRoot;

        [Header("射线交互")]
        [Tooltip("纸张的 World Space Canvas；留空时自动取子物体上的 Canvas。" +
                 "会在运行时注册到 CanvasRayRegistry，供手柄射线与手戳命中。")]
        [SerializeField] private Canvas rayCanvas;

        private Coroutine readRoutine;

        public bool IsReading => readRoutine != null;

        public string Body => body;

        private void Awake()
        {
            if (rayCanvas == null)
                rayCanvas = GetComponentInChildren<Canvas>(true);

            ApplyBody();
            ApplyTestButton();
        }

        private void OnEnable()
        {
            if (rayCanvas == null)
                rayCanvas = GetComponentInChildren<Canvas>(true);

            // 电视屏的 Canvas 指定了 worldCamera，日记页的 prefab 里是空的。
            // GraphicRaycaster 在 World Space 下必须有相机才能算出命中，
            // 这里补上主相机，保证手柄射线与手戳都能打到按钮。
            if (rayCanvas != null &&
                rayCanvas.renderMode != RenderMode.ScreenSpaceOverlay &&
                rayCanvas.worldCamera == null)
            {
                rayCanvas.worldCamera = Camera.main;
            }

            CanvasRayRegistry.Register(rayCanvas);
        }

        private void OnValidate()
        {
            ApplyBody();
            ApplyTestButton();
        }

        private void OnDisable()
        {
            CanvasRayRegistry.Unregister(rayCanvas);

            // 协程会随组件禁用被 Unity 停掉，这里同步清掉句柄，
            // 否则重新激活后 IsReading 会一直为 true，按钮再也点不动。
            readRoutine = null;
        }

        /// <summary>手动入口：绑定到任意交互事件或脚本调用。</summary>
        public void ReadAloud()
        {
            if (!isActiveAndEnabled)
                return;

            // 朗读中再次触发时忽略，而不是从头重启：
            // 重启会把已经合成/播放的第一段反复取消，表现为「只听得到第一句」，
            // 日志里也会反复刷同一句的合成记录。
            if (readRoutine != null)
            {
                Debug.Log(
                    "[LUNA DIARY] already reading, ignoring the repeated " +
                    "read request."
                );

                return;
            }

            Debug.Log("[LUNA DIARY] read requested.");

            readRoutine = StartCoroutine(ReadRoutine());
        }

        /// <summary>临时测试按钮的点击入口。</summary>
        public void OnTestButtonClicked()
        {
            if (!enableTestButton)
                return;

            ReadAloud();
        }

        private void ApplyBody()
        {
            if (bodyText != null)
                bodyText.text = body;
        }

        private void ApplyTestButton()
        {
            if (testButtonRoot != null)
                testButtonRoot.SetActive(enableTestButton);
        }

        private IEnumerator ReadRoutine()
        {
            LunaTTSController controller =
                tts != null ? tts : LunaTTSController.Instance;

            if (controller == null)
            {
                Debug.LogWarning(
                    "DiaryPage: 场景里没有 LunaTTSController，无法朗读。",
                    this
                );

                readRoutine = null;
                yield break;
            }

            List<Beat> beats = null;

            ILLMProvider provider = CreateProvider();

            if (provider != null)
            {
                yield return PlanBeats(
                    provider,
                    planned => beats = planned
                );
            }

            if (beats == null || beats.Count == 0)
            {
                Debug.Log(
                    "[LUNA DIARY] LLM 分段不可用，回退到按句朗读原文。"
                );

                controller.ReadLetter(body);

                while (controller.IsSpeaking)
                    yield return null;

                Debug.Log("[LUNA DIARY] read finished (fallback).");

                readRoutine = null;
                yield break;
            }

            Debug.Log(
                "[LUNA DIARY] read begin: beats=" + beats.Count
            );

            for (int i = 0; i < beats.Count; i++)
            {
                Beat beat = beats[i];

                if (string.IsNullOrWhiteSpace(beat.text))
                {
                    Debug.LogWarning(
                        "[LUNA DIARY] beat " + (i + 1) + "/" +
                        beats.Count + " is empty, skipped."
                    );

                    continue;
                }

                float pause = beat.pause > 0f
                    ? Mathf.Min(beat.pause, maxPauseSeconds)
                    : defaultPauseSeconds;

                Debug.Log(
                    "[LUNA DIARY] beat " + (i + 1) + "/" + beats.Count +
                    " pause=" + pause.ToString("0.00") + "s: \"" +
                    Shorten(beat.text) + "\""
                );

                controller.ReadChunk(beat.text);

                while (controller.IsSpeaking)
                    yield return null;

                if (pause > 0f)
                    yield return new WaitForSecondsRealtime(pause);
            }

            Debug.Log("[LUNA DIARY] read finished.");

            readRoutine = null;
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string compact = text.Replace('\n', ' ').Trim();

            return compact.Length <= 60
                ? compact
                : compact.Substring(0, 60) + "...";
        }

        private ILLMProvider CreateProvider()
        {
            if (!LLMConfig.TryLoad(
                    llmConfigResourcePath,
                    out LLMConfig.Data data,
                    out string error))
            {
                Debug.LogWarning(
                    "[LUNA DIARY] LLM 配置不可用：" + error
                );

                return null;
            }

            return new DeepSeekProvider(data.apiKey, data.model);
        }

        private IEnumerator PlanBeats(
            ILLMProvider provider,
            Action<List<Beat>> onDone)
        {
            var messages = new List<LLMMessage>
            {
                new LLMMessage(LLMRole.System, BeatSystemPrompt),
                new LLMMessage(LLMRole.User, body)
            };

            LLMResult result = null;

            yield return provider.SendAsync(
                messages,
                r => result = r
            );

            if (result == null || !result.Success)
            {
                Debug.LogWarning(
                    "[LUNA DIARY] LLM 分段失败：" +
                    (result != null ? result.Error : "没有返回结果")
                );

                onDone(null);
                yield break;
            }

            onDone(ParseBeats(result.Content));
        }

        private List<Beat> ParseBeats(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');

            if (start < 0 || end <= start)
                return null;

            string json = content.Substring(start, end - start + 1);

            try
            {
                BeatList parsed = JsonUtility.FromJson<BeatList>(json);

                if (parsed == null || parsed.items == null)
                    return null;

                var beats = new List<Beat>(parsed.items.Length);

                for (int i = 0; i < parsed.items.Length; i++)
                {
                    Beat beat = parsed.items[i];

                    if (beat == null ||
                        string.IsNullOrWhiteSpace(beat.text))
                    {
                        continue;
                    }

                    beat.text = beat.text.Trim();
                    beats.Add(beat);
                }

                return beats.Count > 0 ? beats : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[LUNA DIARY] 分段 JSON 解析失败：" + e.Message
                );

                return null;
            }
        }
    }
}
