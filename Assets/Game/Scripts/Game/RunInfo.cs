using System;
using System.Collections.Generic;

namespace SalesSim.Game
{
    /// <summary>
    /// What a run was started with: enough to start exactly the same origin again. A rerun is a training run: it is
    /// scored like any run but never pays out.
    /// </summary>
    public sealed class RunInfo
    {
        public RunInfo(int runId, int seed, string difficulty, string scenarioId, bool isRerun)
        {
            if (seed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed), seed, "Seeds are 0 or greater.");
            }

            RunId = runId;
            Seed = seed;
            Difficulty = difficulty ?? string.Empty;
            ScenarioId = scenarioId ?? string.Empty;
            IsRerun = isRerun;
        }

        /// <summary>Counts runs within the game session, starting at 1.</summary>
        public int RunId { get; }

        public int Seed { get; }

        /// <summary>The engine's difficulty name, e.g. "Medium".</summary>
        public string Difficulty { get; }

        /// <summary>The opaque scenario id the engine regenerates the origin from.</summary>
        public string ScenarioId { get; }

        /// <summary>True when this seed was played before in this game session: a training run without payout.</summary>
        public bool IsRerun { get; }

        /// <summary>
        /// Reads the seed field: empty means "random" (<paramref name="seed"/> is <c>null</c>), otherwise a whole number in
        /// <c>0..2147483647</c> (spaces around it are ignored). Returns false for anything else.
        /// </summary>
        public static bool TryParseSeed(string text, out int? seed)
        {
            seed = null;
            var trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return true;
            }

            if (int.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                seed = value;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The runs of the current game session (in memory only, not persisted): which seeds were played, the last run for
    /// "Seed wiederholen", and the last lead score per seed for a small before/after comparison.
    /// </summary>
    /// <remarks>
    /// A seed counts as played at every difficulty: the engine generates the same origin for a seed regardless of the
    /// difficulty, so playing it again — also at another difficulty or via the manual seed field — is training.
    /// </remarks>
    public sealed class RunRegistry
    {
        private readonly HashSet<int> playedSeeds = new HashSet<int>();
        private readonly Dictionary<int, float> lastScores = new Dictionary<int, float>();
        private int nextRunId = 1;

        /// <summary>The most recently started run; <c>null</c> before the first.</summary>
        public RunInfo Last { get; private set; }

        public bool WasPlayed(int seed) => playedSeeds.Contains(seed);

        /// <summary>Registers a run that was started; it is a rerun if its seed was played before.</summary>
        public RunInfo Begin(int seed, string difficulty, string scenarioId)
        {
            var run = new RunInfo(nextRunId++, seed, difficulty, scenarioId, playedSeeds.Contains(seed));
            playedSeeds.Add(seed);
            Last = run;
            return run;
        }

        /// <summary>The lead score of the latest finished run of <paramref name="seed"/>, if any.</summary>
        public float? LastScore(int seed) => lastScores.TryGetValue(seed, out var score) ? score : (float?)null;

        public void RecordScore(int seed, float score)
        {
            lastScores[seed] = score;
        }
    }
}
