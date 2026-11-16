using System;
using System.Collections;

namespace Game.TTS
{
    public enum TTSAudioFormat
    {
        Wav = 0,
        Mp3 = 1
    }

    /// <summary>
    /// 一次语音合成的结果：成功时带回音频字节，失败时带回可读原因。
    /// 缓存命中时由 LunaTTSController 直接复用磁盘文件，不经过这里。
    /// </summary>
    public sealed class TTSResult
    {
        public bool Success { get; private set; }
        public byte[] AudioData { get; private set; }
        public TTSAudioFormat Format { get; private set; }
        public string Error { get; private set; }

        public static TTSResult Ok(
            byte[] audioData,
            TTSAudioFormat format)
        {
            if (audioData == null || audioData.Length == 0)
                return Fail("The TTS provider returned no audio data.");

            return new TTSResult
            {
                Success = true,
                AudioData = audioData,
                Format = format,
                Error = null
            };
        }

        public static TTSResult Fail(string error)
        {
            return new TTSResult
            {
                Success = false,
                AudioData = null,
                Format = TTSAudioFormat.Wav,
                Error = string.IsNullOrWhiteSpace(error)
                    ? "Speech synthesis failed."
                    : error
            };
        }
    }

    /// <summary>
    /// 语音合成厂商抽象：输入英文文本，输出音频字节。
    /// 云厂商、离线占位实现都只实现这个接口，业务层不感知具体厂商。
    /// </summary>
    public interface ITTSProvider
    {
        /// <summary>用于缓存键的厂商标识，例如 "openai-compatible"。</summary>
        string Name { get; }

        /// <summary>用于缓存键的模型标识。</summary>
        string Model { get; }

        /// <summary>用于缓存键的音色标识。</summary>
        string Voice { get; }

        /// <summary>配置是否完整（例如云端实现需要 Key 与 endpoint）。</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 合成一段文本。回调必须恰好调用一次（成功或失败），
        /// 调用方会在外层加超时保护。
        /// </summary>
        IEnumerator Synthesize(
            string text,
            Action<TTSResult> onComplete);
    }

    /// <summary>各家 TTS 请求体共用的 JSON 转义。</summary>
    internal static class TTSJson
    {
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new System.Text.StringBuilder(
                value.Length + 16
            );

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
                        {
                            builder.Append("\\u")
                                .Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
