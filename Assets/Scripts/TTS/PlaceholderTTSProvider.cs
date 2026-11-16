using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace Game.TTS
{
    /// <summary>
    /// 不依赖网络的占位实现：把文本转成一串带停顿的 "读音响"。
    /// 用途是在云端 Key 到位前把「合成 → 落盘缓存 → 播放」整条链路跑通，
    /// 生成的音频同样进缓存，因此重复句子也能验证缓存命中。
    /// </summary>
    public sealed class PlaceholderTTSProvider : ITTSProvider
    {
        private const int SampleRate = 22050;
        private const int SamplesPerFrame = 16384;
        private const int HeaderBytes = 44;

        private readonly float startDelaySeconds;

        public PlaceholderTTSProvider(float startDelaySeconds = 0.12f)
        {
            this.startDelaySeconds = Mathf.Max(0f, startDelaySeconds);
        }

        public string Name => "placeholder";
        public string Model => "placeholder-tone-v1";
        public string Voice => "placeholder-calm-male";
        public bool IsAvailable => true;

        public IEnumerator Synthesize(
            string text,
            Action<TTSResult> onComplete)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onComplete?.Invoke(
                    TTSResult.Fail("Cannot synthesize empty text.")
                );

                yield break;
            }

            if (startDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(startDelaySeconds);

            int wordCount = CountWords(text);

            // 每个词一个音节，语速刻意压慢，接近冷静克制的男声节奏。
            float wordDuration = 0.26f;
            float gap = 0.055f;
            float seconds = Mathf.Clamp(
                0.18f + wordCount * (wordDuration + gap),
                0.35f,
                16f
            );

            int totalSamples = Mathf.Max(
                1,
                Mathf.RoundToInt(seconds * SampleRate)
            );

            int dataBytes = totalSamples * 2;
            var buffer = new byte[HeaderBytes + dataBytes];
            WriteHeader(buffer, dataBytes);

            for (int i = 0; i < totalSamples; i++)
            {
                float t = i / (float)SampleRate;
                float sample = 0f;

                int word = Mathf.FloorToInt(
                    (t - 0.06f) / (wordDuration + gap)
                );

                if (word >= 0 && word < wordCount)
                {
                    float wordStart =
                        0.06f + word * (wordDuration + gap);
                    float local = t - wordStart;

                    if (local >= 0f && local <= wordDuration)
                    {
                        float attack = Mathf.Clamp01(local / 0.035f);
                        float release = Mathf.Clamp01(
                            (wordDuration - local) / 0.07f
                        );
                        float envelope = attack * release;

                        // 男声基频 110–140Hz 附近，加少量谐波。
                        float frequency = 116f + (word % 5) * 5f;
                        float phase = 2f * Mathf.PI * frequency * local;

                        sample = 0.20f * envelope *
                            (Mathf.Sin(phase) +
                             0.28f * Mathf.Sin(phase * 2.01f) +
                             0.10f * Mathf.Sin(phase * 3.02f));
                    }
                }

                short value = (short)(
                    Mathf.Clamp(sample, -1f, 1f) * short.MaxValue
                );

                buffer[HeaderBytes + i * 2] =
                    (byte)(value & 0xFF);
                buffer[HeaderBytes + i * 2 + 1] =
                    (byte)((value >> 8) & 0xFF);

                if (i % SamplesPerFrame == 0)
                    yield return null;
            }

            onComplete?.Invoke(
                TTSResult.Ok(buffer, TTSAudioFormat.Wav)
            );
        }

        private static int CountWords(string text)
        {
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

            return Mathf.Max(1, count);
        }

        private static void WriteHeader(byte[] buffer, int dataBytes)
        {
            Encoding.ASCII.GetBytes("RIFF").CopyTo(buffer, 0);
            WriteInt(buffer, 4, 36 + dataBytes);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(buffer, 8);
            Encoding.ASCII.GetBytes("fmt ").CopyTo(buffer, 12);
            WriteInt(buffer, 16, 16);
            WriteShort(buffer, 20, 1);
            WriteShort(buffer, 22, 1);
            WriteInt(buffer, 24, SampleRate);
            WriteInt(buffer, 28, SampleRate * 2);
            WriteShort(buffer, 32, 2);
            WriteShort(buffer, 34, 16);
            Encoding.ASCII.GetBytes("data").CopyTo(buffer, 36);
            WriteInt(buffer, 40, dataBytes);
        }

        private static void WriteInt(
            byte[] buffer,
            int offset,
            int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteShort(
            byte[] buffer,
            int offset,
            int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
