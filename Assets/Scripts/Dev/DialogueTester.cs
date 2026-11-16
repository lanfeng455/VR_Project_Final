using System.Collections;
using UnityEngine;
using Game.Core;
using Game.UI;

namespace Game.Dev
{
    /// <summary>
    /// 测试器：GameObject 激活后，循环让对话气泡说语句库里的句子，
    /// 同时向海龟汤窗口写入测试提问，用来验证两块屏幕 UI。
    /// </summary>
    public sealed class DialogueTester : MonoBehaviour
    {
        [Header("语句库")]
        [SerializeField] private string[] lines =
        {
            "I wrote your question down. Feel free to keep digging.",
            "Clues never show up directly. Try asking something more specific.",
            "Part of what you just said is not entirely accurate.",
            "Ask one more question. You might piece together a new answer."
        };

        [SerializeField] private string[] questions =
        {
            "Was anyone else there when he said that?",
            "Who put the cup on the table?",
            "Why didn't he call the police right away?",
            "Was the door locked from the inside?"
        };

        [Header("播放")]
        [Tooltip("每句之间的间隔（秒）。")]
        [SerializeField] private float interval = 3f;

        [SerializeField] private bool loop = true;

        [Tooltip("同时把提问写进海龟汤窗口，用来测试右侧窗口。")]
        [SerializeField] private bool recordQuestions = true;

        private Coroutine routine;

        private void OnEnable()
        {
            routine = StartCoroutine(PlayRoutine());
        }

        private void OnDisable()
        {
            if (routine == null)
                return;

            StopCoroutine(routine);
            routine = null;
        }

        private IEnumerator PlayRoutine()
        {
            // 等待对话气泡完成初始化。
            while (DialogueBubble.Instance == null)
                yield return null;

            if (lines == null || lines.Length == 0)
                yield break;

            int index = 0;

            do
            {
                DialogueBubble.Show(lines[index % lines.Length]);

                if (recordQuestions &&
                    questions != null &&
                    questions.Length > 0)
                {
                    GameManager.Instance?.RecordQuestion(
                        questions[index % questions.Length]
                    );
                }

                index++;

                yield return new WaitForSeconds(interval);
            }
            while (loop);
        }
    }
}
