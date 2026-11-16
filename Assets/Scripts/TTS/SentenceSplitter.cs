using System.Collections.Generic;
using System.Text;

namespace Game.TTS
{
    /// <summary>
    /// 把长文本切成适合逐句朗读与逐句显示的短块。
    ///
    /// 策略：
    /// 1. 在 . ! ? … 以及换行处断句；小数点、常见缩写（Mr./Dr./etc.）不切。
    /// 2. 过短的碎片向后合并，避免一句只有 "Yes." 这种碎片单独占一屏。
    /// 3. 超过 maxChars 的句子在最后一个空格处硬切，保证单句能放进气泡。
    /// </summary>
    public static class SentenceSplitter
    {
        public static List<string> Split(
            string text,
            int minChars,
            int maxChars)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return result;

            minChars = minChars < 1 ? 1 : minChars;
            maxChars = maxChars < minChars ? minChars : maxChars;

            List<string> raw = SplitRaw(text);
            var merged = new List<string>(raw.Count);

            foreach (string part in raw)
            {
                string sentence = CollapseWhitespace(part);

                if (sentence.Length == 0)
                    continue;

                if (merged.Count > 0)
                {
                    string last = merged[merged.Count - 1];

                    if (last.Length < minChars &&
                        last.Length + 1 + sentence.Length <= maxChars)
                    {
                        merged[merged.Count - 1] = last + " " + sentence;
                        continue;
                    }
                }

                merged.Add(sentence);
            }

            foreach (string sentence in merged)
            {
                if (sentence.Length <= maxChars)
                {
                    result.Add(sentence);
                    continue;
                }

                HardSplit(sentence, maxChars, result);
            }

            return result;
        }

        private static List<string> SplitRaw(string text)
        {
            var parts = new List<string>();
            var buffer = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                buffer.Append(c);

                bool isNewline = c == '\n';
                bool isTerminator =
                    c == '.' || c == '!' || c == '?' || c == '…';

                if (!isNewline && !isTerminator)
                    continue;

                if (isTerminator && IsAbbreviation(text, i))
                    continue;

                // 连续的 !? … 收进同一句。
                while (i + 1 < text.Length && IsTerminator(text[i + 1]))
                    buffer.Append(text[++i]);

                // 终止符后面必须跟空白或结尾，否则是小数/缩写。
                // 换行则是无条件断句。
                if (!isNewline &&
                    i + 1 < text.Length &&
                    !char.IsWhiteSpace(text[i + 1]))
                {
                    continue;
                }

                parts.Add(buffer.ToString());
                buffer.Clear();

                while (i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
                    i++;
            }

            if (buffer.Length > 0)
                parts.Add(buffer.ToString());

            return parts;
        }

        private static bool IsTerminator(char c)
        {
            return c == '.' || c == '!' || c == '?' || c == '…';
        }

        private static bool IsAbbreviation(string text, int index)
        {
            if (text[index] != '.')
                return false;

            // 数字小数点：3.14
            if (index > 0 &&
                index + 1 < text.Length &&
                char.IsDigit(text[index - 1]) &&
                char.IsDigit(text[index + 1]))
            {
                return true;
            }

            int start = index - 1;

            while (start >= 0 &&
                   (char.IsLetter(text[start]) || text[start] == '.'))
            {
                start--;
            }

            string token = text
                .Substring(start + 1, index - start - 1)
                .ToLowerInvariant();

            if (token.Length <= 1)
                return true;

            switch (token)
            {
                case "mr":
                case "mrs":
                case "ms":
                case "dr":
                case "prof":
                case "st":
                case "vs":
                case "etc":
                case "eg":
                case "ie":
                case "no":
                    return true;
                default:
                    return false;
            }
        }

        private static void HardSplit(
            string sentence,
            int maxChars,
            List<string> result)
        {
            int start = 0;

            while (start < sentence.Length)
            {
                int length = maxChars;

                if (start + length >= sentence.Length)
                {
                    length = sentence.Length - start;
                }
                else
                {
                    int space = sentence.LastIndexOf(
                        ' ',
                        start + length - 1,
                        length
                    );

                    if (space > start + 8)
                        length = space - start;
                }

                string chunk = sentence
                    .Substring(start, length)
                    .Trim();

                if (chunk.Length > 0)
                    result.Add(chunk);

                start += length;

                while (start < sentence.Length &&
                       char.IsWhiteSpace(sentence[start]))
                {
                    start++;
                }
            }
        }

        private static string CollapseWhitespace(string value)
        {
            var builder = new StringBuilder(value.Length);
            bool pendingSpace = false;

            foreach (char c in value)
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

                builder.Append(c);
            }

            return builder.ToString().Trim();
        }
    }
}
