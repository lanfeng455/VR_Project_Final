using System;
using System.Collections;
using UnityEngine;
using Whisper;
using Whisper.Utils;

namespace Game.Voice
{
    /// <summary>
    /// 只负责录音与语音识别，不依赖 LLM 和具体 UI。
    /// 识别完成后通过 FinalTranscript 事件把最终文本交给上层。
    /// </summary>
    public sealed class WhisperVoiceInput : MonoBehaviour
    {
        [Header("Whisper")]
        [SerializeField] private WhisperManager whisper;
        [SerializeField] private MicrophoneRecord recorder;

        [Header("识别")]
        [SerializeField] private string language = "en";
        [Tooltip("短于该时长的录音直接丢弃。")]
        [SerializeField] private float minRecordingSeconds = 0.3f;

        public event Action<string> FinalTranscript;
        public event Action<string> StatusChanged;

        public bool IsReady { get; private set; }
        public bool IsBusy { get; private set; }
        public bool IsRecording =>
            recorder != null && recorder.IsRecording;

        private bool isStarting;

        private async void Start()
        {
            if (whisper == null || recorder == null)
            {
                Debug.LogError(
                    "WhisperVoiceInput: WhisperManager or MicrophoneRecord is not assigned.",
                    this
                );

                return;
            }

            SetStatus("Loading Whisper model…");

            try
            {
                await whisper.InitModel();

                if (this == null)
                    return;

                IsReady = whisper.IsLoaded;
                SetStatus(
                    IsReady
                        ? "Voice ready. Press A to start or stop recording."
                        : "Failed to load the Whisper model. Check the Console."
                );
            }
            catch (Exception e)
            {
                SetStatus("Whisper initialization failed: " + e.Message);
            }
        }

        private void OnEnable()
        {
            if (recorder == null)
                return;

            recorder.OnRecordStop -= HandleRecordStop;
            recorder.OnRecordStop += HandleRecordStop;
        }

        private void OnDisable()
        {
            if (recorder != null)
                recorder.OnRecordStop -= HandleRecordStop;
        }

        /// <summary>
        /// 手柄按键调用的唯一入口：第一次开始，第二次结束。
        /// </summary>
        public void ToggleRecording()
        {
            if (IsBusy || isStarting)
                return;

            if (!IsReady)
            {
                SetStatus("Voice module is not ready yet.");
                return;
            }

            if (IsRecording)
            {
                recorder.StopRecord();
                SetStatus("Recording stopped. Transcribing…");
                return;
            }

            StartCoroutine(StartRecordingWithPermission());
        }

        private IEnumerator StartRecordingWithPermission()
        {
            isStarting = true;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
            {
                bool finished = false;

                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => finished = true;
                callbacks.PermissionDenied += _ => finished = true;
                callbacks.PermissionDeniedAndDontAskAgain += _ => finished = true;

                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone,
                    callbacks
                );

                yield return new WaitUntil(() => finished);

                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                        UnityEngine.Android.Permission.Microphone))
                {
                    SetStatus("Microphone permission denied. Allow it in system settings.");
                    isStarting = false;
                    yield break;
                }
            }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (!Application.HasUserAuthorization(
                    UserAuthorization.Microphone))
            {
                yield return Application.RequestUserAuthorization(
                    UserAuthorization.Microphone
                );
            }

            if (!Application.HasUserAuthorization(
                    UserAuthorization.Microphone))
            {
                SetStatus("Microphone permission denied. Allow Unity in macOS privacy settings.");
                isStarting = false;
                yield break;
            }
#endif

            if (Microphone.devices.Length == 0)
            {
                SetStatus("No microphone detected. Check the system audio input.");
                isStarting = false;
                yield break;
            }

            try
            {
                recorder.StartRecord();
                SetStatus("Recording. Press A again to stop.");
            }
            catch (Exception e)
            {
                SetStatus("Unable to start recording: " + e.Message);
            }

            isStarting = false;
        }

        private async void HandleRecordStop(AudioChunk chunk)
        {
            if (IsBusy)
                return;

            if (chunk.Data == null ||
                chunk.Data.Length <
                chunk.Frequency * chunk.Channels * minRecordingSeconds)
            {
                SetStatus("Recording too short. Finish your sentence before stopping.");
                return;
            }

            double energy = 0;

            foreach (float sample in chunk.Data)
                energy += sample * sample;

            if (Math.Sqrt(energy / chunk.Data.Length) < 0.0001)
            {
                SetStatus("Almost no audio captured. Check the microphone.");
                return;
            }

            IsBusy = true;
            SetStatus("Whisper is transcribing…");

            try
            {
                whisper.language = language;
                whisper.translateToEnglish = false;

                var result = await whisper.GetTextAsync(
                    chunk.Data,
                    chunk.Frequency,
                    chunk.Channels
                );

                if (this == null)
                    return;

                string text = result?.Result?.Trim();

                if (string.IsNullOrWhiteSpace(text))
                {
                    SetStatus("No speech recognized. Please try again.");
                    return;
                }

                SetStatus("Transcript: " + text);
                FinalTranscript?.Invoke(text);
            }
            catch (Exception e)
            {
                SetStatus("Recognition failed: " + e.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnDestroy()
        {
            if (recorder == null)
                return;

            recorder.OnRecordStop -= HandleRecordStop;

            if (recorder.IsRecording)
                recorder.StopRecord();
        }

        private void SetStatus(string status)
        {
            StatusChanged?.Invoke(status);
            Debug.Log("[Voice] " + status);
        }
    }
}
