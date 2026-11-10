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

        [Serializable]
        private sealed class ApiMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private sealed class ChatRequest
        {
            public string model;
            public ApiMessage[] messages;
            public bool stream;
            public int max_tokens;
        }

        [Serializable]
        private sealed class ChatResponse
        {
            public Choice[] choices;
            public ApiError error;
        }

        [Serializable]
        private sealed class Choice
        {
            public ApiMessage message;
        }

        [Serializable]
        private sealed class ApiError
        {
            public string message;
            public string type;
        }

        public IEnumerator SendAsync(
            IReadOnlyList<LLMMessage> messages,
            Action<LLMResult> onComplete)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(
                    LLMResult.Fail("DeepSeek API Key 为空。"));
                yield break;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                onComplete?.Invoke(
                    LLMResult.Fail("模型名称不能为空。"));
                yield break;
            }

            if (messages == null || messages.Count == 0)
            {
                onComplete?.Invoke(
                    LLMResult.Fail("消息列表不能为空。"));
                yield break;
            }

            var apiMessages = new ApiMessage[messages.Count];

            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i] == null)
                {
                    onComplete?.Invoke(
                        LLMResult.Fail($"第 {i} 条消息为空。"));
                    yield break;
                }

                apiMessages[i] = new ApiMessage
                {
                    role = ToApiRole(messages[i].Role),
                    content = messages[i].Content
                };
            }

            var body = new ChatRequest
            {
                model = model,
                messages = apiMessages,
                stream = false,
                max_tokens = 1024
            };

            string json = UnityEngine.JsonUtility.ToJson(body);
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
                    {
                        error = request.error ?? "未知网络错误";
                    }

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
                            "解析 DeepSeek 响应失败：" + e.Message,
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
                        : "响应中没有有效的模型回复。";

                    onComplete?.Invoke(
                        LLMResult.Fail(error, statusCode));

                    yield break;
                }

                string content = response.choices[0].message.content;

                if (content == null)
                {
                    onComplete?.Invoke(
                        LLMResult.Fail(
                            "模型返回的 content 为空。",
                            statusCode));

                    yield break;
                }

                onComplete?.Invoke(
                    LLMResult.Ok(content, statusCode));
            }
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

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(role), role, "不支持的消息角色。");
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