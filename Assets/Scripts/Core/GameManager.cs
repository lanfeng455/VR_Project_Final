using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    [Serializable]
    public struct DialogueLogEntry
    {
        public string speaker;
        public string text;
    }

    [Serializable]
    public struct TurtleSoupEntry
    {
        public int id;
        public string question;
        public string answer;
    }

    /// <summary>
    /// 全局游戏管理器：记录对话文本与海龟汤问答，供世界空间屏幕读取。
    /// 只负责数据，不处理显示。
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("容量")]
        [SerializeField, Min(1)] private int maxLogEntries = 18;
        [SerializeField, Min(1)] private int maxQuestionEntries = 12;

        [Header("日志来源")]
        [Tooltip("自动记录 DialogueBubble 中显示的所有文本。")]
        [SerializeField] private bool recordDialogueBubble = true;

        [Tooltip("对话气泡没有指定说话人时使用的名字。")]
        [SerializeField] private string defaultSpeaker = "NPC";

        private readonly List<DialogueLogEntry> logEntries =
            new List<DialogueLogEntry>();

        private readonly List<TurtleSoupEntry> questions =
            new List<TurtleSoupEntry>();

        private int nextQuestionId = 1;

        public event Action LogChanged;
        public event Action QuestionsChanged;

        public IReadOnlyList<DialogueLogEntry> LogEntries => logEntries;
        public IReadOnlyList<TurtleSoupEntry> Questions => questions;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            // DialogueBubble 的所有 Awake 已执行完，可以安全订阅。
            if (recordDialogueBubble &&
                Game.UI.DialogueBubble.Instance != null)
            {
                Game.UI.DialogueBubble.Instance.DisplayCompleted +=
                    OnBubbleDisplayCompleted;
            }
        }

        private void OnDestroy()
        {
            if (Game.UI.DialogueBubble.Instance != null)
            {
                Game.UI.DialogueBubble.Instance.DisplayCompleted -=
                    OnBubbleDisplayCompleted;
            }

            if (Instance == this)
                Instance = null;
        }

        private void OnBubbleDisplayCompleted(string text)
        {
            RecordDialogue(defaultSpeaker, text);
        }

        /// <summary>记录一条对话文本，只保留最近的若干条。</summary>
        public void RecordDialogue(string speaker, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (string.IsNullOrWhiteSpace(speaker))
                speaker = defaultSpeaker;

            logEntries.Add(
                new DialogueLogEntry
                {
                    speaker = speaker,
                    text = text.Trim()
                }
            );

            while (logEntries.Count > maxLogEntries)
                logEntries.RemoveAt(0);

            LogChanged?.Invoke();
        }

        /// <summary>
        /// 记录玩家提问（答案留空），返回稳定的提问编号。
        /// </summary>
        public int RecordQuestion(string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return -1;

            int id = nextQuestionId++;

            questions.Add(
                new TurtleSoupEntry
                {
                    id = id,
                    question = question.Trim(),
                    answer = string.Empty
                }
            );

            while (questions.Count > maxQuestionEntries)
                questions.RemoveAt(0);

            QuestionsChanged?.Invoke();

            return id;
        }

        /// <summary>
        /// 为指定提问补充答案。当前版本暂不调用，保留给后续 LLM 回答使用。
        /// </summary>
        public void RecordAnswer(int id, string answer)
        {
            for (int i = 0; i < questions.Count; i++)
            {
                if (questions[i].id != id)
                    continue;

                TurtleSoupEntry entry = questions[i];
                entry.answer = answer ?? string.Empty;
                questions[i] = entry;

                QuestionsChanged?.Invoke();
                return;
            }
        }

        public void ClearLog()
        {
            if (logEntries.Count == 0)
                return;

            logEntries.Clear();
            LogChanged?.Invoke();
        }

        public void ClearQuestions()
        {
            if (questions.Count == 0)
                return;

            questions.Clear();
            QuestionsChanged?.Invoke();
        }
    }
}
