using System;
using SalesSim.Application;

namespace SalesSim.Game
{
    /// <summary>How valuable a finished conversation is as a lead. Ordered: a later value is always the better lead.</summary>
    public enum LeadQuality
    {
        Lost,
        Cold,
        Warm,
        Hot,
        Qualified
    }

    /// <summary>The priced result of one run. Immutable.</summary>
    public sealed class LeadEvaluation
    {
        public LeadEvaluation(LeadQuality quality, float score, int value, int closerCut, string endReason, string commitment)
        {
            Quality = quality;
            Score = score;
            Value = value;
            CloserCut = closerCut;
            EndReason = endReason ?? string.Empty;
            Commitment = commitment ?? string.Empty;
        }

        public LeadQuality Quality { get; }

        /// <summary>Weighted engine indicators, 0..100.</summary>
        public float Score { get; }

        /// <summary>Lead value in euros.</summary>
        public int Value { get; }

        /// <summary>The closer's share in euros.</summary>
        public int CloserCut { get; }

        /// <summary>What the player receives when selling, in euros.</summary>
        public int Payout => Value - CloserCut;

        public bool IsSellable => Value > 0;

        /// <summary>As reported by the engine (e.g. "Completed"); empty if not stated.</summary>
        public string EndReason { get; }

        /// <summary>Next step the engine considers agreed (e.g. "Appointment"); empty if none.</summary>
        public string Commitment { get; }
    }

    /// <summary>
    /// Prices a finished conversation. Game-economy heuristic on top of the engine's results — it does not judge the
    /// conversation itself:
    /// <list type="number">
    /// <item>score = weighted sum of the engine's final Trust, Engagement (= engine Interest), Openness and Patience;</item>
    /// <item>category from the score (Lost &lt; cold &lt; warm &lt; hot thresholds);</item>
    /// <item>a next step the engine considers agreed raises the category to a floor (Information → Warm,
    ///       FollowUp → Hot, Appointment/Agreement → Qualified);</item>
    /// <item>a rejection without agreed next step caps it at Cold; an explicit no-contact request makes it Lost;</item>
    /// <item>value = baseLeadValue × category multiplier; closer cut = value × closerShare (rounded).</item>
    /// </list>
    /// Deterministic and monotone: better indicators never give a worse result when everything else is equal.
    /// </summary>
    public static class LeadEvaluator
    {
        public static LeadEvaluation Evaluate(SalesSessionState finalState, EconomySettings settings)
        {
            if (finalState == null)
            {
                throw new ArgumentNullException(nameof(finalState));
            }

            settings = settings ?? EconomySettings.Default;
            var indicators = finalState.Indicators;
            var score = Clamp(
                settings.trustWeight * indicators.Trust
                + settings.engagementWeight * indicators.Engagement
                + settings.opennessWeight * indicators.Openness
                + settings.patienceWeight * indicators.Patience);

            var quality = Max(QualityFromScore(score, settings), FloorFromCommitment(finalState.Commitment));
            if (finalState.EndReason == "NoContactRequested")
            {
                quality = LeadQuality.Lost;
            }
            else if (finalState.EndReason == "Rejected" && FloorFromCommitment(finalState.Commitment) == LeadQuality.Lost)
            {
                quality = Min(quality, LeadQuality.Cold);
            }

            var value = (int)Math.Round(settings.baseLeadValue * settings.MultiplierFor(quality), MidpointRounding.AwayFromZero);
            var cut = (int)Math.Round(value * settings.closerShare, MidpointRounding.AwayFromZero);
            return new LeadEvaluation(quality, score, value, cut, finalState.EndReason, finalState.Commitment);
        }

        private static LeadQuality QualityFromScore(float score, EconomySettings settings)
        {
            if (score >= settings.hotScore) return LeadQuality.Hot;
            if (score >= settings.warmScore) return LeadQuality.Warm;
            if (score >= settings.coldScore) return LeadQuality.Cold;
            return LeadQuality.Lost;
        }

        /// <summary>A next step the engine reports as agreed (its closing outcome). "NoFurtherAction" or none: no floor.</summary>
        private static LeadQuality FloorFromCommitment(string commitment)
        {
            switch (commitment)
            {
                case "Agreement":
                case "Appointment":
                    return LeadQuality.Qualified;
                case "FollowUp":
                    return LeadQuality.Hot;
                case "Information":
                    return LeadQuality.Warm;
                default:
                    return LeadQuality.Lost;
            }
        }

        // Rounded to two decimals so thresholds are not missed by float noise (e.g. 44.9999 for an exact 45).
        private static float Clamp(float score) => (float)Math.Round(Math.Max(0f, Math.Min(100f, score)), 2);

        private static LeadQuality Max(LeadQuality a, LeadQuality b) => a >= b ? a : b;

        private static LeadQuality Min(LeadQuality a, LeadQuality b) => a <= b ? a : b;
    }
}
