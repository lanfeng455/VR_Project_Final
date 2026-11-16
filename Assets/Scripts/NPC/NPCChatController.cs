using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Game.Core;
using Game.LLM;
using Game.Tools;
using Game.UI;

namespace Game.NPC
{
    public sealed class NPCChatController : MonoBehaviour
    {
        [Header("NPC Personality")]
        [SerializeField] private NPCPersonality personality;

        [Tooltip("一次回复里允许的最大工具调用轮数，防止死循环。")]
        [SerializeField, Range(1, 5)] private int maxToolRounds = 3;

        private ILLMProvider provider;

        private readonly List<LLMMessage> history =
            new List<LLMMessage>();

        private readonly LLMToolRegistry tools = new LLMToolRegistry();

        private bool isBusy;

        /// <summary>可选：判断某条问答是否为闲聊（true 时不进 LOG）。</summary>
        public Func<string, string, bool> ChitchatClassifier { get; set; }

        /// <summary>一次有效的 LLM 请求开始（用于显示 THINKING 占位）。</summary>
        public event Action RequestStarted;

        /// <summary>请求结束。参数表示请求是否成功。</summary>
        public event Action<bool> RequestFinished;

        public LLMToolRegistry Tools => tools;

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
                    LLMResult.Fail("The NPC is still handling the previous message."));
                yield break;
            }

            if (personality == null)
            {
                onComplete?.Invoke(
                    LLMResult.Fail("No NPCPersonality configured."));
                yield break;
            }

            if (provider == null)
            {
                onComplete?.Invoke(
                    LLMResult.Fail("The LLM provider has not been initialized."));
                yield break;
            }

            if (string.IsNullOrWhiteSpace(userText))
            {
                onComplete?.Invoke(
                    LLMResult.Fail("Message text cannot be empty."));
                yield break;
            }

            LLMResult result = null;

            isBusy = true;

            try
            {
                RequestStarted?.Invoke();

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

                bool sceneChanged = false;
                int round = 0;

                while (true)
                {
                    result = null;

                    yield return provider.SendAsync(
                        requestMessages,
                        tools.BuildSpecs(),
                        response => result = response
                    );

                    if (result == null)
                    {
                        result = LLMResult.Fail(
                            "The provider returned no result."
                        );

                        break;
                    }

                    if (!result.Success || !result.HasToolCalls)
                        break;

                    if (round >= maxToolRounds)
                    {
                        result = LLMResult.Fail(
                            "Tool call limit reached."
                        );

                        break;
                    }

                    // 把模型发起的调用和每个工具的执行结果回填，
                    // 让模型基于真实状态产出最终台词。
                    requestMessages.Add(
                        LLMMessage.AssistantWithTools(
                            result.Content,
                            result.ToolCalls
                        )
                    );

                    for (int i = 0; i < result.ToolCalls.Count; i++)
                    {
                        LLMToolCall call = result.ToolCalls[i];
                        ToolExecutionResult execution = tools.Execute(call);

                        if (execution.ChangedScene)
                            sceneChanged = true;

                        requestMessages.Add(
                            LLMMessage.ToolResult(
                                call.Id,
                                execution.Message
                            )
                        );
                    }

                    round++;
                }

                if (result.Success)
                {
                    string reply = string.IsNullOrWhiteSpace(result.Content)
                        ? "…"
                        : result.Content;

                    result = LLMResult.Ok(reply, result.StatusCode);

                    requestMessages.Add(
                        new LLMMessage(LLMRole.Assistant, reply)
                    );

                    history.Clear();

                    for (int i = 1; i < requestMessages.Count; i++)
                        history.Add(requestMessages[i]);

                    TrimHistory();

                    // 电视左屏：先写入玩家提问，NPC 回复由 DialogueBubble 完成后写入。
                    GameManager.Instance?.RecordDialogue("You", userText);

                    bool isChitchat = ChitchatClassifier != null &&
                                      ChitchatClassifier(userText, reply);

                    // 电视右屏 / LUNA LOG：一问一答。
                    // 触发过改变场景的工具，就视为关键问题。
                    int questionId = GameManager.Instance != null
                        ? GameManager.Instance.RecordQuestion(
                            userText,
                            sceneChanged,
                            isChitchat)
                        : -1;

                    if (questionId > 0)
                    {
                        GameManager.Instance.RecordAnswer(
                            questionId,
                            reply
                        );
                    }

                    // 把回复同步给全场景的对话UI（纯显示层）。
                    DialogueBubble.Show(reply);
                }

                onComplete?.Invoke(result);
            }
            finally
            {
                isBusy = false;
                RequestFinished?.Invoke(result != null && result.Success);
            }
        }

        private const int MaxHistoryMessages = 20;

        /// <summary>
        /// 限制历史长度，同时保证不会以 tool 消息开头
        /// （tool 消息必须有对应的 tool_calls）。
        /// </summary>
        private void TrimHistory()
        {
            while (history.Count > MaxHistoryMessages)
            {
                history.RemoveAt(0);

                while (history.Count > 0 &&
                       history[0].Role == LLMRole.Tool)
                {
                    history.RemoveAt(0);
                }
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