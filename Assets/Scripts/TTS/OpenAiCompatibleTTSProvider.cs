using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.TTS
{
    /// <summary>
    /// OpenAI 兼容的云 TTS 实现（/audio/speech）。
    /// 只依赖 endpoint + apiKey + model + voice，换厂商时改配置即可，
    /// 不需要改动 LunaTTSController。
    /// </summary>
    public sealed class OpenAiCompatibleTTSProvider : ITTSProvider
    {
        private readonly string endpoint;
        private readonly string apiKey;
        private readonly string model;
        private readonly string voice;
        private readonly TTSAudioFormat format;
        private readonly float speed;
        private readonly float requestTimeoutSeconds;

        public OpenAiCompatibleTTSProvider(
            string endpoint,
            string apiKey,
            string model,
            string voice,
            TTSAudioFormat format,
            float speed,
            float requestTimeoutSeconds = 30f)
        {
            this.endpoint = endpoint;
            this.apiKey = apiKey;
            this.model = model;
            this.voice = voice;
            this.format = format;
            this.speed = speed;
            this.requestTimeoutSeconds = requestTimeoutSeconds;
        }

        public string Name => "openai-compatible";
        public string Model => model;
        public string Voice => voice;

        public bool IsAvailable =>
            !string.IsNullOrWhiteSpace(endpoint) &&
            !string.IsNullOrWhiteSpace(apiKey);

        public IEnumerator Synthesize(
            string text,
            Action<TTSResult> onComplete)
        {
            if (!IsAvailable)
            {
                onComplete?.Invoke(
                    TTSResult.Fail(
                        "The cloud TTS endpoint or API key is missing."
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
                request.SetRequestHeader(
                    "Authorization",
                    "Bearer " + apiKey
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
                            $"HTTP {request.responseCode}: " +
                            request.error
                        )
                    );

                    yield break;
                }

                byte[] data = request.downloadHandler.data;

                if (data == null || data.Length == 0)
                {
                    onComplete?.Invoke(
                        TTSResult.Fail(
                            "The TTS endpoint returned an empty body."
                        )
                    );

                    yield break;
                }

                string contentType =
                    request.GetResponseHeader("Content-Type") ?? string.Empty;

                if (contentType.IndexOf(
                        "application/json",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    onComplete?.Invoke(
                        TTSResult.Fail(
                            "The endpoint returned JSON instead of audio. " +
                            "Check the endpoint path and response_format."
                        )
                    );

                    yield break;
                }

                onComplete?.Invoke(TTSResult.Ok(data, format));
            }
        }

        private string BuildBody(string text)
        {
            var builder = new StringBuilder(256);

            builder.Append('{');
            builder.Append("\"model\":\"").Append(Escape(model)).Append("\",");
            builder.Append("\"input\":\"").Append(Escape(text)).Append("\",");
            builder.Append("\"voice\":\"").Append(Escape(voice)).Append("\",");
            builder.Append("\"response_format\":\"")
                .Append(format == TTSAudioFormat.Mp3 ? "mp3" : "wav")
                .Append('"');

            if (speed > 0f && Mathf.Abs(speed - 1f) > 0.001f)
            {
                builder.Append(",\"speed\":")
                    .Append(speed.ToString("0.##"));
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length + 16);

            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                            builder.Append("\\u")
                                .Append(((int)c).ToString("x4"));
                        else
                            builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
