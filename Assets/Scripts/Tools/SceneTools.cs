using System.Text;
using Game.Interaction;
using Game.UI;

namespace Game.Tools
{
    /// <summary>把电视切到 LUNA LOG 界面。</summary>
    public sealed class OpenLogTool : ILLMTool
    {
        public string Name => "open_log";

        public string Description =>
            "Switch the TV screen to the LUNA LOG view, which lists the " +
            "questions the player has already asked. Call this when the " +
            "player says \"log\" or asks to review their question history.";

        public string ParametersJson =>
            "{\"type\":\"object\",\"properties\":{}}";

        public bool ChangesScene => false;

        public bool IsAvailable(out string reason)
        {
            reason = TVScreenUI.Instance == null
                ? "The TV screen is not present in the scene."
                : null;

            return reason == null;
        }

        public ToolExecutionResult Execute(string argumentsJson)
        {
            TVScreenUI screen = TVScreenUI.Instance;

            if (screen == null)
                return ToolExecutionResult.Fail("The TV screen is unavailable.");

            screen.ShowLog();
            return ToolExecutionResult.Ok("The log screen is now open.");
        }
    }

    /// <summary>
    /// 揭示一条线索：只接受 SceneClueController 里登记过的 clueId，
    /// Schema 里直接用 enum 白名单约束模型。
    /// </summary>
    public sealed class RevealClueTool : ILLMTool
    {
        private readonly SceneClueController clues;
        private readonly string parametersJson;

        public RevealClueTool(SceneClueController clues)
        {
            this.clues = clues;
            parametersJson = BuildSchema(clues);
        }

        public string Name => "reveal_clue";

        public string Description =>
            "Reveal one clue in the scene. Use it only when the player's " +
            "question makes that clue accessible. This changes the scene, " +
            "so it counts as a key question.";

        public string ParametersJson => parametersJson;

        public bool ChangesScene => true;

        public bool IsAvailable(out string reason)
        {
            if (clues == null)
            {
                reason = "No clue controller is configured.";
                return false;
            }

            if (clues.ClueIds.Count == 0)
            {
                reason = "No clues are registered.";
                return false;
            }

            reason = null;
            return true;
        }

        public ToolExecutionResult Execute(string argumentsJson)
        {
            if (!ToolArguments.TryGetString(
                    argumentsJson,
                    "clueId",
                    out string clueId))
            {
                return ToolExecutionResult.Fail(
                    "Missing required argument 'clueId'.");
            }

            return clues.TryReveal(clueId, out string message)
                ? ToolExecutionResult.Ok(message)
                : ToolExecutionResult.Fail(message);
        }

        private static string BuildSchema(SceneClueController clues)
        {
            var builder = new StringBuilder();
            builder.Append("{\"type\":\"object\",\"properties\":{\"clueId\":");
            builder.Append("{\"type\":\"string\",\"enum\":[");

            if (clues != null)
            {
                var ids = clues.ClueIds;

                for (int i = 0; i < ids.Count; i++)
                {
                    if (i > 0)
                        builder.Append(',');

                    builder.Append('"').Append(ids[i]).Append('"');
                }
            }

            builder.Append("]}},\"required\":[\"clueId\"]}");
            return builder.ToString();
        }
    }
}
