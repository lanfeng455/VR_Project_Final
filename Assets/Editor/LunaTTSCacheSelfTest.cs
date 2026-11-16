using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.TTS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.EditorTools
{
    /// <summary>
    /// 「缓存到底有没有坏」的一键自检：合成 → 落盘 → 冷读 → 双路解码，
    /// 全部在一个独立目录里完成，不依赖 Play 模式，也不碰玩家真正的缓存目录。
    /// 会真实调用一次云端 TTS，所以需要 Resources/TTS/tts-config.json。
    /// </summary>
    public static class LunaTTSCacheSelfTest
    {
        private const string MenuRoot = "Tools/VR Project/LUNA/";
        private const string ConfigResourcePath = "TTS/tts-config";
        private const string CacheFolderName = "tts-cache-selftest";

        private const string TestText =
            "The hallway clock stopped at three seventeen in the morning.";

        private const long CacheMaxBytes = 8L * 1024 * 1024;

        /// <summary>低于这个峰值视为「解码异常」，与运行时判定保持一致。</summary>
        private const float MinPeak = 0.02f;

        /// <summary>两路峰值的最大允许差距（约 6 dB）。</summary>
        private const float PeakRatioLimit = 0.5f;

        private static CoroutinePump pump;

        [MenuItem(MenuRoot + "Verify TTS Cache (Cold Read)", false, 20)]
        public static void VerifyTtsCache()
        {
            if (pump != null)
            {
                Debug.LogWarning(
                    "[LUNA TTS SELF-TEST] already running, wait for it to finish."
                );
                return;
            }

            if (!TTSConfig.TryLoad(
                    ConfigResourcePath,
                    out TTSConfig.Data data,
                    out string error))
            {
                Report(
                    "FAIL",
                    "STEP 0 没有可用的 TTS 配置：" + error +
                    "。请先创建 Assets/Resources/" + ConfigResourcePath +
                    ".json（可参考 tts-config.example.json）。"
                );
                return;
            }

            if (!IsCloudReady(data))
            {
                Report(
                    "FAIL",
                    "STEP 0 配置不完整（provider=" + data.provider +
                    "）：云端实现需要 endpoint/apiKey，" +
                    "火山引擎需要 appId+accessToken 或 apiKey，并且要有 voice。"
                );
                return;
            }

            ITTSProvider provider = CreateCloudProvider(data);

            if (!provider.IsAvailable)
            {
                Report(
                    "FAIL",
                    "STEP 0 provider " + provider.Name + " 报告不可用。"
                );
                return;
            }

            string directory = Path.Combine(
                Application.persistentDataPath,
                CacheFolderName
            );

            pump = new CoroutinePump(
                Run(provider, directory),
                delegate { pump = null; }
            );
        }

        // ---------------- 主流程 ----------------

        private static IEnumerator Run(
            ITTSProvider provider,
            string directory)
        {
            Debug.Log(
                "[LUNA TTS SELF-TEST] ===== start ===== provider=" +
                provider.Name + " voice=" + provider.Voice +
                " model=" + provider.Model + " dir=" + directory
            );

            // 每次都从空目录开始，保证后面的读取一定是「冷读」。
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);

                Directory.CreateDirectory(directory);
            }
            catch (Exception e)
            {
                Report("FAIL", "STEP 0 无法准备测试目录：" + e.Message);
                yield break;
            }

            // ---- STEP 1：合成 ----
            TTSResult result = null;
            bool callbackDone = false;

            yield return provider.Synthesize(TestText, r =>
            {
                result = r;
                callbackDone = true;
            });

            if (!callbackDone || result == null)
            {
                Report("FAIL", "STEP 1 合成没有回调（provider 没有返回结果）。");
                yield break;
            }

            if (!result.Success ||
                result.AudioData == null ||
                result.AudioData.Length == 0)
            {
                Report(
                    "FAIL",
                    "STEP 1 合成失败：" +
                    (string.IsNullOrEmpty(result.Error)
                        ? "返回了空音频"
                        : result.Error)
                );
                yield break;
            }

            TTSAudioFormat detected;

            if (!TTSCache.TryDetectFormat(result.AudioData, out detected))
                detected = result.Format;

            string synthTrace = TTSCache.Fingerprint(result.AudioData);

            Debug.Log(
                "[LUNA TTS SELF-TEST] synth: declared=" + result.Format +
                " sniffed=" + detected + " bytes[" + synthTrace + "] text=\"" +
                TestText + "\""
            );

            // ---- STEP 2：Store 落盘 ----
            TTSCache writer = new TTSCache(directory, CacheMaxBytes);
            string key = writer.BuildKey(
                TestText,
                provider.Voice,
                provider.Model,
                provider.Name
            );

            string storedPath;

            try
            {
                storedPath = writer.Store(key, result.AudioData, detected);
            }
            catch (Exception e)
            {
                Report("FAIL", "STEP 2 Store 抛异常：" + e.Message);
                yield break;
            }

            Debug.Log(
                "[LUNA TTS SELF-TEST] store: " +
                (string.IsNullOrEmpty(writer.LastStoreTrace)
                    ? "(no trace)"
                    : writer.LastStoreTrace) +
                " matched=" + writer.LastStoreMatched +
                " path=" + (storedPath ?? "(null)")
            );

            if (string.IsNullOrEmpty(storedPath))
            {
                Report(
                    "FAIL",
                    "STEP 2 写入不一致：Store 写盘后回读的字节与合成缓冲区不同" +
                    "（" + (writer.LastStoreError ?? "unknown") + "）。" +
                    "缓存条目已被丢弃，说明写入端仍有问题。"
                );
                yield break;
            }

            // ---- STEP 3：冷读（新建实例，只从磁盘索引）----
            TTSCache reader = new TTSCache(directory, CacheMaxBytes);
            string coldPath;
            TTSAudioFormat coldFormat;

            if (!reader.TryGet(key, out coldPath, out coldFormat))
            {
                Report(
                    "FAIL",
                    "STEP 3 冷读失败：Store 之后重新加载索引就找不到或拒收了该条目" +
                    "（" + (reader.LastRejectReason ?? "entry not found") + "）。"
                );
                yield break;
            }

            string coldTrace = TTSCache.FingerprintFile(coldPath);

            Debug.Log(
                "[LUNA TTS SELF-TEST] cold read: format=" + coldFormat +
                " path=" + coldPath + " bytes[" + coldTrace + "]"
            );

            bool bytesMatch = string.Equals(
                synthTrace,
                coldTrace,
                StringComparison.Ordinal
            );

            // ---- STEP 4：双路解码 ----
            string rawDirectory = Path.Combine(directory, "decode-raw");
            Directory.CreateDirectory(rawDirectory);

            string rawPath = Path.Combine(
                rawDirectory,
                "raw" + Extension(detected)
            );

            File.WriteAllBytes(rawPath, result.AudioData);

            DecodeStats raw = null;
            DecodeStats cold = null;

            yield return DecodeRoutine(
                rawPath,
                detected,
                stats => raw = stats
            );

            yield return DecodeRoutine(
                coldPath,
                coldFormat,
                stats => cold = stats
            );

            Debug.Log("[LUNA TTS SELF-TEST] decode synth-bytes: " + Describe(raw));
            Debug.Log("[LUNA TTS SELF-TEST] decode cache-file : " + Describe(cold));

            try
            {
                Directory.Delete(rawDirectory, true);
            }
            catch (Exception)
            {
                // 临时文件删不掉不影响结论。
            }

            // ---- STEP 5：结论 ----
            bool bothDecoded = raw != null && cold != null &&
                               raw.Measured && cold.Measured;

            float rawPeak = raw != null ? raw.Peak : 0f;
            float coldPeak = cold != null ? cold.Peak : 0f;
            float minPeak = Mathf.Min(rawPeak, coldPeak);
            float maxPeak = Mathf.Max(rawPeak, coldPeak);

            bool sameOrder = bothDecoded && maxPeak > 0f &&
                             minPeak / maxPeak >= PeakRatioLimit;

            bool pass = bytesMatch && bothDecoded &&
                        minPeak > MinPeak && sameOrder;

            if (pass)
            {
                Report(
                    "PASS",
                    "SHA 一致，" + Describe(raw) + " / " + Describe(cold)
                );
                yield break;
            }

            var reasons = new List<string>();

            if (!bytesMatch)
            {
                reasons.Add(
                    "写入不一致：合成字节与磁盘文件不同（" +
                    synthTrace + " vs " + coldTrace +
                    "）→ 写入端仍在转码或截断，缓存条目需要重新生成"
                );
            }

            if (!bothDecoded)
            {
                reasons.Add(
                    "需要重新合成的解码异常：字节" +
                    (bytesMatch ? "一致" : "也不一致") +
                    "，但其中一路解不出音频（synth: " +
                    Describe(raw) + "；cache: " + Describe(cold) + "）"
                );
            }
            else if (minPeak <= MinPeak)
            {
                reasons.Add(
                    "需要重新合成的解码异常：字节一致但峰值 " +
                    minPeak.ToString("0.000") +
                    " 低于 " + MinPeak.ToString("0.00") + "，几乎无声"
                );
            }
            else if (!sameOrder)
            {
                reasons.Add(
                    "两路解码幅度不同量级：peak " +
                    rawPeak.ToString("0.000") + " vs " +
                    coldPeak.ToString("0.000") +
                    "（相差超过 6 dB），缓存文件需要重新合成"
                );
            }

            Report("FAIL", string.Join("；", reasons.ToArray()));
        }

        // ---------------- 解码 ----------------

        private sealed class DecodeStats
        {
            public int Samples;
            public int Frequency;
            public int Channels;
            public float Length;
            public float Peak;
            public float Rms;
            public bool Measured;
            public string Error;
        }

        private static string Describe(DecodeStats stats)
        {
            if (stats == null)
                return "samples=n/a (no result)";

            if (!stats.Measured)
            {
                return "samples=" + stats.Samples +
                       " freq=" + stats.Frequency +
                       " channels=" + stats.Channels +
                       " length=" + stats.Length.ToString("0.000") +
                       "s peak=0.000 rms=0.000 FAILED(" +
                       (stats.Error ?? "unmeasured") + ")";
            }

            return "samples=" + stats.Samples +
                   " freq=" + stats.Frequency +
                   " channels=" + stats.Channels +
                   " length=" + stats.Length.ToString("0.000") +
                   "s peak=" + stats.Peak.ToString("0.000") +
                   " rms=" + stats.Rms.ToString("0.000");
        }

        private static IEnumerator DecodeRoutine(
            string path,
            TTSAudioFormat format,
            Action<DecodeStats> onDone)
        {
            string uri;

            try
            {
                uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
            }
            catch (Exception e)
            {
                onDone(new DecodeStats { Error = e.Message });
                yield break;
            }

            AudioType audioType = format == TTSAudioFormat.Mp3
                ? AudioType.MPEG
                : AudioType.WAV;

            using (UnityWebRequest request =
                   UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onDone(new DecodeStats { Error = request.error });
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);

                if (clip == null)
                {
                    onDone(new DecodeStats { Error = "clip is null" });
                    yield break;
                }

                var stats = new DecodeStats
                {
                    Samples = clip.samples,
                    Frequency = clip.frequency,
                    Channels = clip.channels,
                    Length = clip.length
                };

                Measure(clip, stats);

                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(clip);
                else
                    UnityEngine.Object.DestroyImmediate(clip);

                onDone(stats);
            }
        }

        private static void Measure(AudioClip clip, DecodeStats stats)
        {
            if (clip == null || clip.samples <= 0 || clip.channels <= 0)
            {
                stats.Error = "clip has no samples";
                return;
            }

            int channels = Mathf.Max(1, clip.channels);
            const int chunkFrames = 8192;

            double sum = 0.0;
            long counted = 0L;
            int offset = 0;
            float peak = 0f;

            while (offset < clip.samples)
            {
                int frames = Mathf.Min(chunkFrames, clip.samples - offset);
                float[] data = new float[frames * channels];

                if (!clip.GetData(data, offset))
                {
                    stats.Error = "GetData failed at frame " + offset;
                    return;
                }

                for (int i = 0; i < data.Length; i++)
                {
                    float value = Mathf.Abs(data[i]);

                    if (value > peak)
                        peak = value;

                    sum += (double)data[i] * data[i];
                }

                counted += data.Length;
                offset += frames;
            }

            stats.Peak = peak;
            stats.Rms = counted > 0
                ? (float)Math.Sqrt(sum / counted)
                : 0f;
            stats.Measured = true;
        }

        private static string Extension(TTSAudioFormat format)
        {
            return format == TTSAudioFormat.Mp3 ? ".mp3" : ".wav";
        }

        // ---------------- 配置 / provider ----------------

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

        private static ITTSProvider CreateCloudProvider(TTSConfig.Data data)
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

        // ---------------- 结论输出 ----------------

        private static void Report(string verdict, string details)
        {
            string line = "[LUNA TTS SELF-TEST] ===== " + verdict + " ===== " +
                          details;

            if (verdict == "PASS")
                Debug.Log(line);
            else
                Debug.LogError(line);

            EditorUtility.DisplayDialog(
                "LUNA TTS Cache",
                verdict + "\n\n" + details +
                "\n\n完整过程见 Console（过滤 [LUNA TTS SELF-TEST]）。",
                "OK"
            );
        }

        // ---------------- 编辑器协程泵 ----------------

        /// <summary>
        /// 编辑器里没有 MonoBehaviour 协程，这里用 EditorApplication.update
        /// 手动推进 provider 的 IEnumerator，并等待 UnityWebRequest 的异步操作。
        /// </summary>
        private sealed class CoroutinePump
        {
            private readonly Stack<IEnumerator> stack =
                new Stack<IEnumerator>();

            private readonly Action onDone;

            private AsyncOperation pending;
            private bool finished;

            public CoroutinePump(IEnumerator root, Action onDone)
            {
                stack.Push(root);
                this.onDone = onDone;
                EditorApplication.update += Tick;
            }

            private void Tick()
            {
                if (finished)
                    return;

                try
                {
                    if (pending != null)
                    {
                        if (!pending.isDone)
                            return;

                        pending = null;
                    }

                    while (stack.Count > 0)
                    {
                        IEnumerator top = stack.Peek();

                        if (!top.MoveNext())
                        {
                            stack.Pop();

                            var disposable = top as IDisposable;

                            if (disposable != null)
                                disposable.Dispose();

                            continue;
                        }

                        object current = top.Current;

                        if (current is IEnumerator nested)
                        {
                            stack.Push(nested);
                            continue;
                        }

                        if (current is AsyncOperation operation)
                        {
                            if (!operation.isDone)
                            {
                                pending = operation;
                                return;
                            }

                            continue;
                        }

                        // null / YieldInstruction：等下一次编辑器刷新。
                        return;
                    }
                }
                catch (Exception e)
                {
                    Report(
                        "FAIL",
                        "自检过程抛出异常：" + e.GetType().Name + " " +
                        e.Message
                    );
                }

                Finish();
            }

            private void Finish()
            {
                if (finished)
                    return;

                finished = true;
                EditorApplication.update -= Tick;

                while (stack.Count > 0)
                {
                    var disposable = stack.Pop() as IDisposable;

                    if (disposable != null)
                        disposable.Dispose();
                }

                if (onDone != null)
                    onDone();
            }
        }
    }
}
