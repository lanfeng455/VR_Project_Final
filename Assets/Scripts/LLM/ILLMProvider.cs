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

        IEnumerator SendAsync(
            IReadOnlyList<LLMMessage> messages,
            IReadOnlyList<LLMToolSpec> tools,
            Action<LLMResult> onComplete
        );
    }

    public sealed class LLMResult
    {
        public bool Success { get; }
        public string Content { get; }
        public string Error { get; }
        public long StatusCode { get; }

        /// <summary>模型要求调用的工具。为空表示这是一条普通回复。</summary>
        public IReadOnlyList<LLMToolCall> ToolCalls { get; }

        public bool HasToolCalls =>
            ToolCalls != null && ToolCalls.Count > 0;

        private LLMResult(
            bool success,
            string content,
            string error,
            long statusCode,
            IReadOnlyList<LLMToolCall> toolCalls)
        {
            Success = success;
            Content = content;
            Error = error;
            StatusCode = statusCode;
            ToolCalls = toolCalls;
        }

        public static LLMResult Ok(string content, long statusCode = 200)
        {
            return new LLMResult(true, content, null, statusCode, null);
        }

        public static LLMResult WithToolCalls(
            string content,
            IReadOnlyList<LLMToolCall> toolCalls,
            long statusCode = 200)
        {
            return new LLMResult(
                true,
                content,
                null,
                statusCode,
                toolCalls
            );
        }

        public static LLMResult Fail(string error, long statusCode = 0)
        {
            return new LLMResult(false, null, error, statusCode, null);
        }
    }
}
