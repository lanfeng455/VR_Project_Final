using UnityEngine;

namespace Game.NPC
{
    [CreateAssetMenu(
        fileName = "New NPC Personality",
        menuName = "Game/NPC/Personality"
    )]
    public sealed class NPCPersonality : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string characterName = "NPC";
        [SerializeField, TextArea(3, 6)]
        private string background = "";

        [Header("Personality")]
        [SerializeField, TextArea(3, 6)]
        private string personality = "";

        [SerializeField, TextArea(3, 6)]
        private string speakingStyle = "";

        [Header("Boundaries")]
        [SerializeField, TextArea(3, 6)]
        private string behaviorRules = "";

        public string CharacterName => characterName;

        public string BuildSystemPrompt()
        {
            return
                $"你正在扮演游戏角色「{characterName}」。\n\n" +
                $"【角色背景】\n{background}\n\n" +
                $"【性格特点】\n{personality}\n\n" +
                $"【说话风格】\n{speakingStyle}\n\n" +
                $"【行为约束】\n{behaviorRules}\n\n" +
                "请始终以该角色的身份与玩家对话。" +
                "不要擅自编造尚未设定的重要背景事实。" +
                "除非玩家明确要求，否则不要跳出角色进行解释。";
        }
    }
}