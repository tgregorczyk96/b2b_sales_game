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
    /// The kinds are the contact types (on-site visit, phone call), each an engine generation pool in which the player can sell
    /// both offers in one conversation — the review stand and the website, in any order, depending on what the customer needs:
    /// <c>sales-visit</c> (on site, stand set up right there, prepared website draft with demo link) and <c>sales-call</c> (by
    /// phone, no setup). The seed picks the occasion of the contact; the other offer comes along. The earlier product-specific
    /// pools (<c>hair-salon</c>, <c>review-stand-visit</c>, <c>website-prototype-visit</c>) are no longer offered but stay
    /// playable by id (saved reruns and follow-ups). The pools themselves are engine content; this class only labels them.
    /// </remarks>
    public sealed class EngineScenarioSource : IScenarioSource
    {
        /// <summary>The engine's pool for a salon visit with both offers (review stand and website). Played by default.</summary>
        public const string VisitPool = "sales-visit";

        /// <summary>The engine's pool for a phone call with both offers (review stand and website), without on-site setup.</summary>
        public const string CallPool = "sales-call";

        /// <summary>The pool new runs use by default: the visit with both offers.</summary>
        public const string DefaultPool = VisitPool;

        /// <summary>Earlier pool: website, Google profile or review stand as the only offer, by phone or visit.</summary>
        public const string WebsitePool = "hair-salon";

        /// <summary>Earlier pool: selling the Google review stand during a salon visit, website only afterwards.</summary>
        public const string ReviewStandPool = "review-stand-visit";

        /// <summary>Earlier pool: presenting a prepared website draft during a salon visit (demo link, follow-up).</summary>
        public const string PrototypePool = "website-prototype-visit";

        private static readonly (string Pool, string Label)[] KnownKinds =
        {
            (VisitPool, "Vor-Ort-Besuch (Aufsteller & Website)"),
            (CallPool, "Telefonat (Aufsteller & Website)"),
        };

        /// <summary>Pools that can still be selected by id, but are not offered as a kind.</summary>
        private static readonly string[] EarlierPools = { WebsitePool, ReviewStandPool, PrototypePool };

        private readonly ContentRepository repository;
        private readonly Random random;
        private readonly Dictionary<string, (ScenarioGenerator Generator, Lazy<IReadOnlyList<int>> Candidates)> pools =
            new Dictionary<string, (ScenarioGenerator, Lazy<IReadOnlyList<int>>)>();
        private readonly object gate = new object();

        /// <param name="contentDirectory">The engine's <c>content</c> folder.</param>
        /// <param name="pool">The generation pool used first, e.g. <c>sales-visit</c>; an earlier pool is used but not offered as a kind.</param>
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
            if (kinds.All(k => k.Id != pool) && !EarlierPools.Contains(pool))
            {
                kinds.Insert(0, new ScenarioKind(pool, pool));
            }

            Kinds = kinds;
            Select(pool);
        }

        public IReadOnlyList<string> Difficulties { get; }

        public IReadOnlyList<ScenarioKind> Kinds { get; }

        public string SelectedKind { get; private set; }

        /// <exception cref="ArgumentException">Not one of <see cref="Kinds"/> (or an earlier pool, still playable by id).</exception>
        /// <exception cref="ContentException">The pool's content is invalid.</exception>
        public void Select(string kindId)
        {
            if (Kinds.All(k => k.Id != kindId) && !EarlierPools.Contains(kindId))
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
