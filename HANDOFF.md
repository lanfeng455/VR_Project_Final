# 本次工程交接

导入 Whisper了可以测试语音转文字，但是没有连接到你做的LLM部分。可以继续验证Quest 3 麦克风和中文识别，再将最终识别文字接入现有 LLM

## 本次更新

- 导入 Wills Room 环境，并修复 URP 材质兼容问题。
- 导入 Whisper 1.4.0 本地包、多语言 tiny 模型、麦克风示例及独立测试场景。
- Whisper 示例音频已实际转写成功；用户录音、中文准确率和 Quest 3 真机仍需继续验证。
- 新增独立的键盘 LLM 测试副本；语音识别结果尚未连接现有 LLM。
- Unity 工程当前版本：6000.3.23f1。

## Whisper 测试

打开 Assets/TenDaysTests/WhisperStandalone/Whisper_Standalone_Test.unity，点击 Play 后点一下 Game 窗口。
先点“测试示例音频”验证识别；按空格开始录音，说一句中文，再按空格停止。首次录音需允许麦克风权限，可用按钮切换输入设备。
此场景不调用 LLM。

键盘聊天测试：Assets/TenDaysTests/LLM_Keyboard_Test.unity。新场景的 API Key 留空，需要本地配置。

## 同步说明

本次提交包含解包后的环境素材、Whisper 本地依赖、模型、测试场景、字体和 Unity 项目配置。
Library、Temp、Logs、UserSettings 及两份重复的 .unitypackage 安装档不参与同步；Unity 会重新生成缓存。
