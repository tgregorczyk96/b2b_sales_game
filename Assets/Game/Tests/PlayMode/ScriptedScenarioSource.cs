using System.Collections.Generic;
using SalesSim.Application;

namespace SalesSim.Tests.PlayMode
{
    /// <summary><see cref="IScenarioSource"/> for UI tests: fixed difficulty names, "random" seeds counting up from 1000.</summary>
    internal sealed class ScriptedScenarioSource : IScenarioSource
    {
        private int nextSeed = 1000;

        public IReadOnlyList<string> Difficulties { get; } = new[] { "Easy", "Medium", "Hard" };

        public int RandomSeedsPicked { get; private set; }

        public int PickRandomSeed()
        {
            RandomSeedsPicked++;
            return nextSeed++;
        }

        public string ScenarioIdFor(int seed) => $"scripted:{seed}";
    }
}
