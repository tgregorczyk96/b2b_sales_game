using System;
using SalesSim.Application;

namespace SalesSim.Game
{
    /// <summary>
    /// How often the player used live guidance in one run. A pure training metric: it changes no engine state, no lead
    /// value and no payout. Only guidance that was actually shown counts; refused or failed requests do not.
    /// </summary>
    public sealed class GuidanceUsage
    {
        public int HintCount { get; private set; }

        public int HintMoreCount { get; private set; }

        public int ExampleCount { get; private set; }

        public int Total => HintCount + HintMoreCount + ExampleCount;

        /// <summary>True when the run got through without any guidance.</summary>
        public bool IsUnassisted => Total == 0;

        public void Record(GuidanceLevel level)
        {
            switch (level)
            {
                case GuidanceLevel.Hint:
                    HintCount++;
                    break;
                case GuidanceLevel.HintMore:
                    HintMoreCount++;
                    break;
                case GuidanceLevel.Example:
                    ExampleCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }

        public int CountOf(GuidanceLevel level)
        {
            switch (level)
            {
                case GuidanceLevel.Hint: return HintCount;
                case GuidanceLevel.HintMore: return HintMoreCount;
                case GuidanceLevel.Example: return ExampleCount;
                default: throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }
    }
}
