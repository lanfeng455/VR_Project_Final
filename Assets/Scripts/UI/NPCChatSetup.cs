using UnityEngine;
using Game.Interaction;
using Game.LLM;
using Game.Tools;

namespace Game.NPC
{
    public sealed class NPCChatSetup : MonoBehaviour
    {
        [SerializeField] private NPCChatController npc;

        [Header("LLM 配置")]
        [Tooltip("Resources 下的配置路径，不含 .json 扩展名。")]
        [SerializeField] private string configResourcePath = "LLM/llm-config";

        [Header("场景工具")]
        [Tooltip("线索白名单来源；留空则在运行时自动添加。")]
        [SerializeField] private SceneClueController clueController;

        private void Awake()
        {
            if (npc == null)
            {
                Debug.LogError(
                    "NPCChatSetup: NPCChatController is not assigned.",
                    this
                );

                return;
            }

            if (!LLMConfig.TryLoad(
                    configResourcePath,
                    out LLMConfig.Data config,
                    out string error))
            {
                Debug.LogError(
                    "NPCChatSetup: " + error,
                    this
                );

                return;
            }

            ILLMProvider provider = new DeepSeekProvider(
                config.apiKey,
                config.model
            );

            npc.Initialize(provider);
            RegisterTools();
        }

        private void RegisterTools()
        {
            npc.Tools.Register(new OpenLogTool());

            if (clueController == null)
                clueController = GetComponent<SceneClueController>();

            if (clueController == null)
                clueController = FindFirstObjectByType<SceneClueController>();

            if (clueController == null)
                clueController = gameObject.AddComponent<SceneClueController>();

            npc.Tools.Register(new RevealClueTool(clueController));
        }
    }
}
