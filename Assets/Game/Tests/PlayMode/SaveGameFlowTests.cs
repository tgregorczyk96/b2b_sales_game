using System.Collections;
using System.IO;
using NUnit.Framework;
using SalesSim.Application;
using SalesSim.Infrastructure;
using SalesSim.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>
    /// The savegame through the real game loop: earn money, "restart" (configure the loop again from the same save file),
    /// money and played seeds are still there, a played seed is a training run. Uses a temporary save file, never the
    /// player's real savegame.
    /// </summary>
    public sealed class SaveGameFlowTests
    {
        private static readonly SalesIndicators GoodLead = new SalesIndicators(70, 72, 75, 60);

        private string folder;
        private string savePath;
        private ScriptedSession session;
        private SalesRunLoop loop;
        private RunSetupView setup;
        private RunResultView result;
        private TMP_InputField input;
        private Button send;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            folder = Path.Combine(Path.GetTempPath(), "salessim-flow-" + System.Guid.NewGuid().ToString("N"));
            savePath = Path.Combine(folder, JsonFileGameStateStore.FileName);

            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            loop = Object.FindAnyObjectByType<SalesRunLoop>();
            setup = Object.FindAnyObjectByType<RunSetupView>();
            result = Object.FindAnyObjectByType<RunResultView>();
            input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            send = GameObject.Find("SendButton").GetComponent<Button>();
            Start();
        }

        [TearDown]
        public void DeleteFolder()
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        /// <summary>A (re)start of the game: the loop is configured from the save file, as the bootstrap does it.</summary>
        private void Start()
        {
            session = new ScriptedSession();
            loop.Configure(session, new ScriptedScenarioSource(), null, new JsonFileGameStateStore(savePath));
        }

        [UnityTest]
        public IEnumerator NewPlayer_StartsWithZero()
        {
            Assert.That(loop.Wallet.Balance, Is.EqualTo(0));
            Assert.That(loop.Runs.Last, Is.Null);
            Assert.That(setup.LastRunText, Does.Contain("Guthaben: 0 €").And.Contain("0 Seeds gespielt"));
            Assert.That(File.Exists(savePath), Is.False, "Nothing is written before the first run.");
            yield break;
        }

        [UnityTest]
        public IEnumerator EarnedMoney_AndPlayedSeeds_SurviveARestart_AndAPlayedSeedPaysNothingAgain()
        {
            yield return PlayGoodRun(() => setup.NewRunButton.onClick.Invoke());
            result.SellButton.onClick.Invoke();
            Assert.That(loop.Wallet.Balance, Is.EqualTo(700));
            var seed = loop.CurrentRun.Seed;
            var score = Mathf.RoundToInt(loop.CurrentLead.Evaluation.Score);

            Start(); // the game was closed and started again
            yield return null;

            Assert.That(loop.Wallet.Balance, Is.EqualTo(700), "The balance is still there.");
            Assert.That(GameObject.Find("BalanceLabel").GetComponent<TMP_Text>().text, Is.EqualTo("Guthaben: 700 €"));
            Assert.That(setup.LastRunText, Does.Contain($"Seed {seed}").And.Contain("Guthaben: 700 €").And.Contain("1 Seed gespielt"));
            Assert.That(setup.RerunButton.interactable, Is.True, "Seed wiederholen works after a restart.");

            setup.SeedText = seed.ToString();
            yield return PlayGoodRun(() => setup.NewRunButton.onClick.Invoke());

            Assert.That(loop.CurrentRun.IsRerun, Is.True, "Still a training run after the restart.");
            Assert.That(result.CanSell, Is.False);
            result.SellButton.onClick.Invoke();
            Assert.That(loop.Wallet.Balance, Is.EqualTo(700), "No money from a known seed.");
            Assert.That(Text("Run"), Does.Contain($"Vorher: {score}"), "The last score of the seed was restored.");
        }

        [UnityTest]
        public IEnumerator ASeedCountsAsPlayed_AsSoonAsTheRunStarts_EvenIfTheGameIsClosedMidRun()
        {
            setup.NewRunButton.onClick.Invoke();
            yield return null;
            var seed = loop.CurrentRun.Seed;

            Start(); // closed during the conversation
            yield return null;

            Assert.That(loop.Runs.WasPlayed(seed), Is.True);
            setup.SeedText = seed.ToString();
            setup.NewRunButton.onClick.Invoke();
            Assert.That(loop.CurrentRun.IsRerun, Is.True);
        }

        private IEnumerator PlayGoodRun(System.Action start)
        {
            start();
            yield return null;
            input.text = "Darf ich Ihnen nächste Woche einen Termin vorschlagen?";
            send.onClick.Invoke();
            session.Answer("Ja, Dienstag passt.", GoodLead, over: true, endReason: "Completed", commitment: "Appointment");
            yield return null;
            Assert.That(result.IsVisible, Is.True);
        }

        private static string Text(string name) =>
            GameObject.Find("ResultOverlay").transform.Find("Panel/" + name).GetComponent<TMP_Text>().text;
    }
}
