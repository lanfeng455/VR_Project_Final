# Whisper 独立测试

打开 Whisper_Standalone_Test.unity，点击 Play，然后点击 Game 窗口使其获得键盘焦点。

- 测试示例音频：转写项目自带的英文 JFK 片段，不需要麦克风。
- 空格键或录音按钮：第一次开始录音，说一句中文，第二次停止，等待屏幕出现文字。
- 切换麦克风：循环选择系统默认和 Unity 检测到的设备。
- 首次使用录音需要允许 Unity 访问麦克风；本场景不会自动开始录音。
- 使用多语言 tiny 模型，麦克风固定中文，示例音频固定英文，不启用翻译。

已在本机实际运行示例音频识别并成功输出文本，结果保存为 SampleTranscriptionResult.json。
尚未验证用户真实麦克风、中文识别准确率和 Quest 真机。本场景不包含 LLM 组件，不发送任何模型 API 请求。

同学后续可将 WhisperStandaloneTest 中 Transcribe 得到的 result.Result 接入现有 ChatUI / NPCChatController，再接 Quest 手柄录音按钮并验证头显麦克风。
