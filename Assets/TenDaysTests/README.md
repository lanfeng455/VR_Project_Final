# Whisper 导入与接入测试

Whisper 1.4.0 已作为本地 embedded UPM 包安装，不再依赖 Unity 使用 GitHub Git URL 下载。

来源：https://github.com/Macoron/whisper.unity
包目录：Packages/com.whisper.unity
模型：Assets/StreamingAssets/Whisper/ggml-tiny.bin（多语言 tiny，约 78 MB）
麦克风示例：Assets/TenDaysTests/WhisperMicrophone/2 - Microphone.unity（language = zh，未开启翻译）

已验证：Unity 解析本地包成功；本机原生库和多语言模型加载成功；键盘测试场景 UI 引用完整、无丢失脚本。
已追加验证：Whisper 对作者示例音频的实际转写成功。
尚未验证：用户麦克风录音、中文识别准确率、Quest 真机、DeepSeek 在线回复。测试没有自动录音或发送 LLM 请求。

## 当前优先测试：Whisper

打开 Assets/TenDaysTests/WhisperStandalone/Whisper_Standalone_Test.unity。
点击 Play 后，先点“测试示例音频”验证离线识别；中文录音使用空格键开始、再次按空格停止。点击 Game 窗口后键盘快捷键才会生效。
示例音频已实际转写成功；用户麦克风和 Quest 真机尚未验证。该场景不连接 LLM。

## 另行测试：键盘验证 LLM

1. 打开 Assets/TenDaysTests/LLM_Keyboard_Test.unity。
2. 点击 Play，然后在 Game 窗口输入“你好，我叫小明，请记住我的名字”，点击发送。
3. 收到回复后输入“我叫什么名字？”，检查多轮对话是否保留。
4. 若请求失败，查看聊天中的错误及 Console。检查 NPCChatSetup 的 API Key、Model 和网络。新测试场景的 API Key 已留空，请在本机填写后测试，勿将真实密钥提交到 Git。
5. 测试结束后退出 Play 模式。

这是队友原聊天场景的独立副本，补齐了测试用 NPCPersonality，并禁用了 NPCChatTestBootstrap 和 LLMProviderTest，保留 NPCChatSetup 作为唯一初始化入口。
此场景只做普通文字聊天连接验证，没有配置《十天》的汤底与推理节点。键入测试验证 LLM，不会验证 Whisper 的语音识别。

## 同学后续接入位置

目标：Quest 3 手柄按键录音 → Whisper 最终识别文字 → 现有 ChatUI → NPCChatController → DeepSeek → UI 回复。

可从 MicrophoneRecord.OnRecordStop(AudioChunk) 取得录音，再调用：

    var result = await whisper.GetTextAsync(chunk.Data, chunk.Frequency, chunk.Channels);

最终文字为 result.Result。确认非空、未重复且不在上一轮请求中后，在 Unity 主线程中填入现有 TMP_InputField 并调用 ChatUI.SendMessage()。
只提交完整最终结果，避免将 OnNewSegment 的每个片段分别发给 LLM。

先验证电脑麦克风，再在 Windows Quest Link / Air Link 中选择 Quest 麦克风验证；Mac 上的官方 Quest 真机测试应走 Android Build and Run。
同时检查录音权限、录音停止、无声音、空文本、连续提问、识别及网络失败时的状态提示。

## 同步给队友的文件

包括 Packages/com.whisper.unity、Packages/manifest.json、Packages/packages-lock.json、Assets/StreamingAssets/Whisper、Assets/TenDaysTests 及对应 .meta 文件。
本次没有修改原有玩法脚本、原聊天场景、主场景或 ProjectSettings；没有自动提交、推送或发送给其他人。
