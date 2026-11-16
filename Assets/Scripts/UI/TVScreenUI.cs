using System.Text;
using Game.Core;
using TMPro;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.UI
{
    /// <summary>
    /// 电视屏显示层控制器。
    /// 界面物体由编辑器菜单 Tools/VR Project/LUNA/Build TV UI In Scene
    /// 一次性生成并保存到场景中，运行时这里只负责：
    /// 切换 A/B 显隐、写入文字、驱动月亮波纹。
    /// </summary>
    public sealed class TVScreenUI : MonoBehaviour
    {
        public static TVScreenUI Instance { get; private set; }

        [Header("界面根节点")]
        [Tooltip("界面 A：月亮 + LUNA + Need A Hint? + 六条提示。")]
        [SerializeField] private GameObject homeRoot;

        [Tooltip("界面 B：LUNA LOG 问答记录。")]
        [SerializeField] private GameObject logRoot;

        [Header("月亮")]
        [SerializeField] private LunaMoonGraphic homeMoon;
        [SerializeField] private LunaMoonGraphic logMoon;

        [Header("界面 B 文本")]
        [SerializeField] private TMP_Text logText;
        [SerializeField] private string emptyLogText = "No questions yet.";
        [SerializeField] private string questionLabel = "Question: ";
        [SerializeField] private string keyQuestionLabel = "Key Question: ";
        [SerializeField] private string answerLabel = "Answer: ";

        [Header("颜色")]
        [SerializeField] private Color textColor =
            new Color(0.95f, 0.95f, 0.95f, 1f);
        [SerializeField] private Color dimColor =
            new Color(0.86f, 0.86f, 0.86f, 1f);
        [SerializeField] private Color keyQuestionColor =
            new Color(0.78f, 0.35f, 0.32f, 1f);

        [Header("输入")]
        [Tooltip("编辑器/PC 上用 L 键在界面 A、B 之间切换。")]
        [SerializeField] private bool enableLogKey = true;

        [Tooltip("自动跟随对话气泡的打字机状态驱动波纹。")]
        [SerializeField] private bool autoDriveSpeaking = true;

        private bool showingLog;
        private bool bound;

        public bool IsShowingLog => showingLog;

        private void Awake()
        {
            Instance = this;

#if UNITY_EDITOR
            // 编辑器里 OVROverlayCanvas 的合成不会在 Game 视图生效，
            // 关掉它才能直接看到 World Space UI。
            OVROverlayCanvas overlay = GetComponent<OVROverlayCanvas>();

            if (overlay != null)
                overlay.enabled = false;

            gameObject.layer = 0;
#endif

            ShowHome();
        }

        private void Start()
        {
            Bind();
        }

        private void OnDestroy()
        {
            Unbind();

            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (autoDriveSpeaking)
            {
                DialogueBubble bubble = DialogueBubble.Instance;
                SetSpeaking(bubble != null && bubble.IsTyping);
            }

#if ENABLE_INPUT_SYSTEM
            if (enableLogKey &&
                Keyboard.current != null &&
                Keyboard.current.lKey.wasPressedThisFrame)
            {
                if (showingLog)
                    ShowHome();
                else
                    ShowLog();
            }
#endif
        }

        // ---------------- 对外接口 ----------------

        /// <summary>切到界面 A（月亮 + Need A Hint?）。</summary>
        public void ShowHome()
        {
            showingLog = false;
            SetActive(homeRoot, true);
            SetActive(logRoot, false);
            SetSpeaking(false);
        }

        /// <summary>切到界面 B（LUNA LOG）。</summary>
        public void ShowLog()
        {
            showingLog = true;
            SetActive(homeRoot, false);
            SetActive(logRoot, true);
            SetSpeaking(false);
            RefreshLog();
        }

        /// <summary>驱动月亮波纹：主持人说话时 true。</summary>
        public void SetSpeaking(bool value)
        {
            if (homeMoon != null)
                homeMoon.SetSpeaking(value);

            if (logMoon != null)
                logMoon.SetSpeaking(value);
        }

        /// <summary>
        /// 追加一条问答记录。isKey 决定 Question 行是否用强调色。
        /// 闲聊不应传到这里（见 GameManager 的 isChitchat 过滤）。
        /// </summary>
        public void AddEntry(string question, string answer, bool isKey)
        {
            GameManager manager = GameManager.Instance;

            if (manager == null)
                return;

            int id = manager.RecordQuestion(question, isKey, false);

            if (id > 0 && !string.IsNullOrWhiteSpace(answer))
                manager.RecordAnswer(id, answer);
        }

        public void RefreshLog()
        {
            if (logText == null)
                return;

            GameManager manager = GameManager.Instance;

            if (manager == null || manager.Questions.Count == 0)
            {
                logText.text = emptyLogText;
                return;
            }

            var builder = new StringBuilder();
            int shown = 0;

            for (int i = 0; i < manager.Questions.Count; i++)
            {
                TurtleSoupEntry entry = manager.Questions[i];

                // 闲聊不进 LOG，只保留真正的提问。
                if (entry.isChitchat)
                    continue;

                if (shown > 0)
                    builder.Append('\n');

                string label = entry.isKey
                    ? keyQuestionLabel
                    : questionLabel;

                string color = ColorUtility.ToHtmlStringRGB(
                    entry.isKey ? keyQuestionColor : textColor
                );

                string answer = string.IsNullOrWhiteSpace(entry.answer)
                    ? "…"
                    : entry.answer.Trim();

                builder.AppendFormat(
                    "<color=#{0}>{1}{2}</color>\n<color=#{3}>{4}{5}</color>",
                    color,
                    label,
                    entry.question,
                    ColorUtility.ToHtmlStringRGB(dimColor),
                    answerLabel,
                    answer
                );

                shown++;
            }

            logText.text = shown == 0 ? emptyLogText : builder.ToString();
        }

        // ---------------- 内部 ----------------

        private void Bind()
        {
            if (bound)
                return;

            GameManager manager = GameManager.Instance;

            if (manager == null)
            {
                Debug.LogWarning(
                    "TVScreenUI: no GameManager found in the scene.",
                    this
                );

                return;
            }

            manager.QuestionsChanged += RefreshLog;
            bound = true;

            RefreshLog();
        }

        private void Unbind()
        {
            if (!bound)
                return;

            GameManager manager = GameManager.Instance;

            if (manager != null)
                manager.QuestionsChanged -= RefreshLog;

            bound = false;
        }

        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value)
                target.SetActive(value);
        }
    }
}
