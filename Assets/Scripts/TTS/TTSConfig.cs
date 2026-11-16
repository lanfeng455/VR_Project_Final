using System;
using UnityEngine;

namespace Game.TTS
{
    /// <summary>
    /// 从 Resources/TTS/tts-config.json 读取 TTS 配置。
    /// 真实配置不进版本库；缺文件时返回 false，调用方自动退回占位实现。
    /// </summary>
    public static class TTSConfig
    {
        /// <summary>默认语气指令，玩家可在 tts-config.json 里整段替换。</summary>
        public const string DefaultInstruction =
            "calm and neutral male host, concise and plain, " +
            "flat and unemotional tone, no intonation fluctuation, " +
            "quiet indoor space ambience";

        public const string VolcengineEndpoint =
            "https://openspeech.bytedance.com/api/v3/tts/unidirectional";

        [Serializable]
        public sealed class Data
        {
            // 仅供配置文件里写说明文字，运行时不使用。
            public string _note;

            public string provider;
            public string endpoint;
            public string apiKey;

            // 火山引擎专用：AppID + Access Token + 资源 ID。
            public string appId;
            public string accessToken;
            public string resourceId;

            public string model;
            public string voice;
            public string responseFormat;
            public string instruction;
            public float speed;
        }

        public static bool TryLoad(
            string resourcePath,
            out Data data,
            out string error)
        {
            data = null;
            error = null;

            TextAsset asset = Resources.Load<TextAsset>(resourcePath);

            if (asset == null)
            {
                error =
                    $"TTS config not found: Resources/{resourcePath}.json";
                return false;
            }

            try
            {
                data = JsonUtility.FromJson<Data>(asset.text);
            }
            catch (Exception e)
            {
                error = "Failed to parse the TTS config: " + e.Message;
                return false;
            }

            if (data == null)
            {
                error = "TTS config is empty.";
                return false;
            }

            data.provider = TrimOrDefault(data.provider, "cloud")
                .ToLowerInvariant();

            bool volcengine = data.provider == "volcengine";

            data.endpoint = string.IsNullOrWhiteSpace(data.endpoint)
                ? (volcengine ? VolcengineEndpoint : string.Empty)
                : data.endpoint.Trim();

            data.apiKey = (data.apiKey ?? string.Empty).Trim();
            data.appId = (data.appId ?? string.Empty).Trim();
            data.accessToken =
                (data.accessToken ?? string.Empty).Trim();

            data.resourceId = TrimOrDefault(
                data.resourceId,
                "seed-tts-2.0"
            );

            // 云厂商未填 model/voice 时给各自的方向性默认值。
            data.model = TrimOrDefault(
                data.model,
                volcengine ? string.Empty : "tts-1"
            );

            data.voice = TrimOrDefault(
                data.voice,
                volcengine ? "en_male_ronald_uranus_bigtts" : "onyx"
            );

            data.responseFormat = TrimOrDefault(
                data.responseFormat,
                volcengine ? "mp3" : "wav"
            );

            data.instruction = string.IsNullOrWhiteSpace(data.instruction)
                ? DefaultInstruction
                : data.instruction.Trim();

            if (data.speed <= 0f)
                data.speed = 1f;

            return true;
        }

        public static TTSAudioFormat ParseFormat(string value)
        {
            return string.Equals(
                (value ?? string.Empty).Trim(),
                "mp3",
                StringComparison.OrdinalIgnoreCase)
                ? TTSAudioFormat.Mp3
                : TTSAudioFormat.Wav;
        }

        private static string TrimOrDefault(
            string value,
            string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
        }
    }
}
