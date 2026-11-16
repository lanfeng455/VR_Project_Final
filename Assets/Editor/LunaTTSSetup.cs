using System.IO;
using Game.NPC;
using Game.TTS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    /// <summary>
    /// 一次性把 LUNA 的语音节点装配进当前场景并保存。
    /// 只创建 AudioSource 与控制器，不触碰电视界面、STT 与工具调用。
    /// </summary>
    public static class LunaTTSSetup
    {
        private const string MenuPath =
            "Tools/VR Project/LUNA/Add TTS Controller";

        private const string RootName = "LUNA TTS";
        private const string TvObjectName = "TV";

        [MenuItem(MenuPath)]
        public static void Add()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "LUNA TTS",
                    "Exit Play mode before adding the TTS controller.",
                    "OK"
                );

                return;
            }

            LunaTTSController existing =
                Object.FindFirstObjectByType<LunaTTSController>(
                    FindObjectsInactive.Include
                );

            if (existing != null)
            {
                Selection.activeObject = existing;

                EditorUtility.DisplayDialog(
                    "LUNA TTS",
                    "A LunaTTSController already exists in this scene:\n" +
                    existing.gameObject.name +
                    "\n\nNothing was rebuilt.",
                    "OK"
                );

                return;
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Add LUNA TTS");

            AudioSource tvSource = CreateSource(
                root.transform,
                "TV Speaker",
                1f
            );

            GameObject tv = GameObject.Find(TvObjectName);

            if (tv != null)
            {
                tvSource.transform.position = tv.transform.position;
            }
            else
            {
                Debug.LogWarning(
                    "[LUNA TTS] No GameObject named '" + TvObjectName +
                    "' found. Move 'TV Speaker' next to the TV manually."
                );
            }

            AudioSource nearEarSource = CreateSource(
                root.transform,
                "Near Ear Speaker",
                0f
            );

            LunaTTSController controller =
                root.AddComponent<LunaTTSController>();

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("tvSource").objectReferenceValue =
                tvSource;
            serialized.FindProperty("nearEarSource").objectReferenceValue =
                nearEarSource;

            NPCChatController npc =
                Object.FindFirstObjectByType<NPCChatController>(
                    FindObjectsInactive.Include
                );

            if (npc != null)
            {
                serialized.FindProperty("npc").objectReferenceValue = npc;
            }
            else
            {
                Debug.LogWarning(
                    "[LUNA TTS] No NPCChatController found; THINKING " +
                    "during LLM requests will not be driven by events."
                );
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = root;

            Debug.Log(
                "[LUNA TTS] Added '" + RootName + "' with TV (3D) and " +
                "Near Ear (2D) AudioSources. Default route: TV position."
            );
        }

        private const string ClearCacheMenuPath =
            "Tools/VR Project/LUNA/Clear TTS Cache";

        /// <summary>
        /// 一次性清空磁盘语音缓存。
        /// 运行时也会自动丢弃无法识别 / 无法解码的坏条目，
        /// 这个菜单只是给编辑器里排查问题时用的手动手段。
        /// </summary>
        [MenuItem(ClearCacheMenuPath)]
        public static void ClearCache()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "LUNA TTS",
                    "Exit Play mode before clearing the cache.",
                    "OK"
                );

                return;
            }

            string directory = Path.Combine(
                Application.persistentDataPath,
                "tts-cache"
            );

            if (!Directory.Exists(directory))
            {
                EditorUtility.DisplayDialog(
                    "LUNA TTS",
                    "No cache directory yet:\n" + directory,
                    "OK"
                );

                return;
            }

            var cache = new TTSCache(
                directory,
                64L * 1024L * 1024L
            );

            int removed = cache.ClearAll();

            Debug.Log(
                "[LUNA TTS] Cleared " + removed + " cached clip(s) from " +
                directory
            );

            EditorUtility.DisplayDialog(
                "LUNA TTS",
                "Removed " + removed + " cached clip(s).\n" + directory,
                "OK"
            );
        }

        private static AudioSource CreateSource(
            Transform parent,
            string name,
            float spatialBlend)
        {
            var target = new GameObject(name);
            target.transform.SetParent(parent, false);

            AudioSource source = target.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = spatialBlend;
            source.dopplerLevel = 0f;
            source.minDistance = 2f;
            source.maxDistance = 35f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.volume = 1f;

            return source;
        }
    }
}
