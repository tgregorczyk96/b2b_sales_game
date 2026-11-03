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
        public RunInfo(int runId, int seed, string difficulty, string scenarioId, bool isRerun, bool isFollowUp = false)
        {
            IsFollowUp = isFollowUp;
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

        /// <summary>The follow-up visit after a demo period; a training run's follow-up is training too.</summary>
        public bool IsFollowUp { get; }

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
    /// The runs of the game: which seeds were played, the last run for "Seed wiederholen", and the last lead score per
    /// seed for a small before/after comparison. The runtime source of truth; the run loop restores it from the save
    /// (<see cref="Restore"/>) and writes it back (<see cref="WriteTo"/>).
    /// </summary>
    /// <remarks>
    /// A scenario counts as played at every difficulty: the engine generates the same origin for a seed regardless of the
    /// difficulty, so playing it again — also at another difficulty or via the manual seed field — is training. A scenario is a
    /// seed of one kind (e.g. website or review stand): the same seed in another kind is a different origin. Saves from before
    /// kinds existed only know seeds; those seeds count as played in every kind.
    /// </remarks>
    public sealed class RunRegistry
    {
        private readonly HashSet<int> playedSeeds = new HashSet<int>();
        private readonly HashSet<string> playedScenarios = new HashSet<string>();
        private readonly HashSet<int> legacySeeds = new HashSet<int>();
        private readonly Dictionary<int, float> lastScores = new Dictionary<int, float>();
        private readonly Dictionary<string, float> scenarioScores = new Dictionary<string, float>();
        private int nextRunId = 1;

        /// <summary>The most recently started run; <c>null</c> before the first.</summary>
        public RunInfo Last { get; private set; }

        public bool WasPlayed(int seed) => playedSeeds.Contains(seed);

        /// <summary>The scenario (seed of one kind) was played before.</summary>
        public bool WasPlayed(string scenarioId) => playedScenarios.Contains(scenarioId ?? string.Empty);

        /// <summary>Registers a run that was started; it is a rerun if its scenario was played before.</summary>
        public RunInfo Begin(int seed, string difficulty, string scenarioId)
        {
            var id = scenarioId ?? string.Empty;
            var run = new RunInfo(nextRunId++, seed, difficulty, id, playedScenarios.Contains(id) || legacySeeds.Contains(seed));
            playedSeeds.Add(seed);
            playedScenarios.Add(id);
            Last = run;
            return run;
        }

        /// <summary>
        /// Registers the follow-up of a first visit. It continues that scenario: not a new scenario and not a rerun — unless the
        /// first visit was training.
        /// </summary>
        public RunInfo BeginFollowUp(int seed, string difficulty, string scenarioId, bool isTraining)
        {
            var run = new RunInfo(nextRunId++, seed, difficulty, scenarioId, isTraining, isFollowUp: true);
            playedSeeds.Add(seed);
            playedScenarios.Add(scenarioId ?? string.Empty);
            return run;
        }

        /// <summary>The lead score of the latest finished run of <paramref name="seed"/>, if any.</summary>
        public float? LastScore(int seed) => lastScores.TryGetValue(seed, out var score) ? score : (float?)null;

        /// <summary>The lead score of the latest finished run of the same scenario as <paramref name="run"/>, if any.</summary>
        public float? LastScore(RunInfo run)
        {
            if (run == null)
            {
                return null;
            }

            if (scenarioScores.TryGetValue(run.ScenarioId, out var score))
            {
                return score;
            }

            return legacySeeds.Contains(run.Seed) ? LastScore(run.Seed) : null;
        }

        public void RecordScore(int seed, float score)
        {
            lastScores[seed] = score;
        }

        /// <summary>Records the score of a finished run for its scenario (and its seed).</summary>
        public void RecordScore(RunInfo run, float score)
        {
            if (run == null)
            {
                return;
            }

            lastScores[run.Seed] = score;
            scenarioScores[run.ScenarioId] = score;
        }

        /// <summary>A registry continuing a saved game: played seeds stay played, so a replay is still a training run.</summary>
        public static RunRegistry Restore(SaveData data)
        {
            var runs = new RunRegistry();
            if (data == null)
            {
                return runs;
            }

            var knowsScenarios = data.playedScenarios != null && data.playedScenarios.Count > 0;
            foreach (var seed in data.playedSeeds ?? new List<int>())
            {
                if (seed >= 0)
                {
                    runs.playedSeeds.Add(seed);
                    if (!knowsScenarios)
                    {
                        runs.legacySeeds.Add(seed); // a save from before kinds: the seed stays played in every kind
                    }
                }
            }

            foreach (var scenarioId in data.playedScenarios ?? new List<string>())
            {
                if (!string.IsNullOrEmpty(scenarioId))
                {
                    runs.playedScenarios.Add(scenarioId);
                }
            }

            foreach (var entry in data.lastScores ?? new List<SaveData.SeedScore>())
            {
                if (entry != null && entry.seed >= 0)
                {
                    runs.lastScores[entry.seed] = entry.score;
                }
            }

            foreach (var entry in data.scenarioScores ?? new List<SaveData.ScenarioScore>())
            {
                if (entry != null && !string.IsNullOrEmpty(entry.scenarioId))
                {
                    runs.scenarioScores[entry.scenarioId] = entry.score;
                }
            }

            runs.nextRunId = Math.Max(1, data.runsStarted + 1);
            if (data.hasLastRun && data.lastRun != null && data.lastRun.seed >= 0)
            {
                runs.playedSeeds.Add(data.lastRun.seed);
                if (!string.IsNullOrEmpty(data.lastRun.scenarioId))
                {
                    runs.playedScenarios.Add(data.lastRun.scenarioId);
                }

                runs.Last = new RunInfo(0, data.lastRun.seed, data.lastRun.difficulty, data.lastRun.scenarioId, isRerun: false);
            }

            return runs;
        }

        /// <summary>Writes the registry's part of the save (played seeds, last scores, last run, run counter).</summary>
        public void WriteTo(SaveData data)
        {
            data.playedSeeds = new List<int>(playedSeeds);
            data.playedSeeds.Sort();
            data.lastScores = new List<SaveData.SeedScore>();
            foreach (var pair in lastScores)
            {
                data.lastScores.Add(new SaveData.SeedScore { seed = pair.Key, score = pair.Value });
            }

            data.lastScores.Sort((a, b) => a.seed.CompareTo(b.seed));
            data.playedScenarios = new List<string>(playedScenarios);
            data.playedScenarios.Sort(StringComparer.Ordinal);
            data.scenarioScores = new List<SaveData.ScenarioScore>();
            foreach (var pair in scenarioScores)
            {
                data.scenarioScores.Add(new SaveData.ScenarioScore { scenarioId = pair.Key, score = pair.Value });
            }

            data.scenarioScores.Sort((a, b) => string.CompareOrdinal(a.scenarioId, b.scenarioId));
            data.runsStarted = nextRunId - 1;
            data.hasLastRun = Last != null;
            data.lastRun = Last == null
                ? new SaveData.SavedRun()
                : new SaveData.SavedRun { seed = Last.Seed, difficulty = Last.Difficulty, scenarioId = Last.ScenarioId };
        }

        public int PlayedSeedCount => playedSeeds.Count;
    }
}
