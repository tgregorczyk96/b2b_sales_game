using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SalesSim.Application;
using SalesSim.EngineAdapter;
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
    /// End-to-end smoke test of SalesTestScene against the real Sales Engine: starts the fixed origin scenario, sends two
    /// player turns through the UI and checks chat history, thinking indicator and engine state in the UI.
    /// With SALESSIM_ANTHROPIC_API_KEY set (environment or .env) this calls the real Anthropic API, so it only runs when
    /// selected explicitly; without a key the engine's placeholder customer answers.
    /// </summary>
    [Explicit("Calls the real Anthropic API when SALESSIM_ANTHROPIC_API_KEY is set.")]
    [Category("LiveEngine")]
    public sealed class SalesEngineSmokeTest
    {
        private static readonly string[] Sentences =
        {
            "Guten Tag, mein Name ist Max Berger von Webwerk. Haben Sie kurz zwei Minuten? Es geht darum, wie neue Kundinnen Ihren Salon online finden.",
            "Wie finden denn neue Kundinnen bisher zu Ihnen, eher über Empfehlungen oder auch über Google?",
        };

        private const float TimeoutSeconds = 120f;

        private readonly List<string> clientLog = new List<string>();

        [SetUp]
        public void CaptureClientLog()
        {
            UnityEngine.Application.logMessageReceivedThreaded += OnLog;
        }

        [TearDown]
        public void StopCapturing()
        {
            UnityEngine.Application.logMessageReceivedThreaded -= OnLog;
        }

        [UnityTest]
        public IEnumerator RealEngine_TwoTurns_ShowInHistory_WithThinkingAndEngineState()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            var input = Find<TMP_InputField>("PlayerInput");
            var send = Find<Button>("SendButton");
            var history = Object.FindAnyObjectByType<ConversationHistoryView>();

            // The game starts at the run setup. Same engine session as the bootstrap builds (real engine, key from
            // environment / .env), but without a savegame, so the smoke test never touches the player's progress.
            var contentDirectory = Path.Combine(UnityEngine.Application.streamingAssetsPath, "SalesEngine", "content");
            var settings = LocalSettings.Load(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), ".env"));
            var session = SalesEngineGameSession.Create(contentDirectory, settings.Get, message => Debug.Log(message));
            var loop = Object.FindAnyObjectByType<SalesRunLoop>();
            loop.Configure(session, new EngineScenarioSource(contentDirectory), new PlayerProfile("Max Berger", "Webwerk"));
            loop.StartNewRun("Easy", 1406361028);

            yield return WaitUntil(() => input.interactable && history.Messages.Count == 1, "the session to start");
            Assert.That(history.Messages[0].Sender, Is.EqualTo(ConversationSender.Customer));
            Assert.That(history.Messages[0].Text, Is.Not.Empty.And.Not.Contains("nicht verbunden"));
            Debug.Log($"[Smoke] Kunde: \"{history.Messages[0].Text}\" | {DebugPanel()}");

            foreach (var sentence in Sentences)
            {
                var countBefore = history.Messages.Count;
                var panelBefore = DebugPanel();
                input.text = sentence;
                send.onClick.Invoke();

                Assert.That(history.Messages.Count, Is.EqualTo(countBefore + 1), "Player message is shown immediately.");
                Assert.That(history.Messages.Last().Sender, Is.EqualTo(ConversationSender.Player));
                Assert.That(history.IsThinking, Is.True, "Thinking indicator appears right after sending.");
                Assert.That(send.interactable, Is.False, "No second turn while waiting.");

                var started = Time.realtimeSinceStartup;
                yield return WaitUntil(() => history.Messages.Count == countBefore + 2, "the customer's reply");
                var waited = Time.realtimeSinceStartup - started;

                // A failed reply shows a notice instead: report its text, which says what is wrong (key, workspace, ...).
                Assert.That(history.Messages.Last().Sender, Is.EqualTo(ConversationSender.Customer),
                    $"No customer reply. The game says: \"{history.Messages.Last().Text}\"");
                Assert.That(history.IsThinking, Is.False, "Thinking indicator is gone after the reply.");
                Assert.That(send.interactable, Is.True);
                Debug.Log($"[Smoke] Du: \"{sentence}\"");
                Debug.Log($"[Smoke] Kunde ({waited:0.0}s, thinking shown meanwhile): \"{history.Messages.Last().Text}\"");
                Debug.Log($"[Smoke] {DebugPanel()} (before: {panelBefore})");

                if (Logged("[SalesClient] Fallback used: no"))
                {
                    Assert.That(Find<TMP_Text>("IntentText").text, Is.Not.EqualTo("Intent: -"), "The LLM customer always reports an intent.");
                }
            }

            Assert.That(history.Messages.Count, Is.EqualTo(1 + 2 * Sentences.Length), "All turns stay visible.");
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            lock (clientLog)
            {
                clientLog.Add(message);
            }
        }

        private bool Logged(string line)
        {
            lock (clientLog)
            {
                return clientLog.Contains(line);
            }
        }

        private static string DebugPanel()
        {
            return string.Join(" | ",
                Find<TMP_Text>("TrustText").text,
                Find<TMP_Text>("OpennessText").text,
                Find<TMP_Text>("EngagementText").text,
                Find<TMP_Text>("PatienceText").text,
                Find<TMP_Text>("StageText").text,
                Find<TMP_Text>("IntentText").text);
        }

        private static T Find<T>(string name) where T : Component
        {
            var gameObject = GameObject.Find(name);
            Assert.That(gameObject, Is.Not.Null, $"'{name}' not found in SalesTestScene.");
            return gameObject.GetComponent<T>();
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail($"Timed out after {TimeoutSeconds}s waiting for {what}.");
                }

                yield return null;
            }
        }
    }
}
