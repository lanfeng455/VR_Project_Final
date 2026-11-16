using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.TTS
{
    /// <summary>
    /// 火山引擎 / 豆包语音合成 2.0 适配层。
    ///
    /// 选型说明：
    /// - 使用 HTTP 单向流式接口（/api/v3/tts/unidirectional），而不是 WebSocket。
    ///   一次 HTTPS POST 即可完成，没有 WebSocket 握手、分帧和心跳，Quest 上直连即可。
    /// - 响应是 chunked JSON（每块 base64 音频），这里一次收完再拼接成完整文件，
    ///   因为上层已经按句切块，单句很短，真正的边收边播收益有限。
    /// - 认证优先用新版控制台的单一 API Key（请求头 X-Api-Key）；
    ///   旧版控制台的 AppID + Access Token（X-Api-App-Id / X-Api-Access-Key）仍兼容。
    ///   两种方式都必须带 X-Api-Resource-Id，2.0 取 seed-tts-2.0。
    /// - 语气指令按 2.0 文档放在 req_params.additions 里，
    ///   而 additions 本身是转义后的 JSON 字符串，不是嵌套对象。
    /// </summary>
    public sealed class VolcengineTTSProvider : ITTSProvider
    {
        private const string DefaultEndpoint =
            "https://openspeech.bytedance.com/api/v3/tts/unidirectional";

        private const string DefaultResourceId = "seed-tts-2.0";

        private readonly string endpoint;
        private readonly string appId;
        private readonly string accessToken;
        private readonly string apiKey;
        private readonly string resourceId;
        private readonly string speaker;
        private readonly string model;
        private readonly string instruction;
        private readonly TTSAudioFormat format;
        private readonly int sampleRate;
        private readonly float speed;
        private readonly float requestTimeoutSeconds;
        private readonly string cacheVariant;

        [Serializable]
        private sealed class ResponseChunk
        {
            public int code;
            public string message;
            public string data;
        }

        public VolcengineTTSProvider(
            string endpoint,
            string appId,
            string accessToken,
            string apiKey,
            string resourceId,
            string speaker,
            string model,
            string instruction,
            TTSAudioFormat format,
            int sampleRate,
            float speed,
            float requestTimeoutSeconds = 40f)
        {
            this.endpoint = string.IsNullOrWhiteSpace(endpoint)
                ? DefaultEndpoint
                : endpoint.Trim();

            this.appId = (appId ?? string.Empty).Trim();
            this.accessToken = (accessToken ?? string.Empty).Trim();
            this.apiKey = (apiKey ?? string.Empty).Trim();

            this.resourceId = string.IsNullOrWhiteSpace(resourceId)
                ? DefaultResourceId
                : resourceId.Trim();

            this.speaker = (speaker ?? string.Empty).Trim();
            this.model = (model ?? string.Empty).Trim();
            this.instruction = instruction ?? string.Empty;

            // 单向流式接口按段返回音频；请求 wav 时每一段都自带 WAV 头，
            // 拼接后 Unity 往往只能解出第一段（听一句就静音）。
            // 因此这里统一用 mp3，配置里写 wav 时给出警告。
            this.format = TTSAudioFormat.Mp3;

            if (format != TTSAudioFormat.Mp3)
            {
                Debug.LogWarning(
                    "[LUNA TTS] Volcengine streaming TTS returns multiple " +
                    "WAV headers that cannot be concatenated; forcing " +
                    "mp3 instead. Set \"responseFormat\": \"mp3\" in " +
                    "tts-config.json."
                );
            }
            this.sampleRate = sampleRate > 0 ? sampleRate : 24000;
            this.speed = speed <= 0f ? 1f : speed;
            this.requestTimeoutSeconds = requestTimeoutSeconds;

            // 语气指令会改变音频，必须参与缓存键；否则玩家改指令后会命中旧音频。
            cacheVariant =
                (string.IsNullOrEmpty(this.model)
                    ? this.resourceId
                    : this.model) +
                "-" + StableHash(this.instruction).ToString("x8");
        }

        public string Name => "volcengine";
        public string Model => cacheVariant;
        public string Voice => speaker;

        public bool IsAvailable =>
            !string.IsNullOrWhiteSpace(endpoint) &&
            !string.IsNullOrWhiteSpace(speaker) &&
            ((!string.IsNullOrEmpty(appId) &&
              !string.IsNullOrEmpty(accessToken)) ||
             !string.IsNullOrEmpty(apiKey));

        public IEnumerator Synthesize(
            string text,
            Action<TTSResult> onComplete)
        {
            if (!IsAvailable)
            {
                onComplete?.Invoke(
                    TTSResult.Fail(
                        "Volcengine TTS is missing AppID/Access Token, " +
                        "voice ID, or endpoint."
                    )
                );

                yield break;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                onComplete?.Invoke(
                    TTSResult.Fail("Cannot synthesize empty text.")
                );

                yield break;
            }

            string body = BuildBody(text);

            using (var request = new UnityWebRequest(
                       endpoint,
                       UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(
                    Encoding.UTF8.GetBytes(body)
                );

                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader(
                    "Content-Type",
                    "application/json"
                );

                if (!string.IsNullOrEmpty(apiKey))
                {
                    // 新版控制台：单一 API Key。
                    request.SetRequestHeader("X-Api-Key", apiKey);
                }
                else
                {
                    request.SetRequestHeader("X-Api-App-Id", appId);
                    request.SetRequestHeader(
                        "X-Api-Access-Key",
                        accessToken
                    );
                }

                request.SetRequestHeader(
                    "X-Api-Resource-Id",
                    resourceId
                );
                request.SetRequestHeader(
                    "X-Api-Request-Id",
                    Guid.NewGuid().ToString()
                );

                request.timeout = Mathf.CeilToInt(
                    Mathf.Max(1f, requestTimeoutSeconds)
                );

                yield return request.SendWebRequest();

                if (request.result !=
                    UnityWebRequest.Result.Success)
                {
                    onComplete?.Invoke(
                        TTSResult.Fail(
                            DescribeFailure(request)
                        )
                    );

                    yield break;
                }

                byte[] raw = request.downloadHandler.data;

                if (raw == null || raw.Length == 0)
                {
                    onComplete?.Invoke(
                        TTSResult.Fail(
                            "Volcengine returned an empty response."
                        )
                    );

                    yield break;
                }

                // 个别网关会直接回音频字节；JSON 才需要拆 chunk。
                if (!LooksLikeJson(raw))
                {
                    onComplete?.Invoke(
                        TTSResult.Ok(raw, format)
                    );

                    yield break;
                }

                byte[] audio = ParseChunks(
                    Encoding.UTF8.GetString(raw),
                    out string error
                );

                if (audio == null || audio.Length == 0)
                {
                    onComplete?.Invoke(
                        TTSResult.Fail(
                            (string.IsNullOrEmpty(error)
                                ? "Volcengine returned no audio data."
                                : error) +
                            " | voice=" + speaker
                        )
                    );

                    yield break;
                }

                onComplete?.Invoke(TTSResult.Ok(audio, format));
            }
        }

        private string BuildBody(string text)
        {
            var builder = new StringBuilder(512);

            builder.Append("{\"req_params\":{");
            builder.Append("\"text\":\"")
                .Append(TTSJson.Escape(text))
                .Append("\",");
            builder.Append("\"speaker\":\"")
                .Append(TTSJson.Escape(speaker))
                .Append("\",");

            if (!string.IsNullOrEmpty(model))
            {
                builder.Append("\"model\":\"")
                    .Append(TTSJson.Escape(model))
                    .Append("\",");
            }

            builder.Append("\"audio_params\":{");
            builder.Append("\"format\":\"")
                .Append(format == TTSAudioFormat.Mp3 ? "mp3" : "wav")
                .Append("\",");
            builder.Append("\"sample_rate\":")
                .Append(sampleRate);

            int speechRate = Mathf.RoundToInt((speed - 1f) * 100f);
            speechRate = Mathf.Clamp(speechRate, -50, 100);

            if (speechRate != 0)
            {
                builder.Append(",\"speech_rate\":")
                    .Append(speechRate);
            }

            builder.Append('}');

            if (!string.IsNullOrWhiteSpace(instruction))
            {
                // additions 是「转义后的 JSON 字符串」，不是嵌套对象；
                // context_texts 是 2.0 的自然语言语气指令。
                string additions =
                    "{\"context_texts\":[\"" +
                    TTSJson.Escape(instruction) +
                    "\"]}";

                builder.Append(",\"additions\":\"")
                    .Append(TTSJson.Escape(additions))
                    .Append('"');
            }

            builder.Append("}}");
            return builder.ToString();
        }

        /// <summary>
        /// 拼出方便自查的失败信息：HTTP 码、响应体片段、X-Tt-Logid。
        /// 火山把「音色未授权 / 音色 ID 写错」放在响应体里返回。
        /// </summary>
        private string DescribeFailure(UnityWebRequest request)
        {
            var builder = new StringBuilder(256);

            builder.Append("HTTP ")
                .Append(request.responseCode)
                .Append(": ")
                .Append(
                    string.IsNullOrEmpty(request.error)
                        ? "request failed"
                        : request.error
                );

            string body = request.downloadHandler != null
                ? request.downloadHandler.text
                : null;

            if (!string.IsNullOrWhiteSpace(body))
            {
                body = body.Trim();

                builder.Append(" | ")
                    .Append(
                        body.Length > 240
                            ? body.Substring(0, 240) + "..."
                            : body
                    );
            }

            string logId = request.GetResponseHeader("X-Tt-Logid");

            if (!string.IsNullOrWhiteSpace(logId))
                builder.Append(" | logid=").Append(logId);

            builder.Append(" | voice=").Append(speaker);
            return builder.ToString();
        }

        /// <summary>
        /// 把多段 JSON（chunked 响应）里的 base64 音频拼起来。
        /// 顶层对象逐个提取，避免依赖换行分隔。
        /// </summary>
        private static byte[] ParseChunks(
            string body,
            out string error)
        {
            error = null;

            using (var output = new MemoryStream())
            {
                int index = 0;

                while (index < body.Length)
                {
                    int start = body.IndexOf('{', index);

                    if (start < 0)
                        break;

                    int end = FindObjectEnd(body, start);

                    if (end < 0)
                        break;

                    string json = body.Substring(
                        start,
                        end - start + 1
                    );

                    index = end + 1;

                    ResponseChunk chunk;

                    try
                    {
                        chunk = JsonUtility.FromJson<ResponseChunk>(
                            json
                        );
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (chunk == null)
                        continue;

                    // 20000000 是「音频合成正常结束」标记，不是错误码。
                    if (chunk.code == 20000000)
                        continue;

                    if (!string.IsNullOrEmpty(chunk.data))
                    {
                        try
                        {
                            byte[] bytes = Convert.FromBase64String(
                                chunk.data
                            );

                            output.Write(bytes, 0, bytes.Length);
                            continue;
                        }
                        catch (FormatException)
                        {
                            error =
                                "Volcengine returned invalid base64 audio.";
                        }
                    }

                    if (chunk.code != 0 && error == null)
                    {
                        error =
                            $"Volcengine error {chunk.code}: " +
                            (string.IsNullOrEmpty(chunk.message)
                                ? "no message"
                                : chunk.message);
                    }
                }

                return output.Length > 0
                    ? output.ToArray()
                    : null;
            }
        }

        private static int FindObjectEnd(string text, int start)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];

                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (c == '\\')
                        escaped = true;
                    else if (c == '"')
                        inString = false;

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;

                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static bool LooksLikeJson(byte[] data)
        {
            foreach (byte b in data)
            {
                if (b == (byte)' ' ||
                    b == (byte)'\r' ||
                    b == (byte)'\n' ||
                    b == (byte)'\t')
                {
                    continue;
                }

                return b == (byte)'{';
            }

            return false;
        }

        private static uint StableHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0u;

            uint hash = 2166136261u;

            foreach (char c in value.Trim().ToLowerInvariant())
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return hash;
        }
    }
}
