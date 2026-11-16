using System.Collections;
using UnityEngine;
using Game.LLM;
using Game.NPC;
using Game.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.Voice
{
    /// <summary>
    /// 把语音识别结果交给 NPC 对话控制器，并提供手柄 toggle 触发。
    /// 不负责 UI 布局，也不生成任何场景对象。
    /// </summary>
    public sealed class VoiceChatController : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private WhisperVoiceInput voice;
        [SerializeField] private NPCChatController npc;

        [Header("Quest 手柄按键")]
        [SerializeField] private bool enableControllerToggle = true;
        [Tooltip("默认使用右手 A 键；不与射线扳机、摇杆传送冲突。")]
        [SerializeField] private OVRInput.Button recordButton =
            OVRInput.Button.One;
        [SerializeField] private OVRInput.Controller recordController =
            OVRInput.Controller.RTouch;

        [Header("录音提示")]
        [Tooltip("录音期间常驻显示在对话气泡上的提示。")]
        [SerializeField] private string recordingHint =
            "● Listening…  Press A to stop.";

        [Header("调试")]
        [SerializeField] private bool enableDebugTextKey = true;
        [SerializeField, TextArea(2, 4)]
        private string debugText =
            "Hello! Introduce yourself in one sentence.";

        private bool wasRecording;

        private void OnEnable()
        {
            if (voice == null)
                return;

            voice.FinalTranscript += HandleFinalTranscript;
            voice.StatusChanged += HandleVoiceStatus;
        }

        private void OnDisable()
        {
            if (voice == null)
                return;

            voice.FinalTranscript -= HandleFinalTranscript;
            voice.StatusChanged -= HandleVoiceStatus;
        }

        private void Update()
        {
            UpdateRecordingIndicator();

            if (enableControllerToggle &&
                OVRInput.GetDown(recordButton, recordController))
            {
                if (voice != null)
                    voice.ToggleRecording();
            }

#if ENABLE_INPUT_SYSTEM
            if (enableDebugTextKey &&
                Keyboard.current != null &&
                Keyboard.current.tKey.wasPressedThisFrame)
            {
                SendText(debugText);
            }
#endif
        }

        /// <summary>
        /// 录音状态变化时，在现有对话气泡上常驻/撤下提示。
        /// 只操作显示层，不引入新的 UI 层级。
        /// </summary>
        private void UpdateRecordingIndicator()
        {
            if (voice == null)
                return;

            bool recording = voice.IsRecording;

            if (recording == wasRecording)
                return;

            wasRecording = recording;

            if (recording)
                DialogueBubble.Instance?.ShowSticky(recordingHint);
            else
                DialogueBubble.Instance?.Hide();
        }

        /// <summary>
        /// 文本入口：调试按键、以后可能的虚拟键盘都可以调用。
        /// </summary>
        public void SendText(string text)
        {
            if (npc == null)
            {
                Debug.LogError(
                    "VoiceChatController: NPCChatController is not assigned.",
                    this
                );

                return;
            }

            if (string.IsNullOrWhiteSpace(text))
                return;

            if (npc.IsBusy)
            {
                Debug.LogWarning(
                    "VoiceChatController: the NPC is still handling the previous message."
                );

                return;
            }

            StartCoroutine(SendRoutine(text));
        }

        private IEnumerator SendRoutine(string text)
        {
            LLMResult result = null;

            yield return npc.SendMessageAsync(
                text,
                response => result = response
            );

            if (result == null)
            {
                Debug.LogError(
                    "VoiceChatController: the NPC returned no result."
                );

                yield break;
            }

            if (result.Success)
            {
                Debug.Log(
                    $"[Chat] {npc.CharacterName}: {result.Content}"
                );
            }
            else
            {
                Debug.LogError(
                    $"[Chat] Request failed with HTTP {result.StatusCode}: " +
                    result.Error
                );
            }
        }

        private void HandleFinalTranscript(string text)
        {
            SendText(text);
        }

        private static void HandleVoiceStatus(string status)
        {
            Debug.Log("[VoiceChat] " + status);
        }
    }
}
