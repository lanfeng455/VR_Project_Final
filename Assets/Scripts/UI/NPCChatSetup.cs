using UnityEngine;
using Game.LLM;

namespace Game.NPC
{
    public sealed class NPCChatSetup : MonoBehaviour
    {
        [SerializeField] private NPCChatController npc;

        [Header("DeepSeek")]
        [SerializeField] private string apiKey = "";
        [SerializeField] private string model = "deepseek-flash";

        private void Awake()
        {
            if (npc == null)
            {
                Debug.LogError(
                    "NPCChatSetup：未配置 NPCChatController。",
                    this
                );

                return;
            }

            ILLMProvider provider = new DeepSeekProvider(
                apiKey,
                model
            );

            npc.Initialize(provider);
        }
    }
}