using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>
    /// End-to-end smoke test of SalesTestScene against the real Sales Engine: starts the fixed origin scenario, sends one
    /// player turn through the UI and checks that the engine's reply and state come back to the UI.
    /// No LLM yet: the engine's placeholder customer answers, so Intent stays empty (logged, not asserted).
    /// </summary>
    public sealed class SalesEngineSmokeTest
    {
        private const string TestSentence =
            "Guten Tag, mein Name ist Max Berger von Webwerk. Haben Sie kurz zwei Minuten? Es geht darum, wie neue Kundinnen Ihren Salon online finden.";

        private const float TimeoutSeconds = 30f;

        [UnityTest]
        public IEnumerator RealEngine_ProcessesPlayerTurn_AndUiShowsEngineState()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");

            var input = Find<TMP_InputField>("PlayerInput");
            var send = Find<Button>("SendButton");
            var customer = Find<TMP_Text>("CustomerText");

            yield return WaitUntil(() => input.interactable, "the session to start");
            var opening = customer.text;
            var panelBefore = DebugPanel();
            Debug.Log($"[Smoke] Session started. Customer: \"{opening}\" | {panelBefore}");
            Assert.That(opening, Is.Not.Empty.And.Not.Contains("nicht verbunden"), "Opening line should come from the engine.");
            Assert.That(Find<TMP_Text>("StageText").text, Is.EqualTo("Stage: Opening"));

            input.text = TestSentence;
            send.onClick.Invoke();
            yield return WaitUntil(() => input.interactable && customer.text != opening, "the customer's reply");

            var panelAfter = DebugPanel();
            Debug.Log($"[Smoke] Player: \"{TestSentence}\"");
            Debug.Log($"[Smoke] Customer: \"{customer.text}\"");
            Debug.Log($"[Smoke] {panelAfter}");

            Assert.That(customer.text, Is.Not.Empty);
            Assert.That(panelAfter, Is.Not.EqualTo(panelBefore), "The engine should have changed the customer state.");
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
