using System.Collections.Generic;

namespace Game.LLM
{
    /// <summary>
    /// 模型请求调用某个工具时返回的一项。ArgumentsJson 是模型生成的原始 JSON 字符串，
    /// 必须由工具自己校验，不能直接信任。
    /// </summary>
    public sealed class LLMToolCall
    {
        public string Id { get; }
        public string Name { get; }
        public string ArgumentsJson { get; }

        public LLMToolCall(string id, string name, string argumentsJson)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            ArgumentsJson = argumentsJson ?? "{}";
        }
    }

    /// <summary>
    /// 发给模型的工具定义：名称 + 描述 + 参数 JSON Schema（原始 JSON 文本）。
    /// </summary>
    public sealed class LLMToolSpec
    {
        public string Name { get; }
        public string Description { get; }
        public string ParametersJson { get; }

        public LLMToolSpec(
            string name,
            string description,
            string parametersJson)
        {
            Name = name;
            Description = description;
            ParametersJson = string.IsNullOrWhiteSpace(parametersJson)
                ? "{\"type\":\"object\",\"properties\":{}}"
                : parametersJson;
        }

        public static IReadOnlyList<LLMToolSpec> Empty { get; } =
            new LLMToolSpec[0];
    }
}
