using System.Collections.Generic;

namespace Game.Tools
{
    /// <summary>
    /// 工具执行结果。Message 会作为 tool 消息回填给模型，
    /// 因此必须是英文且简短（成功/失败/当前状态）。
    /// </summary>
    public readonly struct ToolExecutionResult
    {
        public bool Success { get; }
        public string Message { get; }

        /// <summary>该工具是否真的改变了场景状态（用于标记 key question）。</summary>
        public bool ChangedScene { get; }

        private ToolExecutionResult(
            bool success,
            string message,
            bool changedScene)
        {
            Success = success;
            Message = message;
            ChangedScene = changedScene;
        }

        public static ToolExecutionResult Ok(
            string message,
            bool changedScene = false)
        {
            return new ToolExecutionResult(true, message, changedScene);
        }

        public static ToolExecutionResult Fail(string message)
        {
            return new ToolExecutionResult(false, message, false);
        }
    }

    /// <summary>
    /// 可被 LLM 调用的工具。新增工具只需实现该接口并注册，
    /// 不要依赖具体工具名做分支。
    /// </summary>
    public interface ILLMTool
    {
        string Name { get; }
        string Description { get; }

        /// <summary>参数的 JSON Schema 原文。</summary>
        string ParametersJson { get; }

        /// <summary>执行成功后是否改变了场景状态。</summary>
        bool ChangesScene { get; }

        /// <summary>前置条件检查：不满足时不暴露给模型。</summary>
        bool IsAvailable(out string reason);

        /// <summary>
        /// 执行工具。argumentsJson 来自模型，必须自行校验；
        /// 任何异常都不能向上抛，统一返回 Fail。
        /// </summary>
        ToolExecutionResult Execute(string argumentsJson);
    }

    public sealed class LLMToolRegistry
    {
        private readonly List<ILLMTool> tools = new List<ILLMTool>();

        public int Count => tools.Count;

        public void Register(ILLMTool tool)
        {
            if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
                return;

            for (int i = 0; i < tools.Count; i++)
            {
                if (tools[i].Name == tool.Name)
                {
                    tools[i] = tool;
                    return;
                }
            }

            tools.Add(tool);
        }

        /// <summary>只把当前可用的工具发给模型。</summary>
        public IReadOnlyList<LLM.LLMToolSpec> BuildSpecs()
        {
            var specs = new List<LLM.LLMToolSpec>();

            for (int i = 0; i < tools.Count; i++)
            {
                if (!tools[i].IsAvailable(out _))
                    continue;

                specs.Add(
                    new LLM.LLMToolSpec(
                        tools[i].Name,
                        tools[i].Description,
                        tools[i].ParametersJson
                    )
                );
            }

            return specs;
        }

        public ToolExecutionResult Execute(LLM.LLMToolCall call)
        {
            if (call == null)
                return ToolExecutionResult.Fail("Invalid tool call.");

            ILLMTool tool = Find(call.Name);

            if (tool == null)
            {
                return ToolExecutionResult.Fail(
                    $"Unknown tool '{call.Name}'.");
            }

            if (!tool.IsAvailable(out string reason))
            {
                return ToolExecutionResult.Fail(
                    string.IsNullOrEmpty(reason)
                        ? $"Tool '{call.Name}' is not available."
                        : reason);
            }

            ToolExecutionResult result = tool.Execute(call.ArgumentsJson);

            return result.Success && tool.ChangesScene
                ? ToolExecutionResult.Ok(result.Message, true)
                : result;
        }

        private ILLMTool Find(string name)
        {
            for (int i = 0; i < tools.Count; i++)
            {
                if (tools[i].Name == name)
                    return tools[i];
            }

            return null;
        }
    }

    /// <summary>
    /// 极简的参数读取：只支持扁平对象里的字符串字段。
    /// 模型的 arguments 不一定合法 JSON，所有方法都返回 bool。
    /// </summary>
    public static class ToolArguments
    {
        public static bool TryGetString(
            string json,
            string key,
            out string value)
        {
            value = null;

            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
                return false;

            string needle = "\"" + key + "\"";
            int index = json.IndexOf(needle, System.StringComparison.Ordinal);

            if (index < 0)
                return false;

            index = json.IndexOf(':', index + needle.Length);

            if (index < 0)
                return false;

            index++;

            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;

            if (index >= json.Length || json[index] != '"')
                return false;

            index++;

            var builder = new System.Text.StringBuilder();

            while (index < json.Length)
            {
                char c = json[index];

                if (c == '\\' && index + 1 < json.Length)
                {
                    char next = json[index + 1];

                    switch (next)
                    {
                        case 'n':
                            builder.Append('\n');
                            break;

                        case 't':
                            builder.Append('\t');
                            break;

                        case 'r':
                            builder.Append('\r');
                            break;

                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(next);
                            break;

                        default:
                            builder.Append(next);
                            break;
                    }

                    index += 2;
                    continue;
                }

                if (c == '"')
                {
                    value = builder.ToString();
                    return true;
                }

                builder.Append(c);
                index++;
            }

            return false;
        }
    }
}
