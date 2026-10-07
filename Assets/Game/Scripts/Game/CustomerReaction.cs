using System;
using SalesSim.Application;

namespace SalesSim.Game
{
    /// <summary>How the on-screen customer visibly reacts to the last turn. Presentation only.</summary>
    public enum CustomerMood
    {
        Neutral,
        Positive,
        VeryPositive,
        Negative,
        VeryNegative
    }

    /// <summary>
    /// Game-presentation heuristic, not a sales judgement: the mood shown after a turn is read from how much the engine's
    /// own indicators moved (sum of the changes of Trust, Openness, Engagement and Patience between two engine states).
    /// </summary>
    public static class CustomerReaction
    {
        public const float StrongChange = 8f;
        public const float ClearChange = 3f;

        public static float TotalChange(SalesIndicators before, SalesIndicators after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));

            return (after.Trust - before.Trust)
                + (after.Openness - before.Openness)
                + (after.Engagement - before.Engagement)
                + (after.Patience - before.Patience);
        }

        public static CustomerMood MoodAfterTurn(SalesIndicators before, SalesIndicators after)
        {
            var change = TotalChange(before, after);
            if (change >= StrongChange) return CustomerMood.VeryPositive;
            if (change >= ClearChange) return CustomerMood.Positive;
            if (change <= -StrongChange) return CustomerMood.VeryNegative;
            if (change <= -ClearChange) return CustomerMood.Negative;
            return CustomerMood.Neutral;
        }
    }
}
