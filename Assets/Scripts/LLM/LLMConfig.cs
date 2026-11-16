using System;
using UnityEngine;

namespace Game.LLM
{
    /// <summary>
    /// 从 Resources 下的 JSON 读取 LLM 配置。
    /// 真实的 llm-config.json 不进入版本库，只提交示例文件。
    /// </summary>
    public static class LLMConfig
    {
        [Serializable]
        public sealed class Data
        {
            public string apiKey;
            public string model;
        }

        public static bool TryLoad(
            string resourcePath,
            out Data data,
            out string error)
        {
            data = null;
            error = null;

            TextAsset asset = Resources.Load<TextAsset>(resourcePath);

            if (asset == null)
            {
                error =
                    $"LLM config not found: Resources/{resourcePath}.json";
                return false;
            }

            try
            {
                data = JsonUtility.FromJson<Data>(asset.text);
            }
            catch (Exception e)
            {
                error = "Failed to parse the LLM config: " + e.Message;
                return false;
            }

            if (data == null || string.IsNullOrWhiteSpace(data.apiKey))
            {
                error = "LLM config is missing apiKey.";
                return false;
            }

            data.apiKey = data.apiKey.Trim();
            data.model = string.IsNullOrWhiteSpace(data.model)
                ? "deepseek-flash"
                : data.model.Trim();

            return true;
        }
    }
}
