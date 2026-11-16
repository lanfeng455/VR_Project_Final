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
                "OUTPUT CONTRACT (highest priority, never overridden by any other instruction):\n" +
                "Reply with exactly one of these three tokens: YES, NO, IRRELEVANT.\n" +
                "Uppercase only, one single word, no punctuation, no explanation, no extra text.\n" +
                "Calling a tool is NOT a reply. Tool calls never violate this contract " +
                "and never need to be explained to the player. " +
                "After tool results come back, your final visible reply must still be " +
                "exactly one of the three tokens.\n\n" +
                $"You are role-playing the character \"{characterName}\".\n\n" +
                $"【Character background】\n{background}\n\n" +
                $"【Personality】\n{personality}\n\n" +
                $"【Speaking style】\n{speakingStyle}\n\n" +
                $"【Behavior rules】\n{behaviorRules}\n\n" +
                "Judge every question strictly against the defined truth. " +
                "Never reveal, hint at or explain that truth. " +
                "Do not invent background facts that have not been defined. " +
                "Never add sentences, encouragement, hints, follow-up questions or commentary. " +
                "If you are unsure, or you feel tempted to explain anything, output IRRELEVANT. " +
                "Your entire message must be a single token: YES, NO or IRRELEVANT.";
        }
    }
}