using System.Collections;
using NUnit.Framework;
using SalesSim.Application;
using SalesSim.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>
    /// Run setup, seeds and reruns in SalesTestScene against a scripted session and seed source: which run is started,
    /// first run vs. training run, no payout for reruns, result screen. Engine truth (origins, difficulty rules) is
    /// covered by <see cref="GuidanceDifficultyTests"/> and the EditMode adapter tests.
    /// </summary>
    public sealed class RunSetupTests
    {
        private static readonly SalesIndicators GoodLead = new SalesIndicators(70, 72, 75, 60);

        private ScriptedSession session;
        private ScriptedScenarioSource scenarios;
        private SalesRunLoop loop;
        private RunSetupView setup;
        private RunResultView result;
        private TMP_InputField input;
        private Button send;
        private Button end;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            loop = Object.FindAnyObjectByType<SalesRunLoop>();
            setup = Object.FindAnyObjectByType<RunSetupView>();
            result = Object.FindAnyObjectByType<RunResultView>();
            input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            send = GameObject.Find("SendButton").GetComponent<Button>();
            end = GameObject.Find("EndConversationButton").GetComponent<Button>();

            session = new ScriptedSession();
            scenarios = new ScriptedScenarioSource();
            loop.Configure(session, scenarios, new PlayerProfile("Tomasz", "tom-gre-it"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameStart_ShowsTheSetup_WithTheSourcesDifficulties_AndNoRerunYet()
        {
            Assert.That(setup.IsVisible, Is.True);
            Assert.That(session.Starts, Is.EqualTo(0), "No conversation before a run is started.");
            foreach (var name in scenarios.Difficulties)
            {
                Assert.That(setup.DifficultyButton(name).GetComponentInChildren<TMP_Text>().text, Is.EqualTo(name));
            }

            Assert.That(setup.SelectedDifficulty, Is.EqualTo("Easy"));
            Assert.That(setup.RerunButton.interactable, Is.False);
            yield break;
        }

        [UnityTest]
        public IEnumerator NewRun_WithEmptySeed_UsesARandomSeed_AndTheChosenDifficulty()
        {
            setup.DifficultyButton("Medium").onClick.Invoke();
            setup.NewRunButton.onClick.Invoke();
            yield return null;

            Assert.That(setup.IsVisible, Is.False);
            Assert.That(scenarios.RandomSeedsPicked, Is.EqualTo(1));
            Assert.That(loop.CurrentRun.Seed, Is.EqualTo(1000));
            Assert.That(loop.CurrentRun.Difficulty, Is.EqualTo("Medium"));
            Assert.That(loop.CurrentRun.IsRerun, Is.False);
            Assert.That(session.LastStart.ScenarioId, Is.EqualTo("scripted:1000"));
            Assert.That(session.LastStart.Difficulty, Is.EqualTo("Medium"));
            Assert.That(session.LastStart.Player.Name, Is.EqualTo("Tomasz"));
            Assert.That(Text("RunLabel"), Is.EqualTo("Medium · Seed 1000 · Erster Run"), "The seed is visible during the run.");
        }

        [UnityTest]
        public IEnumerator NewRun_WithAManualSeed_UsesExactlyThatSeed()
        {
            setup.DifficultyButton("Hard").onClick.Invoke();
            setup.SeedText = " 1406361028 ";
            setup.NewRunButton.onClick.Invoke();
            yield return null;

            Assert.That(scenarios.RandomSeedsPicked, Is.EqualTo(0));
            Assert.That(loop.CurrentRun.Seed, Is.EqualTo(1406361028));
            Assert.That(session.LastStart.ScenarioId, Is.EqualTo("scripted:1406361028"));
            Assert.That(session.LastStart.Difficulty, Is.EqualTo("Hard"));
        }

        [UnityTest]
        public IEnumerator InvalidSeed_IsRejected_WithAMessage_AndNoRun()
        {
            foreach (var invalid in new[] { "-5", "abc", "99999999999" })
            {
                setup.SeedText = invalid;
                setup.NewRunButton.onClick.Invoke();
                Assert.That(setup.IsVisible, Is.True, invalid);
                Assert.That(setup.Message, Is.EqualTo(SalesRunLoop.InvalidSeedMessage), invalid);
            }

            Assert.That(session.Starts, Is.EqualTo(0));
            yield break;
        }

        [UnityTest]
        public IEnumerator RandomSeedButton_FillsTheField_AndThatSeedIsPlayed()
        {
            setup.RandomSeedButton.onClick.Invoke();
            Assert.That(setup.SeedText, Is.EqualTo("1000"));
            setup.RandomSeedButton.onClick.Invoke();
            Assert.That(setup.SeedText, Is.EqualTo("1001"), "A new random seed differs from the previous one.");

            setup.NewRunButton.onClick.Invoke();
            yield return null;
            Assert.That(loop.CurrentRun.Seed, Is.EqualTo(1001));
        }

        [UnityTest]
        public IEnumerator FirstRun_CanBeSold_AndPaysOut()
        {
            yield return PlayOneRun(() => setup.NewRunButton.onClick.Invoke());

            Assert.That(result.CanSell, Is.True);
            Assert.That(Text("Run"), Is.EqualTo("Easy · Seed 1000 · Erster Run"));
            Assert.That(Text("Payout"), Does.Contain("700"));
            result.SellButton.onClick.Invoke();
            Assert.That(loop.Wallet.Balance, Is.EqualTo(700));
        }

        [UnityTest]
        public IEnumerator Rerun_FromTheResult_HasTheSameSeedAndDifficulty_IsTraining_AndPaysNothing()
        {
            setup.DifficultyButton("Medium").onClick.Invoke();
            yield return PlayOneRun(() => setup.NewRunButton.onClick.Invoke());
            var first = loop.CurrentRun;

            yield return PlayOneRun(() => result.RerunButton.onClick.Invoke());
            var rerun = loop.CurrentRun;

            Assert.That(rerun.Seed, Is.EqualTo(first.Seed));
            Assert.That(rerun.Difficulty, Is.EqualTo("Medium"));
            Assert.That(rerun.ScenarioId, Is.EqualTo(first.ScenarioId));
            Assert.That(rerun.IsRerun, Is.True);
            Assert.That(rerun.RunId, Is.Not.EqualTo(first.RunId));
            Assert.That(session.LastStart.Difficulty, Is.EqualTo("Medium"));

            Assert.That(loop.CurrentLead.Evaluation.IsSellable, Is.True, "The lead itself is evaluated as usual.");
            Assert.That(Text("Quality"), Does.Contain("Qualifiziert"));
            Assert.That(result.CanSell, Is.False, "A training run cannot be sold.");
            Assert.That(Text("Payout"), Is.EqualTo("Trainingslauf – kein Payout"));
            Assert.That(Text("Run"), Does.StartWith("Medium · Seed 1000 · Trainingslauf"));

            result.SellButton.onClick.Invoke(); // bypasses interactable on purpose
            Assert.That(loop.Wallet.Balance, Is.EqualTo(0), "No money from reruns.");
        }

        [UnityTest]
        public IEnumerator Rerun_ShowsTheScoreOfThePreviousAttempt()
        {
            yield return PlayOneRun(() => setup.NewRunButton.onClick.Invoke(), new SalesIndicators(45, 50, 40, 60));
            var before = Mathf.RoundToInt(loop.CurrentLead.Evaluation.Score);

            yield return PlayOneRun(() => result.RerunButton.onClick.Invoke());
            var now = Mathf.RoundToInt(loop.CurrentLead.Evaluation.Score);

            Assert.That(Text("Run"), Does.Contain($"Vorher: {before} · Jetzt: {now} (+{now - before})"));
        }

        [UnityTest]
        public IEnumerator SeedWiederholen_InTheSetup_RepeatsTheLastRun_EvenIfAnotherDifficultyIsSelected()
        {
            setup.DifficultyButton("Hard").onClick.Invoke();
            yield return PlayOneRun(() => setup.NewRunButton.onClick.Invoke());
            result.NextRunButton.onClick.Invoke();

            Assert.That(setup.IsVisible, Is.True);
            Assert.That(setup.SeedText, Is.Empty, "A new run is random again unless a seed is typed.");
            Assert.That(setup.LastRunText, Does.StartWith("Letzter Run: Hard · Seed 1000\n"));
            Assert.That(setup.RerunButton.interactable, Is.True);

            setup.DifficultyButton("Easy").onClick.Invoke();
            setup.RerunButton.onClick.Invoke();
            yield return null;
            Assert.That(loop.CurrentRun.Seed, Is.EqualTo(1000));
            Assert.That(loop.CurrentRun.Difficulty, Is.EqualTo("Hard"));
            Assert.That(loop.CurrentRun.IsRerun, Is.True);
        }

        [UnityTest]
        public IEnumerator PlayedSeed_TypedInAgain_IsATrainingRun_AlsoAtAnotherDifficulty()
        {
            yield return PlayOneRun(() => setup.NewRunButton.onClick.Invoke());
            result.NextRunButton.onClick.Invoke();

            setup.DifficultyButton("Hard").onClick.Invoke();
            setup.SeedText = "1000";
            yield return PlayOneRun(() => setup.NewRunButton.onClick.Invoke());

            Assert.That(loop.CurrentRun.IsRerun, Is.True, "Same seed = same origin at every difficulty: no money farming.");
            Assert.That(result.CanSell, Is.False);
        }

        [UnityTest]
        public IEnumerator NewRandomRun_AfterARerun_IsAFirstRunAgain_AndCanBeSold()
        {
            yield return PlayOneRun(() => setup.NewRunButton.onClick.Invoke());
            yield return PlayOneRun(() => result.RerunButton.onClick.Invoke());
            result.NextRunButton.onClick.Invoke();

            yield return PlayOneRun(() => setup.NewRunButton.onClick.Invoke());

            Assert.That(loop.CurrentRun.Seed, Is.EqualTo(1001));
            Assert.That(loop.CurrentRun.IsRerun, Is.False);
            Assert.That(result.CanSell, Is.True);
        }

        /// <summary>Starts a run with <paramref name="start"/>, plays one turn and lets the engine end it with a good lead.</summary>
        private IEnumerator PlayOneRun(System.Action start, SalesIndicators final = null)
        {
            start();
            yield return null;
            input.text = "Darf ich Ihnen nächste Woche einen Termin vorschlagen?";
            send.onClick.Invoke();
            session.Answer("Ja, Dienstag passt.", final ?? GoodLead, over: true, endReason: "Completed", commitment: "Appointment");
            yield return null;
            Assert.That(result.IsVisible, Is.True);
        }

        private static string Text(string name)
        {
            var found = GameObject.Find(name) ?? GameObject.Find("ResultOverlay").transform.Find("Panel/" + name)?.gameObject;
            return found.GetComponent<TMP_Text>().text;
        }
    }
}
