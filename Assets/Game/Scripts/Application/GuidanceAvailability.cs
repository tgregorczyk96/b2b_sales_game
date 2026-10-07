namespace SalesSim.Application
{
    /// <summary>
    /// Whether a guidance level can be requested right now, as the engine decides it (difficulty rules, AI configured,
    /// conversation running). Unity only shows it; it never derives availability itself.
    /// </summary>
    public enum GuidanceAvailability
    {
        Available,

        /// <summary>No conversation has been started yet.</summary>
        NotStarted,

        /// <summary>The run's difficulty does not allow this level.</summary>
        NotAllowedByDifficulty,

        /// <summary>Example sentences need the AI, and none is configured.</summary>
        RequiresAi,

        /// <summary>The conversation has ended.</summary>
        ConversationOver,

        /// <summary>No engine is connected.</summary>
        NotConnected,
    }
}
