namespace SalesSim.Application
{
    /// <summary>
    /// How much live help the player asks for. Meaning and wording come from the engine's training coach; Unity only
    /// forwards the request and shows the text.
    /// </summary>
    public enum GuidanceLevel
    {
        /// <summary>Rough orientation for the next step, without wording. Deterministic, no AI.</summary>
        Hint,

        /// <summary>A concrete starting point for the next sentence. Deterministic, no AI.</summary>
        HintMore,

        /// <summary>One complete example sentence, phrased by the AI.</summary>
        Example,
    }
}
