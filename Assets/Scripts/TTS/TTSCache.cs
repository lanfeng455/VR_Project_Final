using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Game.TTS
{
    /// <summary>
    /// 磁盘语音缓存。
    /// 键 = SHA256(归一化文本 | voice | model | provider)，
    /// 文件落在 persistentDataPath 下的缓存目录，跨会话复用。
    /// 容量上限按字节数控制，超出后按最后访问时间淘汰最旧的文件（LRU）。
    /// </summary>
    public sealed class TTSCache
    {
        private const string WavExtension = ".wav";
        private const string Mp3Extension = ".mp3";

        // 归一化时要去掉的结尾标点：YES. / YES! / YES? 命中同一条缓存。
        private static readonly char[] TrailingPunctuation =
        {
            '.', ',', '!', '?', ';', ':', '…',
            '"', '\'', '”', '’', ')', ']'
        };

        private sealed class Entry
        {
            public string Path;
            public long Size;
            public DateTime LastAccessUtc;
        }

        private readonly string directory;
        private readonly long maxBytes;
        private readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        public TTSCache(string directory, long maxBytes)
        {
            this.directory = directory;
            this.maxBytes = Math.Max(1L, maxBytes);

            Directory.CreateDirectory(this.directory);
            LoadIndex();
            EvictIfNeeded();
        }

        public string DirectoryPath => directory;
        public long MaxBytes => maxBytes;
        public long CurrentBytes { get; private set; }
        public int Count => entries.Count;

        /// <summary>启动时因内容无法识别而被删除的坏文件数量。</summary>
        public int DroppedOnLoad { get; private set; }

        /// <summary>最近一次 Store 没写入的原因，供调用方打进日志。</summary>
        public string LastStoreError { get; private set; }

        /// <summary>最近一次 TryGet 命中后被丢弃的原因（空表示没有丢弃）。</summary>
        public string LastRejectReason { get; private set; }

        /// <summary>
        /// 最近一次 Store 的字节溯源：合成缓冲 / 落盘文件 / 回读文件
        /// 三者的长度、SHA256 前 16 位与头 16 字节。供调用方打日志。
        /// </summary>
        public string LastStoreTrace { get; private set; }

        /// <summary>最近一次 Store 落盘内容与合成缓冲是否逐字节一致。</summary>
        public bool LastStoreMatched { get; private set; }

        /// <summary>
        /// 进程内写盘串行化：朗读、预热、预取可能同时给同一句话写缓存，
        /// 多个协程并发写同一个路径会互相截断。
        /// </summary>
        private static readonly object WriteLock = new object();

        /// <summary>小于这个字节数的文件不可能是有效语音。</summary>
        private const int MinAudioBytes = 512;

        /// <summary>
        /// 文本归一化：转小写、合并空白、去掉结尾标点。
        /// 因此大小写、结尾标点、多余空白的差异会命中同一条缓存。
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            bool pendingSpace = false;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(char.ToLowerInvariant(c));
            }

            string normalized = builder.ToString().Trim();
            return normalized.TrimEnd(TrailingPunctuation).TrimEnd();
        }

        public string BuildKey(
            string text,
            string voice,
            string model,
            string provider)
        {
            string payload =
                Normalize(text) + "|" +
                NormalizeToken(voice) + "|" +
                NormalizeToken(model) + "|" +
                NormalizeToken(provider);

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(payload)
                );

                var builder = new StringBuilder(hash.Length * 2);

                foreach (byte b in hash)
                    builder.Append(b.ToString("x2"));

                return builder.ToString();
            }
        }

        public bool TryGet(
            string key,
            out string path,
            out TTSAudioFormat format)
        {
            path = null;
            format = TTSAudioFormat.Wav;
            LastRejectReason = null;

            if (string.IsNullOrEmpty(key) ||
                !entries.TryGetValue(key, out Entry entry))
            {
                return false;
            }

            if (!File.Exists(entry.Path))
            {
                entries.Remove(key);
                CurrentBytes = Math.Max(0L, CurrentBytes - entry.Size);
                return false;
            }

            Touch(entry);

            // 不能只信扩展名：早期写坏的条目可能扩展名与内容不符，
            // 用错 AudioType 解码会静默得到 0 长度的空音频。
            TTSAudioFormat actualFormat;

            if (!TrySniffFormat(entry.Path, out actualFormat))
            {
                // 既不是 RIFF/WAVE 也不是 MPEG 帧同步：当作坏文件丢掉，
                // 让下一次合成重建，玩家不需要手动删目录。
                LastRejectReason = "content is not MP3/WAV";
                DropEntry(key, entry);
                return false;
            }

            // 全零 / 过短的文件解出来必然无声，同样直接淘汰，
            // 旧版本留下的坏条目不需要玩家手动清理。
            string contentProblem;

            if (!HasPlausibleContent(entry.Path, out contentProblem))
            {
                LastRejectReason = contentProblem;
                DropEntry(key, entry);
                return false;
            }

            path = entry.Path;
            format = actualFormat;
            return true;
        }

        private void DropEntry(string key, Entry entry)
        {
            entries.Remove(key);
            CurrentBytes = Math.Max(0L, CurrentBytes - entry.Size);
            TryDelete(entry.Path);
        }

        /// <summary>丢弃单条缓存（解码失败时用于触发重建）。</summary>
        public void Invalidate(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (!entries.TryGetValue(key, out Entry entry))
                return;

            TryDelete(entry.Path);
            CurrentBytes = Math.Max(0L, CurrentBytes - entry.Size);
            entries.Remove(key);
        }

        /// <summary>清空整个缓存目录，返回删除的文件数。</summary>
        public int ClearAll()
        {
            int removed = 0;

            try
            {
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.EnumerateFiles(
                                 directory))
                    {
                        string extension = Path.GetExtension(file);

                        bool ours =
                            string.Equals(
                                extension,
                                WavExtension,
                                StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(
                                extension,
                                Mp3Extension,
                                StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(
                                extension,
                                ".tmp",
                                StringComparison.OrdinalIgnoreCase);

                        if (!ours)
                            continue;

                        try
                        {
                            File.Delete(file);
                            removed++;
                        }
                        catch (Exception)
                        {
                            // 被占用时跳过，下次启动 LoadIndex 会再清。
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 目录不可读时保持索引为空即可。
            }

            entries.Clear();
            CurrentBytes = 0L;
            return removed;
        }

        /// <summary>
        /// 用文件头判断真实格式。
        /// WAV: "RIFF"...."WAVE"；MP3: "ID3" 或 11 位帧同步。
        /// 返回 false 表示内容无法识别（坏文件）。
        /// </summary>
        private static bool TrySniffFormat(
            string path,
            out TTSAudioFormat format)
        {
            format = TTSAudioFormat.Wav;

            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    byte[] head = new byte[16];
                    int read = stream.Read(head, 0, head.Length);

                    return TryDetectFormat(head, read, out format);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 用字节头判断音频格式。合成结果落盘前也用它，
        /// 避免把错误页 / JSON / 空数据当成音频写进缓存。
        /// </summary>
        public static bool TryDetectFormat(
            byte[] data,
            out TTSAudioFormat format)
        {
            return TryDetectFormat(
                data,
                data != null ? data.Length : 0,
                out format
            );
        }

        private static bool TryDetectFormat(
            byte[] data,
            int length,
            out TTSAudioFormat format)
        {
            format = TTSAudioFormat.Wav;

            if (data == null || length < 2)
                return false;

            if (length >= 12 &&
                data[0] == 'R' &&
                data[1] == 'I' &&
                data[2] == 'F' &&
                data[3] == 'F' &&
                data[8] == 'W' &&
                data[9] == 'A' &&
                data[10] == 'V' &&
                data[11] == 'E')
            {
                format = TTSAudioFormat.Wav;
                return true;
            }

            if (length >= 3 &&
                data[0] == 'I' &&
                data[1] == 'D' &&
                data[2] == '3')
            {
                format = TTSAudioFormat.Mp3;
                return true;
            }

            if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
            {
                format = TTSAudioFormat.Mp3;
                return true;
            }

            return false;
        }

        /// <summary>写入音频并返回落盘路径；写入失败会抛出异常由调用方降级。</summary>
        public string Store(
            string key,
            byte[] data,
            TTSAudioFormat format)
        {
            LastStoreError = null;
            LastStoreTrace = null;
            LastStoreMatched = false;

            if (string.IsNullOrEmpty(key) ||
                data == null ||
                data.Length == 0)
            {
                LastStoreError = "empty audio data";
                return null;
            }

            // 写入端的最低有效性校验：过短或全零的字节流解出来必然无声，
            // 先挡在缓存之外，免得下一次朗读又命中一条「能解码但没声音」的条目。
            if (data.Length < MinAudioBytes)
            {
                LastStoreError =
                    "audio data too small (" + data.Length + " bytes)";
                return null;
            }

            if (IsAllZero(data, data.Length))
            {
                LastStoreError =
                    "audio data is all zero bytes (" + data.Length + " bytes)";
                return null;
            }

            string synthTrace = Fingerprint(data);

            Directory.CreateDirectory(directory);

            string path = Path.Combine(
                directory,
                key + Extension(format)
            );

            // 临时文件带随机后缀：同一句话被多处同时合成时，
            // 固定 ".tmp" 会被另一个协程截断，读出来就是「长度正常但内容坏了」。
            string tempPath =
                path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            lock (WriteLock)
            {
                // 同一个键只保留一种格式：上次以 .wav 写入、这次是 .mp3（或反过来）时，
                // 旧文件会在下次启动被 LoadIndex 重新收录，命中后可能拿到旧内容。
                TryDelete(Path.Combine(
                    directory,
                    key + Extension(
                        format == TTSAudioFormat.Mp3
                            ? TTSAudioFormat.Wav
                            : TTSAudioFormat.Mp3
                    )
                ));

                try
                {
                    // 原样落盘：这里不做任何解码 / 重编码 / 归一化，
                    // 合成返回什么字节就写什么字节。
                    File.WriteAllBytes(tempPath, data);
                }
                catch (Exception)
                {
                    TryDelete(tempPath);
                    throw;
                }

                try
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);

                        File.Move(tempPath, path);
                    }
                    catch (Exception)
                    {
                        // 目标被别的进程占用时退化为覆盖复制。
                        File.Copy(tempPath, path, true);
                    }
                }
                finally
                {
                    TryDelete(tempPath);
                }

                // 字节级溯源：把「合成缓冲」和「真正落盘的字节」做哈希比对。
                // 只有这两者一致，缓存命中时才有可能解出同样的音频。
                byte[] written = File.ReadAllBytes(path);

                LastStoreTrace =
                    "synth[" + synthTrace + "] disk[" +
                    Fingerprint(written) + "]";

                if (written.Length != data.Length ||
                    !ContentEquals(written, data))
                {
                    LastStoreError =
                        "cache file mismatch after write (wrote " +
                        data.Length + " bytes, read " + written.Length + ")";

                    // 内容被改写（转码 / 截断 / 缓冲用错）的条目不能留，
                    // 下一次命中它只会得到无声或低振幅的音频。
                    TryDelete(path);
                    return null;
                }

                LastStoreMatched = true;

                if (entries.TryGetValue(key, out Entry old))
                    CurrentBytes = Math.Max(0L, CurrentBytes - old.Size);

                entries[key] = new Entry
                {
                    Path = path,
                    Size = data.Length,
                    LastAccessUtc = DateTime.UtcNow
                };

                CurrentBytes += data.Length;
                EvictIfNeeded();

                return path;
            }
        }

        /// <summary>
        /// 字节指纹：长度 + SHA256 前 16 位 + 头 16 字节 hex。
        /// 同一段音频在任何环节（合成缓冲 / 落盘 / 读回）都必须完全一致。
        /// </summary>
        public static string Fingerprint(byte[] data)
        {
            if (data == null)
                return "len=0 sha=n/a head=(empty)";

            return "len=" + data.Length +
                   " sha=" + Sha16(data) +
                   " head=" + HeadHex(data);
        }

        /// <summary>读磁盘文件计算字节指纹（用于比对读取时拿到的字节）。</summary>
        public static string FingerprintFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return "len=-1 sha=n/a head=(missing)";

                return Fingerprint(File.ReadAllBytes(path));
            }
            catch (Exception e)
            {
                return "len=-1 sha=n/a head=(unreadable: " + e.Message + ")";
            }
        }

        private static string Sha16(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var builder = new StringBuilder(16);

                for (int i = 0; i < 8; i++)
                    builder.Append(hash[i].ToString("x2"));

                return builder.ToString();
            }
        }

        private static string HeadHex(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "(empty)";

            int count = Math.Min(16, data.Length);
            var builder = new StringBuilder(count * 3);

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    builder.Append(' ');

                builder.Append(data[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private static bool IsAllZero(byte[] data, int length)
        {
            for (int i = 0; i < length; i++)
            {
                if (data[i] != 0)
                    return false;
            }

            return true;
        }

        private static bool ContentEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 检查磁盘上的音频是否还像一段真实语音：
        /// 太短或整段全零都视为坏文件。只扫到第一个非零字节就停。
        /// </summary>
        private static bool HasPlausibleContent(
            string path,
            out string reason)
        {
            reason = null;

            try
            {
                long length = new FileInfo(path).Length;

                if (length < MinAudioBytes)
                {
                    reason = "too small (" + length + " bytes)";
                    return false;
                }

                using (FileStream stream = File.OpenRead(path))
                {
                    byte[] buffer = new byte[8192];

                    int read;

                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (!IsAllZero(buffer, read))
                            return true;
                    }
                }

                reason = "all zero bytes (" + length + " bytes)";
                return false;
            }
            catch (Exception e)
            {
                reason = "unreadable: " + e.Message;
                return false;
            }
        }

        private static string Extension(TTSAudioFormat format)
        {
            return format == TTSAudioFormat.Mp3
                ? Mp3Extension
                : WavExtension;
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().ToLowerInvariant();
        }

        private void LoadIndex()
        {
            entries.Clear();
            CurrentBytes = 0L;

            if (!Directory.Exists(directory))
                return;

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string extension = Path.GetExtension(file);

                if (string.Equals(
                        extension,
                        ".tmp",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // 上次写入中断留下的临时文件。
                    TryDelete(file);
                    continue;
                }

                if (!string.Equals(
                        extension,
                        WavExtension,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        extension,
                        Mp3Extension,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string key = Path.GetFileNameWithoutExtension(file);

                if (string.IsNullOrEmpty(key) || key.Length < 16)
                    continue;

                // 启动时就把内容无法识别的坏文件删掉，
                // 玩家不需要手动清理缓存目录。
                if (!TrySniffFormat(file, out _))
                {
                    TryDelete(file);
                    DroppedOnLoad++;
                    continue;
                }

                var info = new FileInfo(file);

                entries[key] = new Entry
                {
                    Path = file,
                    Size = info.Length,
                    LastAccessUtc = info.LastWriteTimeUtc
                };

                CurrentBytes += info.Length;
            }
        }

        private void Touch(Entry entry)
        {
            entry.LastAccessUtc = DateTime.UtcNow;

            // 把访问时间写回磁盘，LRU 顺序就能跨会话保留。
            try
            {
                File.SetLastWriteTimeUtc(
                    entry.Path,
                    entry.LastAccessUtc
                );
            }
            catch (Exception)
            {
                // 只影响 LRU 精度，不影响播放。
            }
        }

        private void EvictIfNeeded()
        {
            if (CurrentBytes <= maxBytes)
                return;

            var ordered = new List<Entry>(entries.Values);
            ordered.Sort(
                (a, b) => a.LastAccessUtc.CompareTo(b.LastAccessUtc)
            );

            for (int i = 0;
                 i < ordered.Count && CurrentBytes > maxBytes;
                 i++)
            {
                Entry entry = ordered[i];
                TryDelete(entry.Path);

                CurrentBytes = Math.Max(0L, CurrentBytes - entry.Size);
                entries.Remove(
                    Path.GetFileNameWithoutExtension(entry.Path)
                );
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
                // 文件被占用时保留到下次启动再清理。
            }
        }
    }
}
