using System.Text;
using TMPro;
using UnityEngine;
using Game.Core;

namespace Game.UI
{
    /// <summary>
    /// 世界空间电视屏的显示层：
    /// 左侧显示最近的对话日志，右侧显示海龟汤问答。
    /// 只读取 GameManager 的数据，不包含任何游戏逻辑。
    /// </summary>
    public sealed class TVScreenUI : MonoBehaviour
    {
        [Header("窗口")]
        [SerializeField] private TMP_Text logText;
        [SerializeField] private TMP_Text questionText;

        [Header("格式")]
        [SerializeField] private string logLineFormat =
            "<color=#7FD3FF>{0}</color>  {1}";

        [SerializeField] private string questionLineFormat =
            "<color=#FFD27F>问：</color>{0}\n<color=#9BE29B>答：</color>{1}";

        [SerializeField] private string emptyLogText = "暂无对话记录。";
        [SerializeField] private string emptyQuestionText = "暂无提问。";
        [SerializeField] private string blankAnswerText = "……";

        private bool bound;

        private void Awake()
        {
#if UNITY_EDITOR
            // 编辑器里 OVROverlayCanvas 的画面由运行时合成器叠加，Game 视图看不到，会是一片黑。
            // 这里退成普通 World Space Canvas，让 PC 上也能看到屏幕内容。
            var overlay = GetComponent<OVROverlayCanvas>();

            if (overlay != null)
                overlay.enabled = false;

            SetLayerRecursively(gameObject, 0);
#endif
        }

        private void Start()
        {
            Bind();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Bind()
        {
            if (bound)
                return;

            GameManager manager = GameManager.Instance;

            if (manager == null)
            {
                Debug.LogWarning(
                    "TVScreenUI：场景里没有 GameManager。",
                    this
                );

                return;
            }

            manager.LogChanged += RefreshLog;
            manager.QuestionsChanged += RefreshQuestions;
            bound = true;

            RefreshLog();
            RefreshQuestions();
        }

        private void Unbind()
        {
            if (!bound)
                return;

            GameManager manager = GameManager.Instance;

            if (manager != null)
            {
                manager.LogChanged -= RefreshLog;
                manager.QuestionsChanged -= RefreshQuestions;
            }

            bound = false;
        }

        public void RefreshLog()
        {
            if (logText == null)
                return;

            GameManager manager = GameManager.Instance;

            if (manager == null || manager.LogEntries.Count == 0)
            {
                logText.text = emptyLogText;
                return;
            }

            var builder = new StringBuilder();

            for (int i = 0; i < manager.LogEntries.Count; i++)
            {
                DialogueLogEntry entry = manager.LogEntries[i];

                if (builder.Length > 0)
                    builder.Append('\n');

                builder.AppendFormat(
                    logLineFormat,
                    entry.speaker,
                    entry.text
                );
            }

            logText.text = builder.ToString();
        }

        public void RefreshQuestions()
        {
            if (questionText == null)
                return;

            GameManager manager = GameManager.Instance;

            if (manager == null || manager.Questions.Count == 0)
            {
                questionText.text = emptyQuestionText;
                return;
            }

            var builder = new StringBuilder();

            for (int i = 0; i < manager.Questions.Count; i++)
            {
                TurtleSoupEntry entry = manager.Questions[i];

                if (builder.Length > 0)
                    builder.Append("\n\n");

                string answer = string.IsNullOrWhiteSpace(entry.answer)
                    ? blankAnswerText
                    : entry.answer;

                builder.AppendFormat(
                    questionLineFormat,
                    entry.question,
                    answer
                );
            }

            questionText.text = builder.ToString();
        }

        /// <summary>按钮回调：清空日志。</summary>
        public void ClearLog()
        {
            GameManager.Instance?.ClearLog();
        }

        /// <summary>按钮回调：清空海龟汤问答。</summary>
        public void ClearQuestions()
        {
            GameManager.Instance?.ClearQuestions();
        }

#if UNITY_EDITOR
        private static void SetLayerRecursively(GameObject target, int layer)
        {
            target.layer = layer;

            foreach (Transform child in target.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
#endif
    }
}
