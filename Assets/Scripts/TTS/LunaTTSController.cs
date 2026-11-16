using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.NPC;
using Game.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Serialization;

namespace Game.TTS
{
    public enum AudioRoute
    {
        /// <summary>从电视位置发出的 3D 声源（默认）。</summary>
        TvPosition = 0,

        /// <summary>贴耳 2D 声源。</summary>
        NearEar = 1
    }

    public enum TTSProviderMode
    {
        Auto = 0,
        Placeholder = 1,
        Cloud = 2
    }

    /// <summary>
    /// LUNA 的语音输出总控：
    /// 订阅 DialogueBubble.SpeechRequested，先查磁盘缓存，未命中再调 ITTSProvider，
    /// 合成结果落盘后播放；播放结束、失败或超时都恰好回调一次 NotifySpeechFinished()。
    /// 只负责语音与气泡文字节奏，不改动 STT、工具调用和电视界面。
    /// </summary>
    public sealed class LunaTTSController : MonoBehaviour
    {
        public static LunaTTSController Instance { get; private set; }

        [Header("Provider")]
        [Tooltip("Auto：有可用云配置就用云，否则用离线占位实现。")]
        [SerializeField] private TTSProviderMode providerMode =
            TTSProviderMode.Auto;

        [Tooltip("Resources 下的配置路径，不含 .json 后缀。")]
        [SerializeField] private string configResourcePath =
            "TTS/tts-config";

        [Header("Cache")]
        [Tooltip("启用磁盘语音缓存。关掉后完全不读写缓存目录，每句话都重新合成并" +
                 "用临时文件播放——用于确认音频链路本身是否正常。")]
        [FormerlySerializedAs("enableCache")]
        [SerializeField] private bool cacheEnabled = true;
        [SerializeField] private string cacheFolderName = "tts-cache";

        [Tooltip("缓存目录容量上限（MB），超出后按最久未使用淘汰。")]
        [SerializeField, Min(1)] private int cacheMaxMegabytes = 64;

        [Header("Audio Route")]
        [Tooltip("默认从电视位置以 3D 声源播放；NearEar 为贴耳 2D。")]
        [SerializeField] private AudioRoute audioRoute = AudioRoute.TvPosition;

        [Tooltip("电视位置的 3D 声源，建议挂在电视附近。")]
        [SerializeField] private AudioSource tvSource;

        [Tooltip("贴耳 2D 声源，spatialBlend 会被设为 0。")]
        [SerializeField] private AudioSource nearEarSource;

        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField, Min(1f)] private float tvMinDistance = 4f;
        [SerializeField, Min(2f)] private float tvMaxDistance = 25f;

        [Header("Audio Diagnostics")]
        [Tooltip("播放前后打印音频链路诊断（限频）：clip 的声道/采样率/峰值、" +
                 "声源参数、监听器音量与距离衰减。建议一直开着。")]
        [SerializeField] private bool audioDiagnostics = true;

        [Tooltip("3D 声源在当前距离上的估算增益低于该值时，本句改用 2D 播放，" +
                 "避免「播了但完全听不见」。")]
        [SerializeField, Range(0f, 1f)] private float minAudibleGain = 0.3f;

        [Tooltip("允许 3D 声源过远时自动回退到 2D。关掉后只在日志里报警。")]
        [SerializeField] private bool autoFallbackToQuiet3D = true;

        [Header("Timing")]
        [Tooltip("单块合成的最长等待时间，超时按失败降级。")]
        [SerializeField, Min(1f)] private float synthesisTimeoutSeconds = 12f;

        [Tooltip("两句之间的停顿。")]
        [SerializeField, Min(0f)] private float sentenceGapSeconds = 0.05f;

        [Tooltip("最后一句读完后气泡停留的时间。")]
        [SerializeField, Min(0f)] private float holdAfterSpeechSeconds = 2.5f;

        [Header("Sentence Chunking")]
        [Tooltip("过短的句子向后合并，避免一句话只占一屏。")]
        [SerializeField, Min(20)] private int minSentenceChars = 55;

        [Tooltip("单块最大字符数，保证一句能放进气泡。")]
        [SerializeField, Min(40)] private int maxSentenceChars = 140;

        [Header("Thinking")]
        [SerializeField] private string thinkingText = "THINKING...";
        [SerializeField] private bool animateThinking = true;
        [SerializeField, Min(0.1f)] private float thinkingDotInterval = 0.45f;

        [Header("Letter Reading")]
        [Tooltip("在编辑器 / PC 上按 R 朗读下面的调试信件。")]
        [SerializeField] private bool enableDebugReadKey = true;

        [SerializeField, TextArea(5, 14)]
        private string debugLetterText =
            "Dear player,\n\n" +
            "The house remembers what its owners tried to forget. " +
            "Ten days before the storm, the clock in the hall stopped " +
            "at 3:17. No one wound it again.\n\n" +
            "If you are reading this, I did not make it to the shore. " +
            "Look behind the painting in the study. What you find there " +
            "is the reason I never came back.\n\n" +
            "Forgive me.\n\n" +
            "- E.";

        [Tooltip("可选：用于订阅 LLM 请求的开始/结束，显示 THINKING。")]
        [SerializeField] private NPCChatController npc;

        private sealed class ClipJob
        {
            public string Text;
            public string Path;
            public string Key;
            public TTSAudioFormat Format;
            public bool FromCache;
            public bool Failed;
            public bool IsTemp;
            public bool Rebuilt;
            public string Error;
        }

        private ITTSProvider provider;
        private TTSCache cache;

        private Coroutine speechRoutine;
        private Coroutine thinkingRoutine;

        private DialogueBubble subscribedBubble;
        private bool bubbleSubscribed;
        private bool npcBound;

        private int generation;
        private bool speechActive;
        private bool bubbleSpeechPending;
        private bool thinkingVisible;
        private float nextNpcSearchTime;

        /// <summary>每段朗读只打印一次完整音频链路，避免刷屏。</summary>
        private bool audioChainLogged;

        /// <summary>双路解码对照每次运行只做一次，避免每句话都多解码两遍。</summary>
        private bool decodeProbeDone;

        /// <summary>
        /// 低于这个峰值就当作「解码异常」：正常语音峰值在 0.3–0.9，
        /// 只有 0.02（约 -34 dBFS）都不到的振幅才说明数据或解码出了问题。
        /// </summary>
        private const float AbnormalClipPeak = 0.02f;

        public AudioRoute CurrentRoute => audioRoute;
        public string ProviderName => provider != null ? provider.Name : "none";
        public string ProviderVoice => provider != null ? provider.Voice : string.Empty;
        public string ProviderModel => provider != null ? provider.Model : string.Empty;
        public string CacheDirectory => cache != null ? cache.DirectoryPath : string.Empty;
        public bool IsSpeaking => speechActive;
        public bool IsThinkingVisible => thinkingVisible;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            BuildProvider();
            InitializeCache();
            ConfigureSources();
        }

        private void OnEnable()
        {
            bubbleSubscribed = false;
            npcBound = false;
        }

        private void Start()
        {
            TrySubscribeBubble();
            TryBindNpc();
        }

        private void Update()
        {
            if (!bubbleSubscribed)
                TrySubscribeBubble();

            if (!npcBound &&
                Time.unscaledTime >= nextNpcSearchTime)
            {
                nextNpcSearchTime = Time.unscaledTime + 1f;
                TryBindNpc();
            }

#if ENABLE_INPUT_SYSTEM
            if (enableDebugReadKey &&
                UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.rKey
                    .wasPressedThisFrame)
            {
                ReadLetter(debugLetterText);
            }
#else
            if (enableDebugReadKey && Input.GetKeyDown(KeyCode.R))
                ReadLetter(debugLetterText);
#endif
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopSpeechRoutine();
        }

        private void OnDestroy()
        {
            Unsubscribe();

            if (Instance == this)
                Instance = null;
        }

        // ---------------- 公开入口 ----------------

        /// <summary>切换声音来源：电视 3D 或贴耳 2D。</summary>
        public void SetAudioRoute(AudioRoute route)
        {
            audioRoute = route;
        }

        /// <summary>朗读一封长信件（按句切块，边合成边播）。</summary>
        public void ReadLetter(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            StopSpeechRoutine();

            List<string> sentences = SentenceSplitter.Split(
                text,
                minSentenceChars,
                maxSentenceChars
            );

            if (sentences.Count == 0)
                return;

            BeginSpeech(sentences, false);
        }

        /// <summary>
        /// 朗读一段已经由外部规划好节奏的文本：
        /// 仍按句切分，但不做「过短句子向后合并」，保证分段边界不被吞掉。
        /// </summary>
        public void ReadChunk(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            StopSpeechRoutine();

            List<string> sentences = SentenceSplitter.Split(
                text,
                1,
                maxSentenceChars
            );

            BeginSpeech(sentences, false);
        }

        private void BeginSpeech(
            List<string> sentences,
            bool ownsBubble)
        {
            if (sentences == null || sentences.Count == 0)
                return;

            generation++;
            int gen = generation;

            speechActive = true;
            audioChainLogged = false;
            speechRoutine = StartCoroutine(
                SpeakRoutine(sentences, gen, ownsBubble)
            );
        }

        /// <summary>是否只是离线占位实现（此时预热没有意义）。</summary>
        public bool IsPlaceholderProvider =>
            provider == null || provider.Name == "placeholder";

        /// <summary>
        /// 只合成并写入缓存：不显示气泡、不播放、不写对话历史。
        /// 回调参数依次为「是否命中缓存」「是否成功」。
        /// </summary>
        public IEnumerator WarmCache(
            string text,
            Action<bool, bool> onComplete)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onComplete?.Invoke(false, false);
                yield break;
            }

            if (IsPlaceholderProvider || !provider.IsAvailable)
            {
                onComplete?.Invoke(false, false);
                yield break;
            }

            // 缓存被关掉（或初始化失败）时预热没有意义：合成出来也无处可存。
            if (cache == null)
            {
                onComplete?.Invoke(false, false);
                yield break;
            }

            string key = null;

            if (cache != null)
            {
                key = cache.BuildKey(
                    text,
                    provider.Voice,
                    provider.Model,
                    provider.Name
                );

                if (cache.TryGet(key, out _, out _))
                {
                    Debug.Log(
                        "[LUNA WARMUP] hit: \"" + Shorten(text) + "\""
                    );

                    onComplete?.Invoke(true, true);
                    yield break;
                }
            }

            TTSResult result = null;
            bool finished = false;

            yield return SynthesizeWithTimeout(
                text,
                r =>
                {
                    result = r;
                    finished = true;
                }
            );

            if (!finished || result == null || !result.Success)
            {
                Debug.LogWarning(
                    "[LUNA WARMUP] failed: \"" + Shorten(text) + "\" " +
                    (result != null ? result.Error : "timeout")
                );

                onComplete?.Invoke(false, false);
                yield break;
            }

            if (!TTSCache.TryDetectFormat(
                    result.AudioData,
                    out TTSAudioFormat warmFormat))
            {
                Debug.LogWarning(
                    "[LUNA WARMUP] rejected non-audio response: \"" +
                    Shorten(text) + "\" head=" +
                    HeadHex(result.AudioData)
                );

                onComplete?.Invoke(false, false);
                yield break;
            }

            if (cache != null && key != null)
            {
                try
                {
                    cache.Store(key, result.AudioData, warmFormat);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        "[LUNA WARMUP] cache write failed: " + e.Message
                    );
                }
            }

            Debug.Log(
                "[LUNA WARMUP] synthesized: \"" + Shorten(text) + "\""
            );

            onComplete?.Invoke(false, true);
        }

        /// <summary>显示 THINKING 占位（LLM 请求期间也走这里）。</summary>
        public void ShowThinking()
        {
            DialogueBubble bubble = DialogueBubble.Instance;

            if (bubble == null)
                return;

            thinkingVisible = true;
            bubble.ShowSticky(thinkingText);

            if (!animateThinking)
                return;

            if (thinkingRoutine == null)
            {
                thinkingRoutine = StartCoroutine(
                    ThinkingDotsRoutine()
                );
            }
        }

        /// <summary>停止当前朗读与占位（不会隐藏已经显示的正文）。</summary>
        public void StopSpeaking()
        {
            StopSpeechRoutine();
        }

        // ---------------- 初始化 ----------------

        private void BuildProvider()
        {
            TTSConfig.TryLoad(
                configResourcePath,
                out TTSConfig.Data data,
                out string configError
            );

            bool cloudReady = IsCloudReady(data);

            switch (providerMode)
            {
                case TTSProviderMode.Placeholder:
                    provider = new PlaceholderTTSProvider();
                    break;

                case TTSProviderMode.Cloud:
                    if (cloudReady)
                    {
                        provider = CreateCloudProvider(data);
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[LUNA TTS] Cloud mode requested but the " +
                            "TTS config is incomplete. Falling back to " +
                            "the offline placeholder provider."
                        );

                        provider = new PlaceholderTTSProvider();
                    }

                    break;

                default:
                    provider = cloudReady
                        ? CreateCloudProvider(data)
                        : new PlaceholderTTSProvider();
                    break;
            }

            Debug.Log(
                $"[LUNA TTS] provider={provider.Name} " +
                $"voice={provider.Voice} model={provider.Model} " +
                (cloudReady
                    ? "config=loaded"
                    : $"config={(string.IsNullOrEmpty(configError) ? "not loaded" : configError)}")
            );
        }

        private static bool IsCloudReady(TTSConfig.Data data)
        {
            if (data == null)
                return false;

            if (data.provider == "volcengine")
            {
                bool hasAuth =
                    (!string.IsNullOrWhiteSpace(data.appId) &&
                     !string.IsNullOrWhiteSpace(data.accessToken)) ||
                    !string.IsNullOrWhiteSpace(data.apiKey);

                return hasAuth &&
                       !string.IsNullOrWhiteSpace(data.voice);
            }

            return !string.IsNullOrWhiteSpace(data.endpoint) &&
                   !string.IsNullOrWhiteSpace(data.apiKey);
        }

        private static ITTSProvider CreateCloudProvider(
            TTSConfig.Data data)
        {
            if (data.provider == "volcengine")
            {
                return new VolcengineTTSProvider(
                    data.endpoint,
                    data.appId,
                    data.accessToken,
                    data.apiKey,
                    data.resourceId,
                    data.voice,
                    data.model,
                    data.instruction,
                    TTSConfig.ParseFormat(data.responseFormat),
                    24000,
                    data.speed
                );
            }

            return new OpenAiCompatibleTTSProvider(
                data.endpoint,
                data.apiKey,
                data.model,
                data.voice,
                TTSConfig.ParseFormat(data.responseFormat),
                data.speed
            );
        }

        private void InitializeCache()
        {
            if (!cacheEnabled)
            {
                Debug.Log(
                    "[LUNA TTS] cache disabled (cacheEnabled = false): " +
                    "nothing is read from or written to the cache folder; " +
                    "every sentence is synthesized and played from a temp file."
                );

                return;
            }

            long maxBytes =
                (long)Mathf.Max(1, cacheMaxMegabytes) * 1024L * 1024L;

            string directory = Path.Combine(
                Application.persistentDataPath,
                cacheFolderName
            );

            try
            {
                cache = new TTSCache(directory, maxBytes);

                Debug.Log(
                    $"[LUNA TTS] cache={directory} files={cache.Count} " +
                    $"bytes={cache.CurrentBytes} limit={maxBytes} " +
                    $"dropped={cache.DroppedOnLoad}"
                );

                if (cache.DroppedOnLoad > 0)
                {
                    Debug.LogWarning(
                        "[LUNA TTS] removed " + cache.DroppedOnLoad +
                        " unreadable cached clip(s); they will be " +
                        "re-synthesized when used."
                    );
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[LUNA TTS] Failed to initialize the speech cache: " +
                    e.Message
                );

                cache = null;
            }
        }

        private void ConfigureSources()
        {
            if (tvSource != null)
            {
                tvSource.playOnAwake = false;
                tvSource.loop = false;
                tvSource.spatialBlend = 1f;
                tvSource.dopplerLevel = 0f;
                tvSource.mute = false;
                tvSource.volume = Mathf.Clamp01(volume);

                if (tvMaxDistance <= tvMinDistance)
                {
                    Debug.LogWarning(
                        "[LUNA TTS] TV Speaker maxDistance (" +
                        tvMaxDistance.ToString("0.0") +
                        ") <= minDistance (" + tvMinDistance.ToString("0.0") +
                        "); using minDistance * 10 instead."
                    );

                    tvMaxDistance = tvMinDistance * 10f;
                }

                tvSource.minDistance = tvMinDistance;
                tvSource.maxDistance = tvMaxDistance;
                tvSource.rolloffMode = AudioRolloffMode.Logarithmic;
            }

            if (nearEarSource != null)
            {
                nearEarSource.playOnAwake = false;
                nearEarSource.loop = false;
                nearEarSource.spatialBlend = 0f;
                nearEarSource.mute = false;
                nearEarSource.volume = Mathf.Clamp01(volume);
            }
        }

        // ---------------- 订阅 ----------------

        private void TrySubscribeBubble()
        {
            DialogueBubble bubble = DialogueBubble.Instance;

            if (bubble == null)
                return;

            bubble.SpeechRequested -= HandleSpeechRequested;
            bubble.SpeechRequested += HandleSpeechRequested;

            subscribedBubble = bubble;
            bubbleSubscribed = true;
        }

        private void TryBindNpc()
        {
            if (npc == null)
            {
                npc = FindFirstObjectByType<NPCChatController>(
                    FindObjectsInactive.Include
                );
            }

            if (npc == null)
                return;

            npc.RequestStarted -= HandleRequestStarted;
            npc.RequestStarted += HandleRequestStarted;
            npc.RequestFinished -= HandleRequestFinished;
            npc.RequestFinished += HandleRequestFinished;
            npcBound = true;
        }

        private void Unsubscribe()
        {
            if (subscribedBubble != null)
            {
                subscribedBubble.SpeechRequested -=
                    HandleSpeechRequested;

                subscribedBubble = null;
            }

            if (npc != null)
            {
                npc.RequestStarted -= HandleRequestStarted;
                npc.RequestFinished -= HandleRequestFinished;
            }

            bubbleSubscribed = false;
            npcBound = false;
        }

        // ---------------- 事件 ----------------

        private void HandleSpeechRequested(string text)
        {
            // 上一段朗读（或上一封邮件）先正常收尾，避免气泡卡在 30 秒超时。
            StopSpeechRoutine();

            if (DialogueBubble.Instance == null)
                return;

            if (string.IsNullOrWhiteSpace(text))
            {
                DialogueBubble.Instance.NotifySpeechFinished();
                return;
            }

            List<string> sentences = SentenceSplitter.Split(
                text,
                minSentenceChars,
                maxSentenceChars
            );

            if (sentences.Count == 0)
            {
                DialogueBubble.Instance.NotifySpeechFinished();
                return;
            }

            generation++;
            int gen = generation;

            bubbleSpeechPending = true;
            speechActive = true;

            speechRoutine = StartCoroutine(
                SpeakRoutine(sentences, gen, true)
            );
        }

        private void HandleRequestStarted()
        {
            // 新问题优先：停掉正在朗读的内容，并立即显示同一个占位状态。
            StopSpeechRoutine();
            ShowThinking();
        }

        private void HandleRequestFinished(bool success)
        {
            // 请求成功时正文已经写进气泡：只停掉省略号动画，不要盖住正文。
            // 如果 TTS 需要联网合成，SpeechRequested 会重新显示 THINKING。
            HideThinking();

            if (!success)
                DialogueBubble.Instance?.Hide();
        }

        // ---------------- 朗读流水线 ----------------

        private IEnumerator SpeakRoutine(
            List<string> sentences,
            int gen,
            bool ownsBubble)
        {
            if (sentences.Count == 0)
            {
                FinishSpeech(gen, ownsBubble);
                yield break;
            }

            Debug.Log(
                "[LUNA TTS] speak begin: sentences=" + sentences.Count +
                " gen=" + gen
            );

            ClipJob current = null;
            bool waiting = true;

            // 缓存命中时 AcquireClip 会同步回调，因此不会闪现 THINKING。
            StartCoroutine(
                AcquireClip(
                    sentences[0],
                    gen,
                    job =>
                    {
                        current = job;
                        waiting = false;
                    }
                )
            );

            if (waiting)
                ShowThinking();

            while (waiting && gen == generation)
                yield return null;

            if (gen != generation)
                yield break;

            if (current == null || current.Failed)
            {
                yield return FallbackTextRoutine(
                    sentences,
                    0,
                    gen,
                    ownsBubble
                );

                yield break;
            }

            for (int i = 0; i < sentences.Count; i++)
            {
                ClipJob next = null;

                // requested 表示「已经发起预取」，ready 表示「预取已完成」。
                // 两者必须分开：缓存命中时回调是同步的，如果用同一个标志，
                // 会在播完第 1 句后把「已完成」误判成「没有下一句」而提前退出。
                bool nextRequested = false;
                bool nextReady = false;

                if (i + 1 < sentences.Count)
                {
                    nextRequested = true;

                    StartCoroutine(
                        AcquireClip(
                            sentences[i + 1],
                            gen,
                            job =>
                            {
                                next = job;
                                nextReady = true;
                            }
                        )
                    );
                }

                ShowSentence(sentences[i]);

                Debug.Log(
                    "[LUNA TTS] sentence " + (i + 1) + "/" +
                    sentences.Count + ": \"" + Shorten(sentences[i]) + "\""
                );

                yield return PlayClip(current, gen);

                if (gen != generation)
                    yield break;

                if (current.Failed)
                {
                    Debug.LogWarning(
                        "[LUNA TTS] Audio load failed, switching to " +
                        "text only: " + current.Error
                    );

                    yield return FallbackTextRoutine(
                        sentences,
                        i,
                        gen,
                        ownsBubble
                    );

                    yield break;
                }

                if (!nextRequested)
                    break;

                while (!nextReady && gen == generation)
                    yield return null;

                if (gen != generation)
                    yield break;

                current = next;

                if (current == null || current.Failed)
                {
                    Debug.LogWarning(
                        "[LUNA TTS] Speech synthesis failed, switching " +
                        "to text only: " +
                        (current != null ? current.Error : "no result")
                    );

                    yield return FallbackTextRoutine(
                        sentences,
                        i + 1,
                        gen,
                        ownsBubble
                    );

                    yield break;
                }

                if (sentenceGapSeconds > 0f)
                {
                    yield return new WaitForSecondsRealtime(
                        sentenceGapSeconds
                    );
                }
            }

            FinishSpeech(gen, ownsBubble);
        }

        private IEnumerator FallbackTextRoutine(
            List<string> sentences,
            int startIndex,
            int gen,
            bool ownsBubble)
        {
            for (int i = startIndex; i < sentences.Count; i++)
            {
                if (gen != generation)
                    yield break;

                ShowSentence(sentences[i]);

                float dwell = Mathf.Clamp(
                    CountWords(sentences[i]) * 0.32f + 0.9f,
                    1.6f,
                    7f
                );

                yield return new WaitForSecondsRealtime(dwell);
            }

            FinishSpeech(gen, ownsBubble);
        }

        private IEnumerator AcquireClip(
            string text,
            int gen,
            Action<ClipJob> onDone,
            bool bypassCache = false)
        {
            var job = new ClipJob { Text = text };
            string key = null;

            if (cache != null && !bypassCache)
            {
                key = cache.BuildKey(
                    text,
                    provider.Voice,
                    provider.Model,
                    provider.Name
                );

                job.Key = key;

                if (cache.TryGet(
                        key,
                        out string cachedPath,
                        out TTSAudioFormat cachedFormat))
                {
                    job.Path = cachedPath;
                    job.Format = cachedFormat;
                    job.FromCache = true;

                    Debug.Log(
                        "[LUNA TTS] cache hit: \"" + Shorten(text) + "\" " +
                        "format=" + cachedFormat +
                        " bytes=" + FileSize(cachedPath) +
                        " head=" + HeadOfFile(cachedPath) +
                        " read[" + TTSCache.FingerprintFile(cachedPath) + "]" +
                        " path=" + cachedPath
                    );

                    onDone?.Invoke(job);
                    yield break;
                }

                Debug.Log(
                    "[LUNA TTS] cache miss, synthesizing: \"" +
                    Shorten(text) + "\"" +
                    (string.IsNullOrEmpty(cache.LastRejectReason)
                        ? string.Empty
                        : " (dropped a bad entry: " +
                          cache.LastRejectReason + ")")
                );
            }

            TTSResult result = null;
            bool finished = false;

            StartCoroutine(
                SynthesizeWithTimeout(
                    text,
                    r =>
                    {
                        result = r;
                        finished = true;
                    }
                )
            );

            while (!finished && gen == generation)
                yield return null;

            if (gen != generation)
                yield break;

            if (result == null || !result.Success)
            {
                job.Failed = true;
                job.Error = result != null
                    ? result.Error
                    : "Speech synthesis produced no result.";

                onDone?.Invoke(job);
                yield break;
            }

            // 以真实文件头为准：某些网关会忽略请求里的 format，
            // 或直接返回错误页。先把非音频内容挡在缓存之外。
            if (!TTSCache.TryDetectFormat(
                    result.AudioData,
                    out TTSAudioFormat detectedFormat))
            {
                job.Failed = true;
                job.Error =
                    "The TTS provider returned " +
                    (result.AudioData != null
                        ? result.AudioData.Length
                        : 0) +
                    " bytes that are not MP3/WAV audio.";

                Debug.LogWarning(
                    "[LUNA TTS] rejected synthesized audio (not audio): " +
                    job.Error + " head=" + HeadHex(result.AudioData)
                );

                onDone?.Invoke(job);
                yield break;
            }

            job.Format = detectedFormat;

            // 溯源点 1：服务端返回的原始字节。
            Debug.Log(
                "[LUNA TTS] bytes(synth): \"" + Shorten(text) + "\" " +
                "format=" + detectedFormat + " " +
                TTSCache.Fingerprint(result.AudioData)
            );

            if (cache != null && !bypassCache)
            {
                try
                {
                    job.Path = cache.Store(
                        key,
                        result.AudioData,
                        job.Format
                    );

                    if (!string.IsNullOrEmpty(job.Path))
                    {
                        // 溯源点 2：真正落盘的字节（Store 回读校验时记录）。
                        Debug.Log(
                            "[LUNA TTS] bytes(store): \"" + Shorten(text) + "\" " +
                            cache.LastStoreTrace
                        );

                        // 合成缓冲与落盘字节不一致 = 写入端改写了音频
                        // （转码 / 重编码 / 缓冲用错），这类条目即使长度正常
                        // 也会在下次命中时变成无声或低振幅。
                        if (!cache.LastStoreMatched)
                        {
                            Debug.LogWarning(
                                "[LUNA TTS] transcoded on write: \"" +
                                Shorten(text) + "\" " +
                                cache.LastStoreTrace
                            );
                        }

                        Debug.Log(
                            "[LUNA TTS] cached: \"" + Shorten(text) + "\" " +
                            "format=" + job.Format +
                            " bytes=" + result.AudioData.Length +
                            " head=" + HeadOfFile(job.Path)
                        );

                        // 同一进程内双路解码对照：原始字节 vs 缓存文件。
                        if (audioDiagnostics)
                            StartDecodeProbe(result.AudioData, job);
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[LUNA TTS] cache write returned no path: \"" +
                            Shorten(text) + "\" bytes=" +
                            (result.AudioData != null
                                ? result.AudioData.Length
                                : 0) +
                            (string.IsNullOrEmpty(cache.LastStoreError)
                                ? string.Empty
                                : " reason=" + cache.LastStoreError) +
                            (string.IsNullOrEmpty(cache.LastStoreTrace)
                                ? string.Empty
                                : " trace=" + cache.LastStoreTrace) +
                            " (playing from a temp file instead)"
                        );
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        "[LUNA TTS] Cache write failed: " + e.Message
                    );
                }
            }

            if (string.IsNullOrEmpty(job.Path))
            {
                try
                {
                    job.Path = WriteTempClip(
                        result.AudioData,
                        result.Format
                    );

                    job.IsTemp = true;
                }
                catch (Exception e)
                {
                    job.Failed = true;
                    job.Error =
                        "Failed to write the audio file: " + e.Message;
                }
            }

            onDone?.Invoke(job);
        }

        private IEnumerator SynthesizeWithTimeout(
            string text,
            Action<TTSResult> onComplete)
        {
            TTSResult outcome = null;
            bool finished = false;

            try
            {
                StartCoroutine(
                    provider.Synthesize(
                        text,
                        r =>
                        {
                            if (finished)
                                return;

                            outcome = r;
                            finished = true;
                        }
                    )
                );
            }
            catch (Exception e)
            {
                outcome = TTSResult.Fail(
                    "The TTS provider threw: " + e.Message
                );

                finished = true;
            }

            float deadline =
                Time.realtimeSinceStartup +
                Mathf.Max(1f, synthesisTimeoutSeconds);

            while (!finished && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!finished)
            {
                outcome = TTSResult.Fail(
                    $"Speech synthesis timed out after " +
                    $"{synthesisTimeoutSeconds:0}s."
                );
            }

            onComplete?.Invoke(
                outcome ?? TTSResult.Fail("No synthesis result.")
            );
        }

        private IEnumerator PlayClip(ClipJob job, int gen)
        {
            if (job == null ||
                job.Failed ||
                string.IsNullOrEmpty(job.Path))
            {
                if (job != null)
                {
                    job.Failed = true;

                    if (string.IsNullOrEmpty(job.Error))
                        job.Error = "No audio file.";
                }

                yield break;
            }

            AudioType audioType = job.Format == TTSAudioFormat.Mp3
                ? AudioType.MPEG
                : AudioType.WAV;

            string origin = job.FromCache ? "cache" : "file";
            string uri = new Uri(job.Path).AbsoluteUri;
            AudioClip clip = null;

            using (UnityWebRequest request =
                   UnityWebRequestMultimedia.GetAudioClip(
                       uri,
                       audioType))
            {
                yield return request.SendWebRequest();

                if (request.result !=
                    UnityWebRequest.Result.Success)
                {
                    job.Failed = true;
                    job.Error =
                        "Failed to load the synthesized audio (" +
                        origin + "): " + request.error;

                    Debug.LogWarning(
                        "[LUNA TTS] decode failed (load error, " +
                        origin + "): " + job.Error +
                        " path=" + job.Path
                    );
                }
                else
                {
                    try
                    {
                        DownloadHandlerAudioClip download =
                            request.downloadHandler as
                                DownloadHandlerAudioClip;

                        if (download != null && download.audioClip == null)
                        {
                            Debug.LogWarning(
                                "[LUNA TTS] decoder returned no AudioClip (" +
                                origin + "): path=" + job.Path
                            );
                        }

                        clip = DownloadHandlerAudioClip.GetContent(
                            request
                        );
                    }
                    catch (Exception e)
                    {
                        job.Error =
                            "The audio decoder threw (" + origin + "): " +
                            e.Message;
                    }
                }
            }

            if (gen != generation)
            {
                if (clip != null)
                    Destroy(clip);

                yield break;
            }

            // 少数平台上 GetContent 拿到的 clip 还处于 Unloaded 状态，
            // 此时读采样会失败或读到未填充的缓冲，先显式加载一次。
            if (clip != null &&
                clip.loadState == AudioDataLoadState.Unloaded)
            {
                if (!clip.LoadAudioData())
                {
                    Debug.LogWarning(
                        "[LUNA TTS] LoadAudioData failed (" + origin +
                        "): path=" + job.Path
                    );
                }
                else
                {
                    while (clip.loadState == AudioDataLoadState.Loading &&
                           gen == generation)
                    {
                        yield return null;
                    }
                }
            }

            // 个别平台上 GetContent 返回的 clip 会晚一帧才填好采样数据，
            // 先等一帧再判定，避免把正常音频误判成空音频。
            if (clip != null && (clip.samples <= 0 || clip.frequency <= 0))
            {
                yield return null;

                if (gen != generation)
                {
                    Destroy(clip);
                    yield break;
                }
            }

            bool empty = clip == null ||
                         clip.samples <= 0 ||
                         clip.frequency <= 0 ||
                         clip.length <= 0.001f;

            // 解码体检：能读采样就量化整段峰值/RMS，用来区分
            // 「clip 本身数据异常」和「播放链路被衰减/静音」。
            // 刚合成的文件（origin=file）与缓存文件（origin=cache）都走这一步，
            // 两条支路的统计会分别打印，方便对比。
            ClipHealth health = empty
                ? new ClipHealth()
                : InspectClip(clip);

            // 缓存命中时振幅被压掉一个数量级（peak 只有 0.004 这种）
            // 属于解码异常：听起来"有声但几乎听不见"，必须作废重建。
            bool abnormal = !empty &&
                            health.Measured &&
                            health.Peak < AbnormalClipPeak;

            if (empty || abnormal)
            {
                // 这里是最容易被忽略的失败：解码「成功」但拿到 0 长度或
                // 振幅异常低的音频，Play() 之后 isPlaying 正常走完，看起来像
                // 播完了，实际上几乎听不见。必须显式判失败并丢掉坏缓存。
                job.Failed = true;

                string problem = empty
                    ? "zero length"
                    : "abnormal amplitude";

                if (string.IsNullOrEmpty(job.Error))
                {
                    job.Error =
                        "The audio decoded to " +
                        (empty ? "zero length" : "abnormally low amplitude") +
                        " (" + origin + ").";
                }

                Debug.LogWarning(
                    "[LUNA TTS] decode failed (" + problem + ", " + origin +
                    "): samples=" + (clip != null ? clip.samples : -1) +
                    " frequency=" +
                    (clip != null ? clip.frequency : -1) +
                    " channels=" +
                    (clip != null ? clip.channels : -1) +
                    " length=" +
                    (clip != null ? clip.length.ToString("0.00") : "-1") +
                    "s peak=" +
                    (health.Measured ? health.Peak.ToString("0.0000") : "n/a") +
                    " rms=" +
                    (health.Measured ? health.Rms.ToString("0.0000") : "n/a") +
                    " threshold=" + AbnormalClipPeak.ToString("0.000") +
                    " bytes=" + FileSize(job.Path) +
                    " head=" + HeadOfFile(job.Path) +
                    " path=" + job.Path
                );

                if (clip != null)
                    Destroy(clip);

                InvalidateCacheEntry(job);

                if (job.IsTemp)
                    TryDeleteFile(job.Path);

                // 坏缓存已经被删掉，立刻用临时文件重建一次：
                // 走 bypassCache 是为了避开"缓存路径解码异常"这条链路，
                // 保证这一句一定能正常出声。
                // 只重建一次：重建出来的结果再异常就直接放弃，避免死循环。
                if (!job.Rebuilt)
                {
                    ClipJob rebuilt = null;
                    bool rebuildDone = false;

                    StartCoroutine(
                        AcquireClip(
                            job.Text,
                            gen,
                            j =>
                            {
                                rebuilt = j;
                                rebuildDone = true;
                            },
                            true
                        )
                    );

                    while (!rebuildDone && gen == generation)
                        yield return null;

                    if (gen == generation &&
                        rebuilt != null &&
                        !rebuilt.Failed)
                    {
                        Debug.Log(
                            "[LUNA TTS] rebuilt clip after a decode " +
                            "failure: \"" + Shorten(job.Text) + "\""
                        );

                        // 标记只重建一次：重建结果再异常也不再递归。
                        rebuilt.Rebuilt = true;

                        yield return PlayClip(rebuilt, gen);

                        job.Failed = rebuilt.Failed;
                        job.Error = rebuilt.Error;
                        yield break;
                    }
                }

                yield break;
            }

            AudioSource source = ResolveSource();

            if (source == null)
            {
                job.Failed = true;
                job.Error = "No AudioSource is available.";
                Destroy(clip);
                yield break;
            }

            AudioListener listener = FindActiveListener();

            float distance;
            float gain = EstimateGain(
                source,
                audioRoute,
                listener,
                out distance
            );

            string peakText = health.Measured
                ? health.Peak.ToString("0.0000")
                : "n/a";
            string rmsText = health.Measured
                ? health.Rms.ToString("0.0000")
                : "n/a";
            string gainText = gain < 0f
                ? "custom"
                : gain.ToString("0.00");

            // 3D 声源离耳朵太远时（编辑器里很常见：监听器在角色头上，
            // 电视在十米开外），对数衰减会把音量压到几乎听不见。
            // 这种情况本句改用 2D 播放，靠近电视时仍保持 3D 定位。
            bool fallback = false;
            AudioSource playback = source;

            if (audioRoute == AudioRoute.TvPosition &&
                autoFallbackToQuiet3D &&
                gain >= 0f &&
                gain < minAudibleGain)
            {
                fallback = true;
                playback = ResolveNearEarSource(source);

                Debug.LogWarning(
                    "[LUNA TTS] 3D source is too quiet at " +
                    distance.ToString("0.0") + "m (estimated gain " +
                    gainText + " < " + minAudibleGain.ToString("0.00") +
                    "); playing this clip through '" + playback.name +
                    "' in 2D instead."
                );
            }

            Debug.Log(
                "[LUNA TTS] play start: \"" + Shorten(job.Text) + "\" " +
                "source=" + source.name +
                (fallback ? " playback=" + playback.name + " (2D)" : "") +
                " route=" + audioRoute +
                " origin=" + origin +
                " length=" + clip.length.ToString("0.00") + "s" +
                " samples=" + clip.samples +
                " channels=" + clip.channels +
                " freq=" + clip.frequency +
                " peak=" + peakText +
                " rms=" + rmsText
            );

            // 每段朗读只打印一次完整链路：声源参数 + 监听器 + 距离衰减。
            // 它和上面的 clip 统计对照，就能判断问题出在数据还是链路。
            if (audioDiagnostics && !audioChainLogged)
            {
                audioChainLogged = true;

                Debug.Log(
                    "[LUNA TTS] audio chain: listener=" +
                    (listener != null ? listener.name : "MISSING") +
                    " activeListeners=" + CountActiveListeners() +
                    " listenerVolume=" +
                    AudioListener.volume.ToString("0.00") +
                    " distance=" + distance.ToString("0.0") + "m" +
                    " gain=" + gainText +
                    " | source=" + source.name +
                    " vol=" + source.volume.ToString("0.00") +
                    " mute=" + source.mute +
                    " spatial=" + source.spatialBlend.ToString("0.00") +
                    " min/max=" + source.minDistance.ToString("0.0") +
                    "/" + source.maxDistance.ToString("0.0") +
                    " rolloff=" + source.rolloffMode +
                    " mixer=" + MixerName(source) +
                    " clipReadable=" + (health.Measured ? "yes" : "no") +
                    " | output=" + AudioSettings.outputSampleRate + "Hz"
                );
            }

            if (listener == null)
            {
                Debug.LogWarning(
                    "[LUNA TTS] no active AudioListener in the scene; " +
                    "3D audio cannot be heard."
                );
            }

            // 只有「重建之后仍然异常」才会走到这里，照常播完但把结论写清楚。
            if (health.Measured && health.Peak < AbnormalClipPeak)
            {
                Debug.LogWarning(
                    "[LUNA TTS] decoded clip still has abnormally low " +
                    "amplitude after a rebuild: peak=" +
                    peakText + " rms=" + rmsText +
                    " origin=" + origin +
                    " path=" + job.Path +
                    " -> the audio data itself is too quiet."
                );
            }

            float previousSpatialBlend = source.spatialBlend;

            playback.mute = false;
            playback.volume = Mathf.Clamp01(volume);
            playback.spatialBlend = fallback
                ? 0f
                : (audioRoute == AudioRoute.TvPosition ? 1f : 0f);
            playback.clip = clip;

            playback.Play();

            // 音频开始 -> 收起 THINKING，气泡里保留当前句。
            HideThinking();

            float timeout = clip.length + 3f;
            float elapsed = 0f;
            float nextProgressLog = 0.25f;
            int progressLogs = 0;

            while (playback.isPlaying &&
                   elapsed < timeout &&
                   gen == generation)
            {
                elapsed += Time.unscaledDeltaTime;

                // 限频：每句最多两行，观察时间是否真的在走、音量是否被改。
                if (audioDiagnostics &&
                    progressLogs < 2 &&
                    elapsed >= nextProgressLog)
                {
                    progressLogs++;
                    nextProgressLog += Mathf.Max(
                        0.5f,
                        clip.length / 3f
                    );

                    Debug.Log(
                        "[LUNA TTS] playing: t=" +
                        playback.time.ToString("0.00") + "s/" +
                        clip.length.ToString("0.00") + "s" +
                        " isPlaying=" + playback.isPlaying +
                        " vol=" + playback.volume.ToString("0.00") +
                        " listenerVolume=" +
                        AudioListener.volume.ToString("0.00")
                    );
                }

                yield return null;
            }

            if (gen != generation)
            {
                Debug.Log(
                    "[LUNA TTS] play aborted (generation changed): \"" +
                    Shorten(job.Text) + "\" elapsed=" +
                    elapsed.ToString("0.00") + "s"
                );

                // 新的朗读可能已经接管了这个 AudioSource，不能 Stop()，
                // 否则会把刚响起的新音频掐掉。只清理仍归本段所有的 clip。
                if (playback.clip == clip)
                {
                    playback.Stop();
                    playback.clip = null;
                }

                if (fallback && playback == source)
                    source.spatialBlend = previousSpatialBlend;

                Destroy(clip);
                yield break;
            }

            if (playback.isPlaying && elapsed >= timeout)
            {
                Debug.LogWarning(
                    "[LUNA TTS] play timeout: \"" + Shorten(job.Text) +
                    "\" expected=" + clip.length.ToString("0.00") +
                    "s elapsed=" + elapsed.ToString("0.00") + "s"
                );
            }
            else
            {
                Debug.Log(
                    "[LUNA TTS] play end: \"" + Shorten(job.Text) +
                    "\" elapsed=" + elapsed.ToString("0.00") + "s" +
                    " peak=" + peakText +
                    " gain=" + gainText +
                    " origin=" + origin +
                    (fallback ? " playback=2D" : "")
                );
            }

            playback.Stop();
            playback.clip = null;

            if (fallback && playback == source)
                source.spatialBlend = previousSpatialBlend;

            Destroy(clip);

            if (job.IsTemp)
                TryDeleteFile(job.Path);
        }

        /// <summary>
        /// 解码失败时丢掉这条缓存，下一次朗读会重新合成。
        /// 玩家不需要手动删 persistentDataPath 下的文件。
        /// </summary>
        private void InvalidateCacheEntry(ClipJob job)
        {
            if (cache == null || string.IsNullOrEmpty(job.Key))
                return;

            cache.Invalidate(job.Key);

            Debug.LogWarning(
                "[LUNA TTS] dropped bad cache entry, it will be " +
                "re-synthesized: \"" + Shorten(job.Text) + "\""
            );
        }

        private AudioSource ResolveSource()
        {
            AudioSource source = audioRoute == AudioRoute.TvPosition
                ? tvSource
                : nearEarSource;

            if (source == null)
            {
                source = audioRoute == AudioRoute.TvPosition
                    ? nearEarSource
                    : tvSource;
            }

            if (source == null)
            {
                // 兜底：场景里没绑声源时，至少保证能出声（2D）。
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;

                if (tvSource == null)
                    tvSource = source;
                else
                    nearEarSource = source;
            }

            return source;
        }

        // ---------------- 音频诊断 ----------------

        /// <summary>一次解码的结果，用于双路对照。</summary>
        private sealed class DecodeStats
        {
            public bool Ok;
            public int Samples;
            public int Channels;
            public int Frequency;
            public float Length;
            public ClipHealth Health;
        }

        /// <summary>
        /// 同一段音频两条路径的解码对照（每次运行只做一次）：
        /// 原始字节写成的临时文件 vs 缓存文件。若两者字节相同却解出
        /// 不同振幅，问题就在解码路径而不是写入路径。
        /// </summary>
        private void StartDecodeProbe(byte[] data, ClipJob job)
        {
            if (decodeProbeDone ||
                data == null ||
                job == null ||
                string.IsNullOrEmpty(job.Path))
            {
                return;
            }

            string rawPath;

            try
            {
                rawPath = WriteTempClip(data, job.Format);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[LUNA TTS] decode probe skipped: " + e.Message
                );

                return;
            }

            decodeProbeDone = true;

            StartCoroutine(
                DualDecodeProbe(job.Text, rawPath, job.Path, job.Format)
            );
        }

        private IEnumerator DualDecodeProbe(
            string text,
            string rawPath,
            string cachePath,
            TTSAudioFormat format)
        {
            DecodeStats raw = null;
            DecodeStats cached = null;

            yield return DecodeStatsRoutine(
                rawPath,
                format,
                s => raw = s
            );

            yield return DecodeStatsRoutine(
                cachePath,
                format,
                s => cached = s
            );

            // 先算指纹再删临时文件，日志里两路的字节指纹才能对照。
            string rawTrace = TTSCache.FingerprintFile(rawPath);
            string fileTrace = TTSCache.FingerprintFile(cachePath);

            TryDeleteFile(rawPath);

            Debug.Log(
                "[LUNA TTS] decode probe (raw bytes): \"" +
                Shorten(text) + "\" " + DescribeDecode(raw) +
                " raw[" + rawTrace + "]"
            );

            Debug.Log(
                "[LUNA TTS] decode probe (cache file): \"" +
                Shorten(text) + "\" " + DescribeDecode(cached) +
                " file[" + fileTrace + "]"
            );

            if (raw == null || cached == null)
                yield break;

            if (!raw.Ok || !cached.Ok)
            {
                Debug.LogWarning(
                    "[LUNA TTS] decode probe: one of the two paths decoded " +
                    "to nothing (raw=" + raw.Ok + " cache=" + cached.Ok + ")."
                );

                yield break;
            }

            if (raw.Samples != cached.Samples ||
                raw.Channels != cached.Channels ||
                raw.Frequency != cached.Frequency)
            {
                Debug.LogWarning(
                    "[LUNA TTS] decode probe: shape differs between raw " +
                    "bytes and cache file (samples " + raw.Samples + " vs " +
                    cached.Samples + ", channels " + raw.Channels + " vs " +
                    cached.Channels + ", freq " + raw.Frequency + " vs " +
                    cached.Frequency + ")."
                );

                yield break;
            }

            if (!raw.Health.Measured || !cached.Health.Measured)
                yield break;

            float loudest = Mathf.Max(raw.Health.Peak, cached.Health.Peak);

            if (loudest > 1e-6f &&
                Mathf.Min(raw.Health.Peak, cached.Health.Peak) / loudest < 0.5f)
            {
                Debug.LogWarning(
                    "[LUNA TTS] decode probe: amplitude differs by more than " +
                    "6 dB between raw bytes and cache file despite identical " +
                    "bytes; the decoder treats the two paths differently."
                );
            }
            else
            {
                Debug.Log(
                    "[LUNA TTS] decode probe: both paths agree " +
                    "(peak " + raw.Health.Peak.ToString("0.0000") + " / " +
                    cached.Health.Peak.ToString("0.0000") + ")."
                );
            }
        }

        private IEnumerator DecodeStatsRoutine(
            string path,
            TTSAudioFormat format,
            Action<DecodeStats> onDone)
        {
            var stats = new DecodeStats();

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                AudioType audioType = format == TTSAudioFormat.Mp3
                    ? AudioType.MPEG
                    : AudioType.WAV;

                AudioClip clip = null;

                using (UnityWebRequest request =
                       UnityWebRequestMultimedia.GetAudioClip(
                           new Uri(path).AbsoluteUri,
                           audioType))
                {
                    yield return request.SendWebRequest();

                    if (request.result ==
                        UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            clip = DownloadHandlerAudioClip.GetContent(
                                request
                            );
                        }
                        catch (Exception)
                        {
                            clip = null;
                        }
                    }
                }

                if (clip != null)
                {
                    if (clip.loadState == AudioDataLoadState.Unloaded)
                    {
                        clip.LoadAudioData();

                        while (clip.loadState ==
                               AudioDataLoadState.Loading)
                        {
                            yield return null;
                        }
                    }

                    stats.Samples = clip.samples;
                    stats.Channels = clip.channels;
                    stats.Frequency = clip.frequency;
                    stats.Length = clip.length;
                    stats.Ok = clip.samples > 0 && clip.frequency > 0;
                    stats.Health = stats.Ok
                        ? InspectClip(clip)
                        : new ClipHealth();

                    Destroy(clip);
                }
            }

            onDone?.Invoke(stats);
        }

        private static string DescribeDecode(DecodeStats stats)
        {
            if (stats == null)
                return "samples=- freq=- channels=- length=-s peak=n/a rms=n/a";

            return "samples=" + stats.Samples +
                   " freq=" + stats.Frequency +
                   " channels=" + stats.Channels +
                   " length=" + stats.Length.ToString("0.00") + "s" +
                   " peak=" + (stats.Health.Measured
                       ? stats.Health.Peak.ToString("0.0000")
                       : "n/a") +
                   " rms=" + (stats.Health.Measured
                       ? stats.Health.Rms.ToString("0.0000")
                       : "n/a");
        }

        /// <summary>解码后 clip 的采样统计。</summary>
        private struct ClipHealth
        {
            /// <summary>GetData 成功，Peak/Rms 可信。</summary>
            public bool Measured;

            public float Peak;
            public float Rms;
        }

        /// <summary>
        /// 顺序扫描整段 clip 的采样，得到精确的峰值与 RMS。
        /// 分块读取，长音频也不会一次分配大数组；
        /// 压缩在内存里读不到采样的 clip 会返回 Measured = false。
        /// </summary>
        private static ClipHealth InspectClip(AudioClip clip)
        {
            var health = new ClipHealth();

            if (clip == null || clip.samples <= 0 || clip.channels <= 0)
                return health;

            int channels = Mathf.Max(1, clip.channels);

            // 顺序整段扫描：只抽几段窗口会漏掉语音之间的停顿，
            // 把正常音频误判成"低振幅"，所以这里逐块读完（每块 8192 帧）。
            const int chunkFrames = 8192;

            double sum = 0.0;
            long counted = 0L;
            int offset = 0;

            while (offset < clip.samples)
            {
                int frames = Mathf.Min(chunkFrames, clip.samples - offset);
                float[] data = new float[frames * channels];

                if (!clip.GetData(data, offset))
                    return new ClipHealth();

                for (int i = 0; i < data.Length; i++)
                {
                    float value = Mathf.Abs(data[i]);

                    if (value > health.Peak)
                        health.Peak = value;

                    sum += (double)data[i] * data[i];
                }

                counted += data.Length;
                offset += frames;
            }

            health.Measured = true;
            health.Rms = counted > 0
                ? (float)System.Math.Sqrt(sum / counted)
                : 0f;

            return health;
        }

        /// <summary>当前真正生效的监听器（主相机优先）。</summary>
        private static AudioListener FindActiveListener()
        {
            Camera main = Camera.main;

            if (main != null)
            {
                AudioListener onMain = main.GetComponent<AudioListener>();

                if (onMain != null && onMain.isActiveAndEnabled)
                    return onMain;
            }

            AudioListener[] listeners = FindObjectsByType<AudioListener>(
                FindObjectsSortMode.None
            );

            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].isActiveAndEnabled)
                    return listeners[i];
            }

            return null;
        }

        /// <summary>场景里同时启用了几个监听器（多监听器会让 3D 音频行为异常）。</summary>
        private static int CountActiveListeners()
        {
            AudioListener[] listeners = FindObjectsByType<AudioListener>(
                FindObjectsSortMode.None
            );

            int count = 0;

            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].isActiveAndEnabled)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// 估算 3D 声源在当前距离上的音量系数。
        /// 返回负数表示自定义衰减曲线，无法估算。
        /// </summary>
        private static float EstimateGain(
            AudioSource source,
            AudioRoute route,
            AudioListener listener,
            out float distance)
        {
            distance = 0f;

            if (source == null || route != AudioRoute.TvPosition)
                return 1f;

            if (listener == null)
                return 0f;

            distance = Vector3.Distance(
                source.transform.position,
                listener.transform.position
            );

            float min = Mathf.Max(0.01f, source.minDistance);
            float max = Mathf.Max(min + 0.01f, source.maxDistance);

            if (source.rolloffMode == AudioRolloffMode.Linear)
            {
                return Mathf.Clamp01(
                    1f - (distance - min) / (max - min)
                );
            }

            if (source.rolloffMode == AudioRolloffMode.Logarithmic)
            {
                return Mathf.Clamp01(
                    min / Mathf.Clamp(distance, min, max)
                );
            }

            return -1f;
        }

        /// <summary>
        /// 3D 太弱时用的 2D 兜底：优先贴耳声源，
        /// 没有就临时把原声源降成 2D（播完恢复）。
        /// </summary>
        private AudioSource ResolveNearEarSource(AudioSource source)
        {
            if (nearEarSource != null &&
                nearEarSource != source &&
                nearEarSource.isActiveAndEnabled &&
                nearEarSource.gameObject.activeInHierarchy)
            {
                return nearEarSource;
            }

            return source;
        }

        private static string MixerName(AudioSource source)
        {
            if (source == null || source.outputAudioMixerGroup == null)
                return "none";

            return source.outputAudioMixerGroup.audioMixer.name + "/" +
                   source.outputAudioMixerGroup.name;
        }

        /// <summary>读磁盘文件的前 8 个字节，用来比对两条支路拿到的内容。</summary>
        private static string HeadOfFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return "(missing)";

                byte[] head = new byte[8];

                using (FileStream stream = File.OpenRead(path))
                {
                    int read = stream.Read(head, 0, head.Length);

                    return HeadHex(head) + "/" + read;
                }
            }
            catch (Exception)
            {
                return "(unreadable)";
            }
        }

        private void FinishSpeech(int gen, bool ownsBubble)
        {
            if (gen != generation)
                return;

            speechActive = false;
            HideThinking();

            if (ownsBubble)
                EndPendingBubbleSpeech();

            StartCoroutine(HideBubbleLater(gen));
            speechRoutine = null;
        }

        private IEnumerator HideBubbleLater(int gen)
        {
            if (holdAfterSpeechSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(
                    holdAfterSpeechSeconds
                );
            }

            if (gen != generation || thinkingVisible)
                yield break;

            DialogueBubble.Instance?.Hide();
        }

        private void StopSpeechRoutine()
        {
            EndPendingBubbleSpeech();

            if (speechRoutine != null)
            {
                StopCoroutine(speechRoutine);
                speechRoutine = null;
            }

            generation++;
            speechActive = false;
            audioChainLogged = false;

            StopPlayback();
            HideThinking();
        }

        private void EndPendingBubbleSpeech()
        {
            if (!bubbleSpeechPending)
                return;

            bubbleSpeechPending = false;
            DialogueBubble.Instance?.NotifySpeechFinished();
        }

        private void StopPlayback()
        {
            if (tvSource != null && tvSource.isPlaying)
                tvSource.Stop();

            if (nearEarSource != null && nearEarSource.isPlaying)
                nearEarSource.Stop();
        }

        private void ShowSentence(string sentence)
        {
            HideThinking();
            DialogueBubble.Instance?.ShowSticky(sentence);
        }

        private void HideThinking()
        {
            thinkingVisible = false;

            if (thinkingRoutine != null)
            {
                StopCoroutine(thinkingRoutine);
                thinkingRoutine = null;
            }
        }

        private IEnumerator ThinkingDotsRoutine()
        {
            string baseText = (thinkingText ?? string.Empty)
                .TrimEnd('.', ' ');

            if (string.IsNullOrEmpty(baseText))
                baseText = "THINKING";

            int dots = 0;

            while (thinkingVisible)
            {
                yield return new WaitForSecondsRealtime(
                    thinkingDotInterval
                );

                if (!thinkingVisible)
                    break;

                dots = dots % 3 + 1;

                DialogueBubble.Instance?.ShowSticky(
                    baseText + new string('.', dots)
                );
            }

            thinkingRoutine = null;
        }

        // ---------------- 工具 ----------------

        private static string WriteTempClip(
            byte[] data,
            TTSAudioFormat format)
        {
            string directory = Path.Combine(
                Application.temporaryCachePath,
                "luna-tts"
            );

            Directory.CreateDirectory(directory);

            string path = Path.Combine(
                directory,
                "clip-" + DateTime.UtcNow.Ticks +
                (format == TTSAudioFormat.Mp3 ? ".mp3" : ".wav")
            );

            File.WriteAllBytes(path, data);
            return path;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
                // 临时文件删不掉不影响播放。
            }
        }

        /// <summary>只打印前 8 个字节，用于判断返回内容是什么格式。</summary>
        private static string HeadHex(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "(empty)";

            int count = Mathf.Min(8, data.Length);
            var builder = new System.Text.StringBuilder(count * 3);

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    builder.Append(' ');

                builder.Append(data[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private static long FileSize(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && File.Exists(path)
                    ? new FileInfo(path).Length
                    : -1L;
            }
            catch (Exception)
            {
                return -1L;
            }
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

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            int count = 0;
            bool insideWord = false;

            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (!insideWord)
                    {
                        count++;
                        insideWord = true;
                    }
                }
                else
                {
                    insideWord = false;
                }
            }

            return count;
        }
    }
}
