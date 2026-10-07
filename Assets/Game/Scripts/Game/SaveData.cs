using System;
using System.Collections.Generic;

namespace SalesSim.Game
{
    /// <summary>
    /// The persistent game state: only what must survive a restart — money, which seeds were played (so a replay stays a
    /// training run), the last run (for "Seed wiederholen") and the last lead score per seed. Plain serializable fields;
    /// readers ignore fields they do not know, and <see cref="version"/> says which format wrote the file.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>Format of this build. Bump when a field changes meaning; adding fields needs no bump.</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public int balance;
        public int runsStarted;
        public List<int> playedSeeds = new List<int>();
        public List<SeedScore> lastScores = new List<SeedScore>();
        public bool hasLastRun;
        public SavedRun lastRun = new SavedRun();

        [Serializable]
        public sealed class SeedScore
        {
            public int seed;
            public float score;
        }

        [Serializable]
        public sealed class SavedRun
        {
            public int seed;
            public string difficulty = string.Empty;
            public string scenarioId = string.Empty;
        }
    }

    /// <summary>Loads and saves <see cref="SaveData"/>. Never throws for a missing or damaged save: it starts fresh.</summary>
    public interface IGameStateStore
    {
        /// <summary>The saved state, or a new one (balance 0, nothing played) when there is none or it cannot be read.</summary>
        SaveData Load();

        void Save(SaveData data);
    }
}
