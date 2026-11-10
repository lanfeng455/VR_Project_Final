using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.Core;
using Game.LLM;
using Game.UI;

namespace Game.NPC
{
    public sealed class NPCChatController : MonoBehaviour
    {
        [Header("NPC Personality")]
        [SerializeField] private NPCPersonality personality;

        private ILLMProvider provider;

        private readonly List<LLMMessage> history =
            new List<LLMMessage>();

        private bool isBusy;

        public string CharacterName =>
            personality != null
                ? personality.CharacterName
                : "NPC";

        public bool IsBusy => isBusy;

        public NPCPersonality Personality => personality;

        public int HistoryMessageCount => history.Count;

        /// <summary>
        /// 由外部注入模型提供者。
        /// 例如：DeepSeekProvider。
        /// </summary>
        public void Initialize(ILLMProvider llmProvider)
        {
            if (llmProvider == null)
                throw new ArgumentNullException(nameof(llmProvider));

            provider = llmProvider;
            ClearHistory();
        }

        /// <summary>
        /// 发送一条玩家消息。
        /// 成功后保存玩家消息与 NPC 回复。
        /// </summary>
        public IEnumerator SendMessageAsync(
            string userText,
            Action<LLMResult> onComplete)
        {
            if (isBusy)
            {
                onComplete?.Invoke(
                    LLMResult.Fail("NPC 正在处理上一条消息。"));
                yield break;
            }

            if (personality == null)
            {
                onComplete?.Invoke(
                    LLMResult.Fail("尚未配置 NPCPersonality。"));
                yield break;
            }

            if (provider == null)
            {
                onComplete?.Invoke(
                    LLMResult.Fail("尚未初始化 LLM Provider。"));
                yield break;
            }

            if (string.IsNullOrWhiteSpace(userText))
            {
                onComplete?.Invoke(
                    LLMResult.Fail("消息内容不能为空。"));
                yield break;
            }

            isBusy = true;

            try
            {
                // 临时请求列表：人格提示词 + 已有历史 + 当前输入。
                // 先不修改正式历史，避免失败请求污染会话。
                var requestMessages = new List<LLMMessage>(
                    history.Count + 2
                );

                requestMessages.Add(
                    new LLMMessage(
                        LLMRole.System,
                        personality.BuildSystemPrompt()
                    )
                );

                requestMessages.AddRange(history);

                requestMessages.Add(
                    new LLMMessage(LLMRole.User, userText)
                );

                LLMResult result = null;

                yield return provider.SendAsync(
                    requestMessages,
                    response => result = response
                );

                if (result == null)
                {
                    result = LLMResult.Fail(
                        "Provider 没有返回请求结果。"
                    );
                }

                if (result.Success)
                {
                    history.Add(
                        new LLMMessage(LLMRole.User, userText)
                    );

                    history.Add(
                        new LLMMessage(
                            LLMRole.Assistant,
                            result.Content
                        )
                    );

                    // 把回复同步给全场景的对话UI（纯显示层）。
                    DialogueBubble.Show(result.Content);

                    // 记录玩家提问；答案由 GameManager.RecordAnswer 预留接口补充。
                    GameManager.Instance?.RecordQuestion(userText);
                }

                onComplete?.Invoke(result);
            }
            finally
            {
                isBusy = false;
            }
        }

        /// <summary>
        /// 清除当前 NPC 的会话历史。
        /// 人格配置不受影响。
        /// </summary>
        public void ClearHistory()
        {
            history.Clear();
        }
    }
}