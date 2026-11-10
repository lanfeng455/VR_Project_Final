using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.LLM;

public class LLMProviderTest : MonoBehaviour
{
    [Header("DeepSeek")]
    [SerializeField] private string apiKey = "";
    [SerializeField] private string model = "deepseek-flash";

    private void Start()
    {
        ILLMProvider provider = new DeepSeekProvider(
            apiKey,
            model
        );

        var messages = new List<LLMMessage>
        {
            new LLMMessage(
                LLMRole.System,
                "你是一位性格内向、说话简洁的图书管理员。"
            ),
            new LLMMessage(
                LLMRole.User,
                "你好，请简单介绍一下自己。"
            )
        };

        StartCoroutine(
            provider.SendAsync(messages, OnResponse)
        );
    }

    private void OnResponse(LLMResult result)
    {
        if (result.Success)
        {
            Debug.Log($"LLM 回复：\n{result.Content}");
        }
        else
        {
            Debug.LogError(
                $"LLM 请求失败，HTTP {result.StatusCode}：\n" +
                result.Error
            );
        }
    }
}