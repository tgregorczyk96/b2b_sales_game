namespace SalesSim.Presentation
{
    /// <summary>Purely visual states of the on-screen character. Which state to show is decided by engine output, not by Unity.</summary>
    public enum CharacterVisualState
    {
        Idle,
        Speaking,
        SuccessFeedback,
        FailureFeedback
    }
}
