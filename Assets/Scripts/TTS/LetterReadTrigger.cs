using UnityEngine;

namespace Game.TTS
{
    /// <summary>
    /// 挂在剧情信件上，作为「为我朗读」的明确入口：
    /// 可以绑定到信上的 Button / 交互事件，也可以开启碰撞体进入时自动朗读。
    /// 不做任何意图猜测。
    /// </summary>
    public sealed class LetterReadTrigger : MonoBehaviour
    {
        [Tooltip("留空时使用场景里的 LunaTTSController.Instance。")]
        [SerializeField] private LunaTTSController tts;

        [Tooltip("信件的英文全文。")]
        [SerializeField, TextArea(6, 20)]
        private string letterText =
            "The house remembers what its owners tried to forget. " +
            "Ten days before the storm, the clock in the hall stopped " +
            "at 3:17. No one wound it again.";

        [Tooltip("玩家进入触发器时自动朗读；默认关闭，优先用按钮/交互显式触发。")]
        [SerializeField] private bool readOnTriggerEnter;

        /// <summary>供 Button / UnityEvent / 手柄射线调用。</summary>
        public void Read()
        {
            LunaTTSController controller =
                tts != null ? tts : LunaTTSController.Instance;

            if (controller == null)
            {
                Debug.LogWarning(
                    "LetterReadTrigger: no LunaTTSController in the scene.",
                    this
                );

                return;
            }

            controller.ReadLetter(letterText);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!readOnTriggerEnter)
                return;

            Read();
        }
    }
}
