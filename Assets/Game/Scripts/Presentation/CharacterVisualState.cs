namespace SalesSim.Presentation
{
    /// <summary>
    /// Purely visual states of the on-screen customer. Which state to show is derived from engine output
    /// (waiting for the engine, engine indicator changes), never from sales rules in Unity.
    /// </summary>
    public enum CharacterVisualState
    {
        Idle,
        Speaking,
        Thinking,
        Positive,
        VeryPositive,
        Negative,
        VeryNegative
    }
}
