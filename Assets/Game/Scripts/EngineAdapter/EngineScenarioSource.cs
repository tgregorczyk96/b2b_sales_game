using System;
using System.Collections.Generic;
using System.Linq;
using SalesEngine.Content;
using SalesSim.Application;

namespace SalesSim.EngineAdapter
{
    /// <summary>
    /// <see cref="IScenarioSource"/> from the engine: random seeds come from <see cref="TrainingPools.PickSeed"/> with the
    /// console's default <see cref="TrainingPool.Standard"/> (as <c>--random</c>), scenario ids have the adapter's form
    /// <c>{pool}:{seed}</c>, and the difficulties are the engine's presets. Seeds are any number ≥ 0; the engine generates
    /// the same origin for the same seed at every difficulty.
    /// </summary>
    public sealed class EngineScenarioSource : IScenarioSource
    {
        /// <summary>The engine's generation pool the simulator plays (same as the console's <c>--seed</c>).</summary>
        public const string DefaultPool = "hair-salon";

        private readonly ScenarioGenerator generator;
        private readonly Random random;
        private readonly Lazy<IReadOnlyList<int>> candidates;

        /// <param name="contentDirectory">The engine's <c>content</c> folder.</param>
        /// <param name="pool">The generation pool, e.g. <c>hair-salon</c>.</param>
        /// <param name="random">Only picks seeds; tests pass a seeded one. <c>null</c> uses a time-seeded one.</param>
        public EngineScenarioSource(string contentDirectory, string pool = DefaultPool, Random random = null)
        {
            generator = new ContentRepository(contentDirectory ?? throw new ArgumentNullException(nameof(contentDirectory)))
                .LoadGenerator(pool);
            this.random = random ?? new Random();
            candidates = new Lazy<IReadOnlyList<int>>(() => TrainingPools.CandidateSeeds(generator, TrainingPool.Standard));
            Difficulties = SalesEngineGameSession.Difficulties.Select(d => d.Name).ToList();
        }

        public IReadOnlyList<string> Difficulties { get; }

        /// <summary>
        /// Builds the engine's candidate seed window (10,000 generated origins, seconds in the editor) so the first
        /// random pick does not stall. Safe to call from a worker thread; picks wait for it if it is still running.
        /// </summary>
        public void Prepare()
        {
            _ = candidates.Value;
        }

        /// <remarks>
        /// Same pick as <see cref="TrainingPools.PickSeed"/> (a random entry of the engine's
        /// <see cref="TrainingPools.CandidateSeeds"/>), but the candidate window is built once and reused instead of on
        /// every call.
        /// </remarks>
        public int PickRandomSeed()
        {
            var seeds = candidates.Value;
            return seeds.Count == 0
                ? TrainingPools.PickSeed(generator, TrainingPool.Standard, random) // the engine reports the empty pool
                : seeds[random.Next(seeds.Count)];
        }

        public string ScenarioIdFor(int seed)
        {
            if (seed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed), seed, "Seeds are 0 or greater.");
            }

            return $"{generator.PoolId}:{seed}";
        }

        /// <summary>The origin's building blocks (business/perspective/offer, customer) for a scenario id — diagnostics and tests only.</summary>
        public string OriginOf(string scenarioId)
        {
            var (_, seed) = SalesEngineGameSession.ParseScenarioId(scenarioId);
            var origin = generator.Generate(seed);
            return $"{origin.Source}, customer {origin.Blocks.Customer}";
        }
    }
}
