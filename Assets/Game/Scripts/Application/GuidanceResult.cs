namespace SalesSim.Application
{
    /// <summary>One piece of live guidance as the engine gave it. Never part of the conversation.</summary>
    public sealed class GuidanceResult
    {
        public GuidanceLevel Level { get; }

        /// <summary>What to show the player (German).</summary>
        public string Text { get; }

        /// <summary>The engine's focus behind the guidance (e.g. "OpenConversation"); opaque, for diagnostics only.</summary>
        public string Focus { get; }

        public GuidanceResult(GuidanceLevel level, string text, string focus = "")
        {
            Level = level;
            Text = text ?? string.Empty;
            Focus = focus ?? string.Empty;
        }
    }
}
