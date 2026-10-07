using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SalesEngine;
using SalesEngine.Difficulty;
using SalesEngine.Training;
using SalesSim.Application;
using SalesSim.EngineAdapter;
using SalesSim.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using GuidanceAvailability = SalesSim.Application.GuidanceAvailability;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>
    /// Guidance buttons against the real engine at each difficulty: what is allowed comes from the engine's
    /// <see cref="GuidancePolicy"/>, never from Unity. Placeholder customer and a fixed example generator — no API.
    /// </summary>
    public sealed class GuidanceDifficultyTests
    {
        private const string ScenarioId = "hair-salon:1406361028";
        private const string ExampleSentence = "Guten Tag, hier ist Tomasz von tom-gre-it – haben Sie kurz zwei Minuten?";

        private static readonly GuidanceLevel[] Levels = { GuidanceLevel.Hint, GuidanceLevel.HintMore, GuidanceLevel.Example };

        private SalesConversationController controller;
        private GuidancePanelView guidance;
        private RunResultView result;
        private Button end;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            controller = Object.FindAnyObjectByType<SalesConversationController>();
            guidance = Object.FindAnyObjectByType<GuidancePanelView>();
            result = Object.FindAnyObjectByType<RunResultView>();
            end = GameObject.Find("EndConversationButton").GetComponent<Button>();
        }

        [UnityTest]
        public IEnumerator Easy_AllThreeLevelsAreAvailable_AsTheEngineAllows()
        {
            foreach (var level in Levels)
            {
                Assert.That(GuidancePolicy.Allows(DifficultyProfile.Easy, ToType(level)), Is.True,
                    $"The current Easy run must keep {level}.");
            }

            yield return PlayAt(DifficultyProfile.Easy);
        }

        [UnityTest]
        public IEnumerator Medium_FollowsTheEngineRules()
        {
            yield return PlayAt(DifficultyProfile.Medium);
        }

        [UnityTest]
        public IEnumerator Hard_FollowsTheEngineRules()
        {
            yield return PlayAt(DifficultyProfile.Hard);
        }

        /// <summary>
        /// Shows the engine's availability before any click, presses every button (also the disabled ones), and checks
        /// that only allowed guidance is shown and counted — in the panel and on the result screen.
        /// </summary>
        private IEnumerator PlayAt(DifficultyProfile difficulty)
        {
            var generator = new FixedExampleGenerator();
            var session = new SalesEngineGameSession(ContentDirectory, () => new SalesSimulationEngine(), difficulty, generator);
            controller.Initialize(session, ScenarioId, new PlayerProfile("Tomasz", "tom-gre-it"));
            yield return null;

            foreach (var level in Levels)
            {
                var allowed = GuidancePolicy.Allows(difficulty, ToType(level));
                Assert.That(session.GetGuidanceAvailability(level),
                    Is.EqualTo(allowed ? GuidanceAvailability.Available : GuidanceAvailability.NotAllowedByDifficulty), level.ToString());
                Assert.That(guidance.ButtonFor(level).interactable, Is.EqualTo(allowed), $"{level} before any click");
                Assert.That(guidance.IsAvailable(level), Is.EqualTo(allowed), level.ToString());
                Assert.That(Label(level).Contains("nicht auf dieser Stufe"), Is.EqualTo(!allowed), level.ToString());
            }

            foreach (var level in Levels)
            {
                guidance.ButtonFor(level).onClick.Invoke(); // bypasses interactable on purpose
                yield return null;
            }

            yield return null;
            var allowedCount = 0;
            foreach (var level in Levels)
            {
                var allowed = GuidancePolicy.Allows(difficulty, ToType(level));
                allowedCount += allowed ? 1 : 0;
                Assert.That(guidance.StateOf(level), Is.EqualTo(allowed ? GuidanceSectionState.Shown : GuidanceSectionState.Hidden), level.ToString());
                Assert.That(controller.GuidanceUsage.CountOf(level), Is.EqualTo(allowed ? 1 : 0), level.ToString());
            }

            var exampleAllowed = GuidancePolicy.Allows(difficulty, GuidanceType.FullExampleSentence);
            Assert.That(generator.Calls, Is.EqualTo(exampleAllowed ? 1 : 0), "Disallowed examples never reach the generator.");
            if (exampleAllowed)
            {
                Assert.That(guidance.TextOf(GuidanceLevel.Example), Is.EqualTo(ExampleSentence));
                Assert.That(generator.LastTrainee, Is.EqualTo(new TraineeProfile("Tomasz", "tom-gre-it")), "The player profile reaches the engine.");
            }

            end.onClick.Invoke();
            yield return null;

            Assert.That(result.IsVisible, Is.True);
            var summary = GameObject.Find("ResultOverlay").transform.Find("Panel/Guidance").GetComponent<TMP_Text>().text;
            Assert.That(summary, Does.Contain($"Hinweise: {controller.GuidanceUsage.HintCount}\n"));
            Assert.That(summary, Does.Contain($"Mehr Hinweise: {controller.GuidanceUsage.HintMoreCount}\n"));
            Assert.That(summary, Does.Contain($"Beispiele: {controller.GuidanceUsage.ExampleCount}"));
            Assert.That(summary.Contains("Run ohne Hilfe"), Is.EqualTo(allowedCount == 0));
        }

        private string Label(GuidanceLevel level) => guidance.ButtonFor(level).GetComponentInChildren<TMP_Text>().text;

        private static GuidanceType ToType(GuidanceLevel level)
        {
            switch (level)
            {
                case GuidanceLevel.Hint: return GuidanceType.Orientation;
                case GuidanceLevel.HintMore: return GuidanceType.Specific;
                default: return GuidanceType.FullExampleSentence;
            }
        }

        private static string ContentDirectory => Path.Combine(UnityEngine.Application.streamingAssetsPath, "SalesEngine", "content");

        private sealed class FixedExampleGenerator : IExampleSentenceGenerator
        {
            public int Calls { get; private set; }

            public TraineeProfile LastTrainee { get; private set; }

            public Task<string> GenerateAsync(ExampleSentenceRequest request, CancellationToken cancellationToken)
            {
                Calls++;
                LastTrainee = request.Context.Trainee;
                return Task.FromResult(ExampleSentence);
            }
        }
    }
}
