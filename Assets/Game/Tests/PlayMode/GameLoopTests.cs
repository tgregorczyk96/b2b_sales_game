using System.Collections;
using NUnit.Framework;
using SalesSim.Application;
using SalesSim.Game;
using SalesSim.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>
    /// The first game loop in SalesTestScene against a scripted session: character states, run end, result screen,
    /// selling the lead, balance and the next run. No engine, no API.
    /// </summary>
    public sealed class GameLoopTests
    {
        private ScriptedSession session;
        private SalesConversationController controller;
        private ConversationHistoryView history;
        private CharacterView character;
        private RunResultView result;
        private SalesRunLoop loop;
        private TMP_InputField input;
        private Button send;
        private Button end;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            controller = Object.FindAnyObjectByType<SalesConversationController>();
            history = Object.FindAnyObjectByType<ConversationHistoryView>();
            character = Object.FindAnyObjectByType<CharacterView>();
            result = Object.FindAnyObjectByType<RunResultView>();
            loop = Object.FindAnyObjectByType<SalesRunLoop>();
            input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            send = GameObject.Find("SendButton").GetComponent<Button>();
            end = GameObject.Find("EndConversationButton").GetComponent<Button>();

            session = new ScriptedSession();
            loop.Configure(session, new ScriptedScenarioSource(), null);
            loop.StartNewRun("Easy", 42);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Character_SpeaksThenThinks_WhileWaiting_ThenShowsTheMood()
        {
            Assert.That(character.State, Is.EqualTo(CharacterVisualState.Idle));

            Send("Hallo");
            yield return null;
            Assert.That(character.State, Is.EqualTo(CharacterVisualState.Speaking));

            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(character.State, Is.EqualTo(CharacterVisualState.Thinking));

            session.Answer("Schön, erzählen Sie.", new SalesIndicators(46, 55, 30, 70)); // +16 in total
            yield return null;
            Assert.That(character.State, Is.EqualTo(CharacterVisualState.VeryPositive));

            Send("Und?");
            session.Answer("Hm.", new SalesIndicators(44, 54, 29, 70)); // −4
            yield return null;
            Assert.That(character.State, Is.EqualTo(CharacterVisualState.Negative));

            Send("Noch was?");
            session.Answer("Ja.", new SalesIndicators(44, 54, 29, 70)); // ±0
            yield return null;
            Assert.That(character.State, Is.EqualTo(CharacterVisualState.Idle));
        }

        [UnityTest]
        public IEnumerator EveryCharacterState_ShowsItsOwnFullCanvasSprite()
        {
            var body = GameObject.Find("Body").GetComponent<Image>();
            var shown = new System.Collections.Generic.HashSet<Sprite>();
            foreach (CharacterVisualState state in System.Enum.GetValues(typeof(CharacterVisualState)))
            {
                character.SetState(state);
                yield return null;

                Assert.That(body.sprite, Is.Not.Null, state.ToString());
                Assert.That(body.sprite.name, Does.Contain($"_{state.ToString().ToLowerInvariant()}_v"), state.ToString());
                Assert.That(body.sprite.rect.width, Is.EqualTo(body.sprite.texture.width), "Not trimmed, so the states stay aligned.");
                Assert.That(body.sprite.rect.height, Is.EqualTo(body.sprite.texture.height), "Not trimmed, so the states stay aligned.");
                shown.Add(body.sprite);
            }

            Assert.That(shown.Count, Is.EqualTo(System.Enum.GetValues(typeof(CharacterVisualState)).Length));
        }

        [UnityTest]
        public IEnumerator FailedTurn_ReturnsTheCharacterToIdle()
        {
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("scripted failure"));
            Send("Hallo");
            session.Fail(new System.InvalidOperationException("scripted failure"));
            yield return null;

            Assert.That(character.State, Is.EqualTo(CharacterVisualState.Idle));
        }

        [UnityTest]
        public IEnumerator EngineEndsTheConversation_ResultScreen_Sell_Balance_NextRun()
        {
            Assert.That(result.IsVisible, Is.False);
            Send("Darf ich Ihnen nächste Woche einen Termin vorschlagen?");
            session.Answer("Ja, Dienstag passt.", new SalesIndicators(70, 72, 75, 60), over: true, endReason: "Completed", commitment: "Appointment");
            yield return null;

            Assert.That(result.IsVisible, Is.True, "Result screen appears when the run ends.");
            Assert.That(history.Messages.Count, Is.EqualTo(3), "The chat stays visible behind the result.");
            Assert.That(input.interactable, Is.False);
            Assert.That(loop.CurrentLead.Evaluation.Quality, Is.EqualTo(LeadQuality.Qualified));
            Assert.That(loop.CurrentLead.Evaluation.Value, Is.EqualTo(1000));
            Assert.That(Text("Payout"), Does.Contain("700"));
            Assert.That(result.CanSell, Is.True);

            result.SellButton.onClick.Invoke();
            Assert.That(loop.Wallet.Balance, Is.EqualTo(700));
            Assert.That(GameObject.Find("BalanceLabel").GetComponent<TMP_Text>().text, Is.EqualTo("Guthaben: 700 €"));
            Assert.That(result.CanSell, Is.False, "A lead can be sold only once.");
            result.SellButton.onClick.Invoke();
            Assert.That(loop.Wallet.Balance, Is.EqualTo(700));

            result.NextRunButton.onClick.Invoke();
            yield return null;
            Assert.That(result.IsVisible, Is.False);
            var setup = Object.FindAnyObjectByType<RunSetupView>();
            Assert.That(setup.IsVisible, Is.True, "Back to the run setup.");
            Assert.That(session.Starts, Is.EqualTo(1));

            setup.NewRunButton.onClick.Invoke();
            yield return null;
            Assert.That(session.Starts, Is.EqualTo(2), "A new run is started.");
            Assert.That(setup.IsVisible, Is.False);
            Assert.That(history.Messages.Count, Is.EqualTo(1));
            Assert.That(input.interactable, Is.True);
            Assert.That(character.State, Is.EqualTo(CharacterVisualState.Idle));
            Assert.That(loop.Wallet.Balance, Is.EqualTo(700), "The balance survives the next run.");
        }

        [UnityTest]
        public IEnumerator PlayerEndsTheConversation_WorthlessLead_CannotBeSold()
        {
            end.onClick.Invoke();
            yield return null;

            Assert.That(result.IsVisible, Is.True);
            Assert.That(loop.CurrentLead.Evaluation.Quality, Is.EqualTo(LeadQuality.Lost));
            Assert.That(result.CanSell, Is.False);
            Assert.That(Text("Value"), Does.Contain("0 €"));

            result.NextRunButton.onClick.Invoke();
            Object.FindAnyObjectByType<RunSetupView>().NewRunButton.onClick.Invoke();
            yield return null;
            Assert.That(loop.Wallet.Balance, Is.EqualTo(0));
            Assert.That(session.Starts, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator EndButton_IsLocked_WhileATurnIsRunning()
        {
            Send("Hallo");
            Assert.That(end.interactable, Is.False);
            end.onClick.Invoke();
            Assert.That(result.IsVisible, Is.False);

            session.Answer("Ja?");
            yield return null;
            Assert.That(end.interactable, Is.True);
        }

        private void Send(string text)
        {
            input.text = text;
            send.onClick.Invoke();
        }

        private static string Text(string name)
        {
            return GameObject.Find(name).GetComponent<TMP_Text>().text;
        }
    }
}
