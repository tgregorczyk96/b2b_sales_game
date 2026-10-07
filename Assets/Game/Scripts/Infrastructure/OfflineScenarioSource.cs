using System;
using System.Collections.Generic;
using SalesSim.Application;

namespace SalesSim.Infrastructure
{
    /// <summary>
    /// Placeholder for <see cref="IScenarioSource"/> while the engine cannot be set up (goes with
    /// <see cref="NotConnectedSalesGameSession"/>). No scenarios, no difficulty rules — just keeps the setup usable.
    /// </summary>
    public sealed class OfflineScenarioSource : IScenarioSource
    {
        private readonly Random random = new Random();

        public IReadOnlyList<string> Difficulties { get; } = new[] { "Offline" };

        public int PickRandomSeed() => random.Next();

        public string ScenarioIdFor(int seed) => $"offline:{seed}";
    }
}
