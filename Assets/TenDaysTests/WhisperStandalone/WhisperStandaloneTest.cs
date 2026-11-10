using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Whisper;
using Whisper.Utils;

namespace TenDays.Tests
{
    public sealed class WhisperStandaloneTest : MonoBehaviour
    {
        public WhisperManager whisper;
        public MicrophoneRecord recorder;
        public AudioClip sampleAudio;
        public TMP_Text statusText, outputText, deviceText, recordLabel;
        public Button sampleButton, recordButton, deviceButton;
        bool busy, ready;
        int deviceIndex = -1;
        string[] devices = Array.Empty<string>();

        async void Start()
        {
            sampleButton.onClick.AddListener(RunSample);
            recordButton.onClick.AddListener(ToggleRecording);
            deviceButton.onClick.AddListener(CycleDevice);
            recorder.OnRecordStop += OnRecordStop;
            RefreshDevices();
            SetBusy(true); SetStatus("正在加载 Whisper 模型……");
            try
            {
                await whisper.InitModel();
                if (!this) return;
                ready = whisper.IsLoaded;
                SetStatus(ready ? "模型已就绪。先点“测试示例音频”，或按空格录音。" : "模型加载失败，请检查 Console。");
            }
            catch(Exception e) { SetStatus("模型加载失败：" + e.Message); }
            finally { if(this) SetBusy(false); }
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                ToggleRecording();
        }

        public void ToggleRecording()
        {
            if(busy || !ready) return;
            if(recorder.IsRecording) { recorder.StopRecord(); return; }
            SetBusy(true); StartCoroutine(StartWithPermission());
        }

        IEnumerator StartWithPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                bool finished=false;
                var callbacks=new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => finished=true;
                callbacks.PermissionDenied += _ => finished=true;
                callbacks.PermissionDeniedAndDontAskAgain += _ => finished=true;
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone,callbacks);
                yield return new WaitUntil(()=>finished);
                if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                { SetStatus("没有麦克风权限，请在系统设置中允许。"); SetBusy(false); yield break; }
            }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if(!Application.HasUserAuthorization(UserAuthorization.Microphone))
                yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            if(!Application.HasUserAuthorization(UserAuthorization.Microphone))
            { SetStatus("没有麦克风权限，请在 macOS 隐私设置中允许 Unity。"); SetBusy(false); yield break; }
#endif
            RefreshDevices();
            if(devices.Length == 0) { SetStatus("未检测到麦克风，请检查系统声音输入。"); SetBusy(false); yield break; }
            try
            {
                recorder.StartRecord();
                outputText.text="";
                SetStatus("录音中：请说一句中文，再按空格停止（最长 20 秒）。");
                recordLabel.text="停止录音（空格）";
            }
            catch(Exception e) { SetStatus("无法录音："+e.Message); }
            SetBusy(false);
        }

        void RefreshDevices()
        {
            devices=Microphone.devices;
            if(deviceIndex>=devices.Length)deviceIndex=-1;
            recorder.SelectedMicDevice=deviceIndex<0?null:devices[deviceIndex];
            deviceText.text="麦克风："+(deviceIndex<0?"系统默认":devices[deviceIndex])+"  ·  检测到 "+devices.Length+" 个设备";
        }

        void CycleDevice()
        {
            if(busy||recorder.IsRecording)return;
            devices=Microphone.devices;
            deviceIndex=deviceIndex+1>=devices.Length?-1:deviceIndex+1;
            RefreshDevices();
        }

        async void OnRecordStop(AudioChunk chunk)
        {
            recordLabel.text="开始录音（空格）";
            if(chunk.Data==null||chunk.Data.Length<chunk.Frequency*chunk.Channels*0.3f)
            { SetStatus("录音太短，请说完一句话后再停止。"); SetBusy(false); return; }
            double energy=0; foreach(var sample in chunk.Data)energy+=sample*sample;
            if(Math.Sqrt(energy/chunk.Data.Length)<0.0001)
            { SetStatus("几乎没有录到声音，请检查麦克风、权限或输入设备。"); SetBusy(false); return; }
            await Transcribe(chunk.Data,chunk.Frequency,chunk.Channels,"zh","麦克风");
        }

        public async void RunSample()
        {
            if(busy||!ready||recorder.IsRecording)return;
            if(!sampleAudio){SetStatus("没有配置示例音频。");return;}
            var samples=new float[sampleAudio.samples*sampleAudio.channels];
            if(!sampleAudio.GetData(samples,0)){SetStatus("无法读取示例音频。");return;}
            await Transcribe(samples,sampleAudio.frequency,sampleAudio.channels,"en","英文示例");
        }

        async Task Transcribe(float[] samples,int frequency,int channels,string language,string source)
        {
            SetBusy(true);outputText.text="";SetStatus("Whisper 正在识别："+source+"……");
            var watch=Stopwatch.StartNew();
            try
            {
                whisper.language=language;whisper.translateToEnglish=false;
                var result=await whisper.GetTextAsync(samples,frequency,channels);
                if(!this)return;
                var text=result?.Result?.Trim();
                outputText.text=string.IsNullOrWhiteSpace(text)?"（未识别出文字）":text;
                SetStatus("识别完成 · "+source+" · 耗时 "+watch.Elapsed.TotalSeconds.ToString("F1")+" 秒 · 未发送给 LLM");
                UnityEngine.Debug.Log("[Whisper standalone transcript] "+text);
            }
            catch(Exception e){SetStatus("识别失败："+e.Message);}
            finally{if(this){whisper.language="zh";SetBusy(false);}}
        }

        void SetStatus(string text){if(statusText)statusText.text=text;}
        void SetBusy(bool value)
        {
            busy=value;
            if(sampleButton)sampleButton.interactable=ready&&!busy&&!recorder.IsRecording;
            if(recordButton)recordButton.interactable=ready&&!busy;
            if(deviceButton)deviceButton.interactable=!busy&&!recorder.IsRecording;
        }
        void OnDestroy()
        {
            if(recorder){recorder.OnRecordStop-=OnRecordStop;if(recorder.IsRecording)recorder.StopRecord();}
        }
    }
}
