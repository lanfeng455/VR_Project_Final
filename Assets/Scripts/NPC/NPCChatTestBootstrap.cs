using System.Collections;
using UnityEngine;
using Game.LLM;

namespace Game.NPC
{
    public sealed class NPCChatTestBootstrap : MonoBehaviour
    {
        [Header("Test Configuration")]
        [SerializeField] private NPCChatController npc;
        [SerializeField] private string apiKey = "";
        [SerializeField] private string model = "deepseek-flash";

        private void Start()
        {
            if (npc == null)
            {
                Debug.LogError("请先配置 NPCChatController 引用。");
                return;
            }

            ILLMProvider provider = new DeepSeekProvider(
                apiKey,
                model
            );

            npc.Initialize(provider);

            // StartCoroutine(TestConversation());
        }

        // private IEnumerator TestConversation()
        // {
        //     Debug.Log($"开始与 {npc.CharacterName} 对话。");
        //
        //     // 第一轮：告诉 NPC 一个名字。
        //     yield return npc.SendMessageAsync(
        //         "你好，我叫小明，请记住我的名字。",
        //         result =>
        //         {
        //             LogResult("第一轮", result);
        //         }
        //     );
        //
        //     if (npc.HistoryMessageCount == 0)
        //         yield break;
        //
        //     // 第二轮：检查模型能否利用之前提供的历史。
        //     yield return npc.SendMessageAsync(
        //         "我刚才告诉你我叫什么名字？",
        //         result =>
        //         {
        //             LogResult("第二轮", result);
        //         }
        //     );
        //
        //     Debug.Log(
        //         $"测试结束，历史消息数量：{npc.HistoryMessageCount}"
        //     );
        // }

        private void LogResult(string label, LLMResult result)
        {
            if (result.Success)
            {
                Debug.Log(
                    $"{label}，{npc.CharacterName}：\n{result.Content}"
                );
            }
            else
            {
                Debug.LogError(
                    $"{label}请求失败：{result.Error}"
                );
            }
        }
    }
}