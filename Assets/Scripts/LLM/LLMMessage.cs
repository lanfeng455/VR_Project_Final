using System;
using System.Collections.Generic;

namespace Game.LLM
{
    public enum LLMRole
    {
        System,
        User,
        Assistant,
        Tool
    }

    [Serializable]
    public sealed class LLMMessage
    {
        public LLMRole Role { get; }
        public string Content { get; }

        /// <summary>assistant 消息里模型发起的工具调用。</summary>
        public IReadOnlyList<LLMToolCall> ToolCalls { get; }

        /// <summary>tool 消息里对应的调用编号。</summary>
        public string ToolCallId { get; }

        public LLMMessage(LLMRole role, string content)
        {
            Role = role;
            Content = content ?? string.Empty;
        }

        public LLMMessage(
            LLMRole role,
            string content,
            IReadOnlyList<LLMToolCall> toolCalls,
            string toolCallId = null)
        {
            Role = role;
            Content = content ?? string.Empty;
            ToolCalls = toolCalls;
            ToolCallId = toolCallId;
        }

        public static LLMMessage AssistantWithTools(
            string content,
            IReadOnlyList<LLMToolCall> toolCalls)
        {
            return new LLMMessage(
                LLMRole.Assistant,
                content,
                toolCalls
            );
        }

        public static LLMMessage ToolResult(
            string toolCallId,
            string content)
        {
            return new LLMMessage(
                LLMRole.Tool,
                content,
                null,
                toolCallId
            );
        }
    }
}
