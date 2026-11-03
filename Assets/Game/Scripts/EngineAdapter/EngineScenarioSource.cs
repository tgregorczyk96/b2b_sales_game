using System;
using System.Collections.Generic;
using System.IO;
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
    /// <remarks>
    /// The kinds are the engine's generation pools that exist in the content: <c>hair-salon</c> (website, Google profile or
    /// review stand, by phone or visit) and <c>review-stand-visit</c> (the review stand sold and set up on site, website as
    /// follow-up). The pools themselves — which offers, which situations — are engine content; this class only labels them.
    /// </remarks>
    public sealed class EngineScenarioSource : IScenarioSource
    {
        /// <summary>The engine's generation pool the simulator plays by default (same as the console's <c>--seed</c>).</summary>
        public const string DefaultPool = "hair-salon";

        /// <summary>The engine's pool for selling the Google review stand during a salon visit.</summary>
        public const string ReviewStandPool = "review-stand-visit";

        private static readonly (string Pool, string Label)[] KnownKinds =
        {
            (DefaultPool, "Website & Co. (Telefon oder Besuch)"),
            (ReviewStandPool, "Bewertungsaufsteller (Vor-Ort-Besuch)"),
        };

        private readonly ContentRepository repository;
        private readonly Random random;
        private readonly Dictionary<string, (ScenarioGenerator Generator, Lazy<IReadOnlyList<int>> Candidates)> pools =
            new Dictionary<string, (ScenarioGenerator, Lazy<IReadOnlyList<int>>)>();
        private readonly object gate = new object();

        /// <param name="contentDirectory">The engine's <c>content</c> folder.</param>
        /// <param name="pool">The generation pool used first, e.g. <c>hair-salon</c>.</param>
        /// <param name="random">Only picks seeds; tests pass a seeded one. <c>null</c> uses a time-seeded one.</param>
        public EngineScenarioSource(string contentDirectory, string pool = DefaultPool, Random random = null)
        {
            repository = new ContentRepository(contentDirectory ?? throw new ArgumentNullException(nameof(contentDirectory)));
            this.random = random ?? new Random();
            Difficulties = SalesEngineGameSession.Difficulties.Select(d => d.Name).ToList();

            var kinds = KnownKinds
                .Where(k => k.Pool == pool || File.Exists(Path.Combine(contentDirectory, "generation", k.Pool + ".json")))
                .Select(k => new ScenarioKind(k.Pool, k.Label))
                .ToList();
            if (kinds.All(k => k.Id != pool))
            {
                kinds.Insert(0, new ScenarioKind(pool, pool));
            }

            Kinds = kinds;
            Select(pool);
        }

        public IReadOnlyList<string> Difficulties { get; }

        public IReadOnlyList<ScenarioKind> Kinds { get; }

        public string SelectedKind { get; private set; }

        /// <exception cref="ArgumentException">Not one of <see cref="Kinds"/>.</exception>
        /// <exception cref="ContentException">The pool's content is invalid.</exception>
        public void Select(string kindId)
        {
            if (Kinds.All(k => k.Id != kindId))
            {
                throw new ArgumentException($"Unknown scenario kind '{kindId}'.", nameof(kindId));
            }

            Pool(kindId); // loads and validates the pool once
            SelectedKind = kindId;
        }

        /// <summary>
        /// Builds the engine's candidate seed window of every kind (10,000 generated origins each, seconds in the editor) so
        /// the first random pick does not stall. Safe to call from a worker thread; picks wait for it if it is still running.
        /// </summary>
        public void Prepare()
        {
            foreach (var kind in Kinds)
            {
                _ = Pool(kind.Id).Candidates.Value;
            }
        }

        /// <remarks>
        /// Same pick as <see cref="TrainingPools.PickSeed"/> (a random entry of the engine's
        /// <see cref="TrainingPools.CandidateSeeds"/>), but the candidate window is built once and reused instead of on
        /// every call.
        /// </remarks>
        public int PickRandomSeed()
        {
            var (generator, candidates) = Pool(SelectedKind);
            var seeds = candidates.Value;
            lock (gate)
            {
                return seeds.Count == 0
                    ? TrainingPools.PickSeed(generator, TrainingPool.Standard, random) // the engine reports the empty pool
                    : seeds[random.Next(seeds.Count)];
            }
        }

        public string ScenarioIdFor(int seed)
        {
            if (seed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed), seed, "Seeds are 0 or greater.");
            }

            return $"{Pool(SelectedKind).Generator.PoolId}:{seed}";
        }

        /// <summary>The origin's building blocks (business/perspective/offer, customer) for a scenario id — diagnostics and tests only.</summary>
        public string OriginOf(string scenarioId)
        {
            var (poolId, seed) = SalesEngineGameSession.ParseScenarioId(scenarioId);
            var origin = Pool(poolId).Generator.Generate(seed);
            return $"{origin.Source}, customer {origin.Blocks.Customer}";
        }

        private (ScenarioGenerator Generator, Lazy<IReadOnlyList<int>> Candidates) Pool(string poolId)
        {
            lock (gate)
            {
                if (!pools.TryGetValue(poolId, out var pool))
                {
                    var generator = repository.LoadGenerator(poolId);
                    pool = (generator, new Lazy<IReadOnlyList<int>>(() => TrainingPools.CandidateSeeds(generator, TrainingPool.Standard)));
                    pools[poolId] = pool;
                }

                return pool;
            }
        }
    }
}
