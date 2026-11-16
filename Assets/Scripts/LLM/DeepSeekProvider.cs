using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace Game.LLM
{
    public sealed class DeepSeekProvider : ILLMProvider
    {
        private const string ApiUrl =
            "https://api.deepseek.com/chat/completions";

        private readonly string apiKey;
        private readonly string model;
        private readonly int timeoutSeconds;

        public DeepSeekProvider(
            string apiKey,
            string model = "deepseek-flash",
            int timeoutSeconds = 60)
        {
            this.apiKey = apiKey;
            this.model = model;
            this.timeoutSeconds = timeoutSeconds;
        }

        public IEnumerator SendAsync(
            IReadOnlyList<LLMMessage> messages,
            Action<LLMResult> onComplete)
        {
            return SendAsync(messages, LLMToolSpec.Empty, onComplete);
        }

        // ---------------- 响应 DTO（DeepSeek 的 OpenAI 兼容结构） ----------------

        [Serializable]
        private sealed class ApiFunctionCall
        {
            public string name;
            public string arguments;
        }

        [Serializable]
        private sealed class ApiToolCall
        {
            public string id;
            public string type;
            public ApiFunctionCall function;
        }

        [Serializable]
        private sealed class ApiMessage
        {
            public string role;
            public string content;
            public ApiToolCall[] tool_calls;
        }

        [Serializable]
        private sealed class Choice
        {
            public ApiMessage message;
            public string finish_reason;
        }

        [Serializable]
        private sealed class ApiError
        {
            public string message;
            public string type;
        }

        [Serializable]
        private sealed class ChatResponse
        {
            public Choice[] choices;
            public ApiError error;
        }

        // ---------------- 请求 ----------------

        public IEnumerator SendAsync(
            IReadOnlyList<LLMMessage> messages,
            IReadOnlyList<LLMToolSpec> tools,
            Action<LLMResult> onComplete)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(
                    LLMResult.Fail("DeepSeek API key is empty."));
                yield break;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                onComplete?.Invoke(
                    LLMResult.Fail("Model name cannot be empty."));
                yield break;
            }

            if (messages == null || messages.Count == 0)
            {
                onComplete?.Invoke(
                    LLMResult.Fail("The message list cannot be empty."));
                yield break;
            }

            string json = BuildRequestBody(messages, tools);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(ApiUrl, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = timeoutSeconds;

                request.SetRequestHeader(
                    "Content-Type", "application/json");

                request.SetRequestHeader(
                    "Authorization", "Bearer " + apiKey);

                yield return request.SendWebRequest();

                long statusCode = request.responseCode;
                string responseText = request.downloadHandler != null
                    ? request.downloadHandler.text
                    : string.Empty;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = TryGetApiError(responseText);

                    if (string.IsNullOrEmpty(error))
                        error = request.error ?? "Unknown network error";

                    onComplete?.Invoke(
                        LLMResult.Fail(error, statusCode));

                    yield break;
                }

                ChatResponse response;

                try
                {
                    response =
                        UnityEngine.JsonUtility.FromJson<ChatResponse>(
                            responseText);
                }
                catch (Exception e)
                {
                    onComplete?.Invoke(
                        LLMResult.Fail(
                            "Failed to parse the DeepSeek response: " +
                            e.Message,
                            statusCode));

                    yield break;
                }

                if (response == null ||
                    response.choices == null ||
                    response.choices.Length == 0 ||
                    response.choices[0].message == null)
                {
                    string error = response != null &&
                                   response.error != null
                        ? response.error.message
                        : "The response contained no valid model reply.";

                    onComplete?.Invoke(
                        LLMResult.Fail(error, statusCode));

                    yield break;
                }

                ApiMessage message = response.choices[0].message;
                string content = message.content;
                var toolCalls = ToToolCalls(message.tool_calls);

                if (toolCalls.Count > 0)
                {
                    // 工具调用时 content 通常为 null，属于正常情况。
                    onComplete?.Invoke(
                        LLMResult.WithToolCalls(
                            content,
                            toolCalls,
                            statusCode));

                    yield break;
                }

                if (content == null)
                {
                    onComplete?.Invoke(
                        LLMResult.Fail(
                            "The model returned empty content.",
                            statusCode));

                    yield break;
                }

                onComplete?.Invoke(
                    LLMResult.Ok(content, statusCode));
            }
        }

        private static List<LLMToolCall> ToToolCalls(
            ApiToolCall[] calls)
        {
            var result = new List<LLMToolCall>();

            if (calls == null)
                return result;

            for (int i = 0; i < calls.Length; i++)
            {
                ApiToolCall call = calls[i];

                if (call == null || call.function == null)
                    continue;

                if (string.IsNullOrWhiteSpace(call.function.name))
                    continue;

                result.Add(
                    new LLMToolCall(
                        call.id,
                        call.function.name,
                        call.function.arguments
                    )
                );
            }

            return result;
        }

        /// <summary>
        /// 手写请求体：tools[].function.parameters 必须是 JSON 对象，
        /// JsonUtility 只能输出字符串，所以这里按原始 JSON 拼装。
        /// </summary>
        private string BuildRequestBody(
            IReadOnlyList<LLMMessage> messages,
            IReadOnlyList<LLMToolSpec> tools)
        {
            var builder = new StringBuilder(1024);

            builder.Append("{\"model\":");
            AppendString(builder, model);

            builder.Append(",\"stream\":false,\"max_tokens\":1024");

            builder.Append(",\"messages\":[");
            AppendMessages(builder, messages);
            builder.Append(']');

            if (tools != null && tools.Count > 0)
            {
                builder.Append(",\"tool_choice\":\"auto\",\"tools\":[");
                AppendTools(builder, tools);
                builder.Append(']');
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendMessages(
            StringBuilder builder,
            IReadOnlyList<LLMMessage> messages)
        {
            for (int i = 0; i < messages.Count; i++)
            {
                LLMMessage message = messages[i];

                if (i > 0)
                    builder.Append(',');

                builder.Append("{\"role\":");
                AppendString(builder, ToApiRole(message.Role));

                builder.Append(",\"content\":");
                AppendString(builder, message.Content);

                if (message.Role == LLMRole.Tool &&
                    !string.IsNullOrEmpty(message.ToolCallId))
                {
                    builder.Append(",\"tool_call_id\":");
                    AppendString(builder, message.ToolCallId);
                }

                if (message.ToolCalls != null &&
                    message.ToolCalls.Count > 0)
                {
                    builder.Append(",\"tool_calls\":[");

                    for (int c = 0; c < message.ToolCalls.Count; c++)
                    {
                        LLMToolCall call = message.ToolCalls[c];

                        if (c > 0)
                            builder.Append(',');

                        builder.Append("{\"id\":");
                        AppendString(builder, call.Id);
                        builder.Append(",\"type\":\"function\",\"function\":{\"name\":");
                        AppendString(builder, call.Name);
                        builder.Append(",\"arguments\":");
                        AppendString(builder, call.ArgumentsJson);
                        builder.Append("}}");
                    }

                    builder.Append(']');
                }

                builder.Append('}');
            }
        }

        private static void AppendTools(
            StringBuilder builder,
            IReadOnlyList<LLMToolSpec> tools)
        {
            for (int i = 0; i < tools.Count; i++)
            {
                LLMToolSpec tool = tools[i];

                if (i > 0)
                    builder.Append(',');

                builder.Append("{\"type\":\"function\",\"function\":{\"name\":");
                AppendString(builder, tool.Name);
                builder.Append(",\"description\":");
                AppendString(builder, tool.Description);
                builder.Append(",\"parameters\":");
                builder.Append(tool.ParametersJson);
                builder.Append("}}");
            }
        }

        private static void AppendString(
            StringBuilder builder,
            string value)
        {
            builder.Append('"');

            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];

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
                                builder.Append("\\u");
                                builder.Append(((int)c).ToString("x4"));
                            }
                            else
                            {
                                builder.Append(c);
                            }

                            break;
                    }
                }
            }

            builder.Append('"');
        }

        private static string ToApiRole(LLMRole role)
        {
            switch (role)
            {
                case LLMRole.System:
                    return "system";

                case LLMRole.User:
                    return "user";

                case LLMRole.Assistant:
                    return "assistant";

                case LLMRole.Tool:
                    return "tool";

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(role), role, "Unsupported message role.");
            }
        }

        private static string TryGetApiError(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return null;

            try
            {
                ChatResponse response =
                    UnityEngine.JsonUtility.FromJson<ChatResponse>(
                        responseText);

                if (response != null && response.error != null)
                    return response.error.message;
            }
            catch
            {
                // 错误响应不是预期 JSON 时，由调用方使用网络错误信息。
            }

            return null;
        }
    }
}
