using System;

namespace Game.LLM
{
    public enum LLMRole
    {
        System,
        User,
        Assistant
    }

    [Serializable]
    public sealed class LLMMessage
    {
        public LLMRole Role { get; }
        public string Content { get; }

        public LLMMessage(LLMRole role, string content)
        {
            Role = role;
            Content = content ?? string.Empty;
        }
    }
}