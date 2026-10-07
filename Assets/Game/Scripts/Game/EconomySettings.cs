using System;

namespace SalesSim.Game
{
    /// <summary>
    /// Tuning of the game economy (what a conversation result is worth in the game). Not sales logic: the engine decides
    /// what happened in the conversation; these numbers only price it. Serializable so a scene can override the defaults.
    /// </summary>
    [Serializable]
    public sealed class EconomySettings
    {
        /// <summary>What a fully qualified lead for the current scenario is worth, in euros.</summary>
        public int baseLeadValue = 1000;

        /// <summary>Share of the lead value the closer keeps when the lead is handed over (0..1).</summary>
        public float closerShare = 0.30f;

        // Weights of the engine's customer indicators (0..100) in the lead score; they sum to 1.
        public float trustWeight = 0.35f;
        public float engagementWeight = 0.35f;
        public float opennessWeight = 0.20f;
        public float patienceWeight = 0.10f;

        // Minimum lead score per category; below coldScore the lead is worthless (Lost).
        public float coldScore = 45f;
        public float warmScore = 55f;
        public float hotScore = 70f;

        // Share of baseLeadValue per category.
        public float coldMultiplier = 0.10f;
        public float warmMultiplier = 0.30f;
        public float hotMultiplier = 0.60f;
        public float qualifiedMultiplier = 1.00f;

        public static EconomySettings Default => new EconomySettings();

        public float MultiplierFor(LeadQuality quality)
        {
            switch (quality)
            {
                case LeadQuality.Cold: return coldMultiplier;
                case LeadQuality.Warm: return warmMultiplier;
                case LeadQuality.Hot: return hotMultiplier;
                case LeadQuality.Qualified: return qualifiedMultiplier;
                default: return 0f;
            }
        }
    }
}
