using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SalesEngine.Content;
using SalesEngine.Difficulty;
using SalesEngine.Training;
using SalesSim.Application;
using SalesSim.EngineAdapter;
using GuidanceAvailability = SalesSim.Application.GuidanceAvailability;

namespace SalesSim.Tests.EditMode
{
    /// <summary>
    /// Seeds, difficulties and origins against the real engine (placeholder customer, no API): what the run setup relies
    /// on. The engine is the source of truth; these tests only check that Unity's adapter passes it through.
    /// </summary>
    public sealed class RunSetupEngineTests
    {
        private const int Seed = 1406361028;

        private static string ContentDirectory =>
            Path.Combine(UnityEngine.Application.streamingAssetsPath, "SalesEngine", "content");

        private static IEnumerable<DifficultyProfile> Difficulties()
        {
            yield return DifficultyProfile.Easy;
            yield return DifficultyProfile.Medium;
            yield return DifficultyProfile.Hard;
        }

        [Test]
        public void Source_OffersTheEnginesDifficultyPresets_EasiestFirst()
        {
            var source = new EngineScenarioSource(ContentDirectory);

            Assert.That(source.Difficulties, Is.EqualTo(Difficulties().Select(d => d.Name).ToArray()));
        }

        [Test]
        public void ScenarioId_IsThePoolAndTheSeed()
        {
            var source = new EngineScenarioSource(ContentDirectory);

            Assert.That(source.ScenarioIdFor(Seed), Is.EqualTo("hair-salon:" + Seed));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.ScenarioIdFor(-1));
        }

        [Test]
        public void RandomSeeds_ComeFromTheEnginesStandardPool_DifferNormally_AndAreReproducibleWithAFixedRandom()
        {
            var watch = Stopwatch.StartNew();
            var source = new EngineScenarioSource(ContentDirectory, random: new Random(1));
            var sequence = Enumerable.Range(0, 20).Select(_ => source.PickRandomSeed()).ToList();
            UnityEngine.Debug.Log($"[RunSetup] 20 random seeds picked in {watch.ElapsedMilliseconds} ms (first pick builds the candidate list)");

            var generator = new ContentRepository(ContentDirectory).LoadGenerator("hair-salon");
            Assert.That(new EngineScenarioSource(ContentDirectory, random: new Random(7)).PickRandomSeed(),
                Is.EqualTo(TrainingPools.PickSeed(generator, TrainingPool.Standard, new Random(7))),
                "The engine's own pick; with the same Random the same seed, so tests can fix seeds.");
            Assert.That(sequence.Distinct().Count(), Is.GreaterThan(10), "Consecutive random seeds normally differ.");
            Assert.That(sequence.Zip(sequence.Skip(1), (a, b) => a != b).Count(differs => differs), Is.GreaterThan(15));
            Assert.That(sequence, Has.All.Matches<int>(seed => TrainingPools.Admits(TrainingPool.Standard, generator.Generate(seed).Metadata)));
        }

        [Test]
        public void SameSeed_GivesTheSameOrigin_AtEveryDifficulty()
        {
            var source = new EngineScenarioSource(ContentDirectory);
            var id = source.ScenarioIdFor(Seed);

            var origin = source.OriginOf(id);

            Assert.That(origin, Does.Contain("customer "));
            Assert.That(new EngineScenarioSource(ContentDirectory).OriginOf(id), Is.EqualTo(origin), "Regenerated from the seed alone.");
            var distinct = Enumerable.Range(0, 50).Select(i => source.OriginOf(source.ScenarioIdFor(i))).Distinct().Count();
            Assert.That(distinct, Is.GreaterThan(1), "Different seeds give different origins.");
        }

        [TestCaseSource(nameof(Difficulties))]
        public async Task SameSeedAndDifficulty_StartsTheSameRunAgain(DifficultyProfile difficulty)
        {
            var logs = new List<string>();
            var session = SalesEngineGameSession.CreateWithoutLlm(ContentDirectory, logs.Add);
            var request = new SessionStartRequest("hair-salon:" + Seed, null, difficulty.Name);

            var first = await session.StartSessionAsync(request);
            var again = await session.StartSessionAsync(request);

            var runLines = logs.Where(line => line.StartsWith("[SalesClient] Run: ", StringComparison.Ordinal)).ToList();
            Assert.That(runLines.Count, Is.EqualTo(2));
            Assert.That(runLines[1], Is.EqualTo(runLines[0]), "Same origin, same difficulty.");
            Assert.That(runLines[0], Does.EndWith("difficulty " + difficulty.Name));
            Assert.That(again.CustomerMessage, Is.EqualTo(first.CustomerMessage));
            Assert.That(again.Indicators.Trust, Is.EqualTo(first.Indicators.Trust));
            Assert.That(again.Indicators.Openness, Is.EqualTo(first.Indicators.Openness));
            Assert.That(again.Indicators.Engagement, Is.EqualTo(first.Indicators.Engagement));
            Assert.That(again.Indicators.Patience, Is.EqualTo(first.Indicators.Patience));
        }

        [Test]
        public async Task TheDifficulty_ChangesTheRun_NotTheOrigin()
        {
            var logs = new List<string>();
            var session = SalesEngineGameSession.CreateWithoutLlm(ContentDirectory, logs.Add);

            await session.StartSessionAsync(new SessionStartRequest("hair-salon:" + Seed, null, "Easy"));
            await session.StartSessionAsync(new SessionStartRequest("hair-salon:" + Seed, null, "Hard"));

            var runLines = logs.Where(line => line.StartsWith("[SalesClient] Run: ", StringComparison.Ordinal)).ToList();
            Assert.That(OriginPart(runLines[1]), Is.EqualTo(OriginPart(runLines[0])));
            Assert.That(runLines[0], Does.EndWith("difficulty Easy"));
            Assert.That(runLines[1], Does.EndWith("difficulty Hard"));
        }

        [TestCaseSource(nameof(Difficulties))]
        public async Task TheChosenDifficulty_DrivesGuidanceAvailability(DifficultyProfile difficulty)
        {
            // Default Easy: only the request's difficulty can change the rules.
            var session = new SalesEngineGameSession(ContentDirectory, () => new SalesEngine.SalesSimulationEngine(), DifficultyProfile.Easy);
            await session.StartSessionAsync(new SessionStartRequest("hair-salon:" + Seed, null, difficulty.Name));

            Assert.That(session.GetGuidanceAvailability(GuidanceLevel.Hint) == GuidanceAvailability.Available,
                Is.EqualTo(GuidancePolicy.Allows(difficulty, GuidanceType.Orientation)));
            Assert.That(session.GetGuidanceAvailability(GuidanceLevel.HintMore) == GuidanceAvailability.Available,
                Is.EqualTo(GuidancePolicy.Allows(difficulty, GuidanceType.Specific)));
            Assert.That(session.GetGuidanceAvailability(GuidanceLevel.HintMore) == GuidanceAvailability.NotAllowedByDifficulty,
                Is.EqualTo(!GuidancePolicy.Allows(difficulty, GuidanceType.Specific)));
        }

        [Test]
        public void UnknownDifficulty_IsRejected()
        {
            var session = SalesEngineGameSession.CreateWithoutLlm(ContentDirectory);

            Assert.Throws<ArgumentException>(
                () => session.StartSessionAsync(new SessionStartRequest("hair-salon:1", null, "Nightmare")).GetAwaiter().GetResult());
        }

        private static string OriginPart(string runLine)
        {
            return runLine.Substring(0, runLine.LastIndexOf(", difficulty", StringComparison.Ordinal));
        }
    }
}
