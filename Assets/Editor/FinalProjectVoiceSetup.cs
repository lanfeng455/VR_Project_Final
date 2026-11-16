using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Dev;
using Game.NPC;
using Game.Voice;
using Whisper;
using Whisper.Utils;

namespace Game.EditorTools
{
    /// <summary>
    /// 一键把 LLM 对话与 Whisper 语音节点装配进 FinalProject。
    /// 也可以通过 Tools/VR Project/Setup FinalProject Voice Chat 手动执行。
    /// </summary>
    [InitializeOnLoad]
    public static class FinalProjectVoiceSetup
    {
        private const string FinalProjectPath =
            "Assets/Scenes/FinalProject.unity";

        private const string LlmScenePath =
            "Assets/Xinyu HONG/Scene/接入大模型测试.unity";

        private const string PersonalityPath =
            "Assets/NPC/StoryHostPersonality.asset";

        private const string MarkerRelativePath =
            "Temp/voice_setup_requested";

        [MenuItem("Tools/VR Project/Setup FinalProject Voice Chat")]
        public static void SetupFromMenu()
        {
            Setup();
        }

        private static bool attempted;

        static FinalProjectVoiceSetup()
        {
            EditorApplication.update += AutoSetupIfRequested;
        }

        private static void AutoSetupIfRequested()
        {
            if (attempted)
                return;

            string marker = Path.Combine(
                ProjectRoot,
                MarkerRelativePath
            );

            if (!File.Exists(marker))
                return;

            // 编译中、导入中、Play 模式中都先等一等。
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                return;
            }

            attempted = true;

            try
            {
                Setup();
                File.Delete(marker);
                Debug.Log("[VoiceSetup] 自动装配完成。");
            }
            catch (Exception e)
            {
                Debug.LogError("[VoiceSetup] 自动装配失败：" + e);
            }
        }

        private static string ProjectRoot =>
            Path.GetDirectoryName(Application.dataPath);

        public static void Setup()
        {
            Scene scene = SceneManager.GetSceneByPath(FinalProjectPath);
            bool openedAdditive = false;

            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(
                    FinalProjectPath,
                    OpenSceneMode.Additive
                );

                openedAdditive = true;
            }

            try
            {
                NPCPersonality personality = EnsurePersonality();

                GameObject root = FindRoot(scene, "VoiceChat");

                if (root == null)
                {
                    root = new GameObject("VoiceChat");
                    SceneManager.MoveGameObjectToScene(root, scene);
                }

                WhisperManager whisper =
                    GetOrAdd<WhisperManager>(root);
                ConfigureWhisper(whisper);

                MicrophoneRecord microphone =
                    GetOrAdd<MicrophoneRecord>(root);
                ConfigureMicrophone(microphone);

                WhisperVoiceInput voiceInput =
                    GetOrAdd<WhisperVoiceInput>(root);
                ConfigureVoiceInput(
                    voiceInput,
                    whisper,
                    microphone
                );

                NPCChatController npc =
                    GetOrAdd<NPCChatController>(root);
                AssignPersonality(npc, personality);

                NPCChatSetup setup = GetOrAdd<NPCChatSetup>(root);
                ConfigureSetup(setup, npc);

                VoiceChatController chat =
                    GetOrAdd<VoiceChatController>(root);
                ConfigureChat(chat, voiceInput, npc);

                DisableDialogueTester(scene);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                AssignPersonalityInLlmScene(personality);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(
                    "[VoiceSetup] FinalProject 已装配 VoiceChat 节点。"
                );

                CheckLlmConfig();
            }
            finally
            {
                if (openedAdditive)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static NPCPersonality EnsurePersonality()
        {
            NPCPersonality personality =
                AssetDatabase.LoadAssetAtPath<NPCPersonality>(
                    PersonalityPath
                );

            if (personality != null)
                return personality;

            if (!AssetDatabase.IsValidFolder("Assets/NPC"))
                AssetDatabase.CreateFolder("Assets", "NPC");

            personality =
                ScriptableObject.CreateInstance<NPCPersonality>();

            AssetDatabase.CreateAsset(personality, PersonalityPath);

            var serialized = new SerializedObject(personality);
            serialized.FindProperty("characterName").stringValue =
                "Story Host";
            serialized.FindProperty("background").stringValue =
                "You are the host of a lateral-thinking riddle game. " +
                "The player asks yes-or-no questions to reconstruct a hidden story.";
            serialized.FindProperty("personality").stringValue =
                "Calm, patient, and completely terse. " +
                "You never converse; you only judge questions.";
            serialized.FindProperty("speakingStyle").stringValue =
                "Your entire reply is exactly one of these three tokens: " +
                "YES, NO, IRRELEVANT. Uppercase only, one single word, " +
                "no punctuation, no explanation, no extra words.";
            serialized.FindProperty("behaviorRules").stringValue =
                "Judge every question strictly against the hidden truth. " +
                "Reply YES only when the question is true under that truth. " +
                "Reply NO only when it is false under that truth. " +
                "Reply IRRELEVANT when the question cannot be answered from that truth. " +
                "Never reveal, hint at, or explain the truth. " +
                "Output nothing except the single token.";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(personality);
            AssetDatabase.SaveAssets();

            return personality;
        }

        private static void ConfigureWhisper(WhisperManager whisper)
        {
            var serialized = new SerializedObject(whisper);
            serialized.FindProperty("logLevel").intValue = 1;
            serialized.FindProperty("modelPath").stringValue =
                "Whisper/ggml-tiny.bin";
            serialized
                .FindProperty("isModelPathInStreamingAssets")
                .boolValue = true;
            serialized.FindProperty("initOnAwake").boolValue = false;
            serialized.FindProperty("useGpu").boolValue = false;
            serialized.FindProperty("flashAttention").boolValue = false;
            serialized.FindProperty("language").stringValue = "en";
            serialized.FindProperty("translateToEnglish").boolValue =
                false;
            serialized.FindProperty("strategy").intValue = 0;
            serialized.FindProperty("noContext").boolValue = true;
            serialized.FindProperty("singleSegment").boolValue = false;
            serialized.FindProperty("enableTokens").boolValue = false;
            serialized.FindProperty("initialPrompt").stringValue = "";
            serialized.FindProperty("stepSec").floatValue = 3f;
            serialized.FindProperty("keepSec").floatValue = 0.2f;
            serialized.FindProperty("lengthSec").floatValue = 10f;
            serialized.FindProperty("updatePrompt").boolValue = true;
            serialized.FindProperty("dropOldBuffer").boolValue = false;
            serialized.FindProperty("useVad").boolValue = true;
            serialized.FindProperty("tokensTimestamps").boolValue =
                false;
            serialized.FindProperty("audioCtx").intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureMicrophone(
            MicrophoneRecord microphone)
        {
            microphone.maxLengthSec = 20;
            microphone.loop = false;
            microphone.frequency = 16000;
            microphone.chunksLengthSec = 0.5f;
            microphone.echo = false;
            microphone.useVad = false;
            microphone.vadStop = false;
            microphone.dropVadPart = true;
        }

        private static void ConfigureVoiceInput(
            WhisperVoiceInput input,
            WhisperManager whisper,
            MicrophoneRecord microphone)
        {
            var serialized = new SerializedObject(input);
            serialized.FindProperty("whisper").objectReferenceValue =
                whisper;
            serialized.FindProperty("recorder").objectReferenceValue =
                microphone;
            serialized.FindProperty("language").stringValue = "en";
            serialized
                .FindProperty("minRecordingSeconds")
                .floatValue = 0.3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureSetup(
            NPCChatSetup setup,
            NPCChatController npc)
        {
            var serialized = new SerializedObject(setup);
            serialized.FindProperty("npc").objectReferenceValue = npc;
            serialized
                .FindProperty("configResourcePath")
                .stringValue = "LLM/llm-config";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureChat(
            VoiceChatController chat,
            WhisperVoiceInput voice,
            NPCChatController npc)
        {
            var serialized = new SerializedObject(chat);
            serialized.FindProperty("voice").objectReferenceValue =
                voice;
            serialized.FindProperty("npc").objectReferenceValue = npc;
            serialized
                .FindProperty("enableControllerToggle")
                .boolValue = true;
            // OVRInput.Button.One = 右手 A 键。
            serialized.FindProperty("recordButton").intValue = 1;
            // OVRInput.Controller.RTouch。
            serialized.FindProperty("recordController").intValue = 2;
            serialized.FindProperty("enableDebugTextKey").boolValue =
                true;
            serialized.FindProperty("debugText").stringValue =
                "你好，请用一句话介绍你自己。";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignPersonality(
            NPCChatController npc,
            NPCPersonality personality)
        {
            var serialized = new SerializedObject(npc);
            serialized.FindProperty("personality").objectReferenceValue =
                personality;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void DisableDialogueTester(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                DialogueTester[] testers =
                    root.GetComponentsInChildren<DialogueTester>(true);

                foreach (DialogueTester tester in testers)
                {
                    tester.gameObject.SetActive(false);
                    Debug.Log(
                        "[VoiceSetup] 已禁用测试对话器：" +
                        tester.gameObject.name
                    );
                }
            }
        }

        private static void AssignPersonalityInLlmScene(
            NPCPersonality personality)
        {
            Scene scene = SceneManager.GetSceneByPath(LlmScenePath);

            if (!scene.IsValid() || !scene.isLoaded)
                return;

            if (scene.isDirty)
            {
                Debug.LogWarning(
                    "[VoiceSetup] LLM 场景有未保存修改，" +
                    "跳过人格引用修复。"
                );

                return;
            }

            NPCChatController npc = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                npc = root.GetComponentInChildren<NPCChatController>(
                    true
                );

                if (npc != null)
                    break;
            }

            if (npc == null)
                return;

            AssignPersonality(npc, personality);

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                NPCChatTestBootstrap[] bootstraps =
                    root.GetComponentsInChildren<NPCChatTestBootstrap>(
                        true
                    );

                foreach (NPCChatTestBootstrap bootstrap in bootstraps)
                    bootstrap.enabled = false;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                "[VoiceSetup] 已修复 LLM 场景人格引用并禁用旧 bootstrap。"
            );
        }

        private static void CheckLlmConfig()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Resources/LLM/llm-config.json"
            );

            if (!File.Exists(path))
            {
                Debug.LogWarning(
                    "[VoiceSetup] 未找到 " + path +
                    "，请先把 LLM 配置放进 Resources/LLM/。"
                );
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }

            return null;
        }

        private static T GetOrAdd<T>(GameObject target)
            where T : Component
        {
            T component = target.GetComponent<T>();

            if (component == null)
                component = target.AddComponent<T>();

            return component;
        }
    }
}
