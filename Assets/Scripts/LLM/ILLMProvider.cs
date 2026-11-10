using System;
using System.Collections;
using System.Collections.Generic;

namespace Game.LLM
{
    public interface ILLMProvider
    {
        IEnumerator SendAsync(
            IReadOnlyList<LLMMessage> messages,
            Action<LLMResult> onComplete
        );
    }

    public sealed class LLMResult
    {
        public bool Success { get; }
        public string Content { get; }
        public string Error { get; }
        public long StatusCode { get; }

        private LLMResult(
            bool success,
            string content,
            string error,
            long statusCode)
        {
            Success = success;
            Content = content;
            Error = error;
            StatusCode = statusCode;
        }

        public static LLMResult Ok(string content, long statusCode = 200)
        {
            return new LLMResult(true, content, null, statusCode);
        }

        public static LLMResult Fail(string error, long statusCode = 0)
        {
            return new LLMResult(false, null, error, statusCode);
        }
    }
}