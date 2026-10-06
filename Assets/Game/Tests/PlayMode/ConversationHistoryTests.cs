using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
    /// Chat history UI of SalesTestScene against a scripted <see cref="ISalesGameSession"/>: deterministic timing,
    /// no engine or API involved. The live end-to-end run is <see cref="SalesEngineSmokeTest"/>.
    /// </summary>
    public sealed class ConversationHistoryTests
    {
        private ScriptedSession session;
        private SalesConversationController controller;
        private ConversationHistoryView history;
        private TMP_InputField input;
        private Button send;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null; // let the scene's bootstrap run first; the scripted session then replaces its session

            controller = UnityEngine.Object.FindAnyObjectByType<SalesConversationController>();
            history = UnityEngine.Object.FindAnyObjectByType<ConversationHistoryView>();
            input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            send = GameObject.Find("SendButton").GetComponent<Button>();

            session = new ScriptedSession();
            controller.Initialize(session, "scripted");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpeningLine_IsTheFirstCustomerMessage()
        {
            Assert.That(history.Messages.Count, Is.EqualTo(1));
            Assert.That(history.Messages[0].Sender, Is.EqualTo(ConversationSender.Customer));
            Assert.That(history.Messages[0].SenderLabel, Is.EqualTo("Kunde"));
            Assert.That(history.Messages[0].Text, Is.EqualTo("Guten Tag?"));
            yield break;
        }

        [UnityTest]
        public IEnumerator PlayerMessage_AppearsImmediately_WithThinkingAndLockedInput_ThenTheReply()
        {
            Send("Hallo, haben Sie kurz Zeit?");

            // Before the session has answered.
            Assert.That(history.Messages.Last().Sender, Is.EqualTo(ConversationSender.Player));
            Assert.That(history.Messages.Last().SenderLabel, Is.EqualTo("Du"));
            Assert.That(history.Messages.Last().Text, Is.EqualTo("Hallo, haben Sie kurz Zeit?"));
            Assert.That(history.IsThinking, Is.True);
            Assert.That(input.interactable, Is.False);
            Assert.That(send.interactable, Is.False);
            Assert.That(controller.IsTurnInProgress, Is.True);

            yield return null;
            Assert.That(history.IsThinking, Is.True, "Thinking stays visible for the whole request.");

            session.Answer("Worum geht es denn?", trust: 44);
            yield return null;

            Assert.That(history.Messages.Count, Is.EqualTo(3));
            Assert.That(history.Messages.Last().Sender, Is.EqualTo(ConversationSender.Customer));
            Assert.That(history.Messages.Last().Text, Is.EqualTo("Worum geht es denn?"));
            Assert.That(history.IsThinking, Is.False);
            Assert.That(input.interactable, Is.True);
            Assert.That(send.interactable, Is.True);
            Assert.That(GameObject.Find("TrustText").GetComponent<TMP_Text>().text, Is.EqualTo("Trust: 44"));
        }

        [UnityTest]
        public IEnumerator SecondSend_WhileWaiting_IsIgnored()
        {
            Send("Erster Satz");
            input.text = "Zweiter Satz";
            send.onClick.Invoke();

            Assert.That(session.TurnsReceived, Is.EqualTo(1));
            Assert.That(history.Messages.Count(m => m.Sender == ConversationSender.Player), Is.EqualTo(1));

            session.Answer("Okay.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SeveralTurns_StayVisibleInOrder()
        {
            Send("Eins");
            session.Answer("A");
            yield return null;
            Send("Zwei");
            session.Answer("B");
            yield return null;

            Assert.That(history.Messages.Select(m => m.Text), Is.EqualTo(new[] { "Guten Tag?", "Eins", "A", "Zwei", "B" }));
        }

        [UnityTest]
        public IEnumerator FailedTurn_KeepsThePlayerMessage_InventsNoReply_AndReleasesTheInput()
        {
            LogAssert.Expect(LogType.Exception, new Regex("scripted failure"));
            Send("Hallo");
            session.Fail(new InvalidOperationException("scripted failure"));
            yield return null;

            Assert.That(history.Messages.Count, Is.EqualTo(2));
            Assert.That(history.Messages.Last().Sender, Is.EqualTo(ConversationSender.Player));
            Assert.That(history.Messages.Last().Text, Is.EqualTo("Hallo"));
            Assert.That(history.Messages.Last().SenderLabel, Does.Contain("nicht zugestellt"));
            Assert.That(history.IsThinking, Is.False);
            Assert.That(input.interactable, Is.True);
            Assert.That(send.interactable, Is.True);
            Assert.That(input.text, Is.EqualTo("Hallo"), "The text is offered again for a retry.");
        }

        [UnityTest]
        public IEnumerator MessageText_CanBeSelectedAndCopied_ButNotEdited()
        {
            Send("Bitte kopier mich");
            session.Answer("Kopier mich auch");
            yield return null;

            foreach (var message in history.Messages.Skip(1))
            {
                var body = message.GetComponentInChildren<SelectableMessageText>();
                Assert.That(body.readOnly, Is.True);
                Assert.That(body.interactable, Is.True, "Selectable with the mouse.");

                body.ActivateInputField();
                yield return null;
                body.stringPosition = 0;
                body.selectionStringAnchorPosition = 0;
                body.selectionStringFocusPosition = body.text.Length;
                GUIUtility.systemCopyBuffer = string.Empty;
                body.ProcessEvent(Event.KeyboardEvent("^c"));
                Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(message.Text), "Ctrl+C copies the selection.");

                var before = body.text;
                body.ProcessEvent(Event.KeyboardEvent("x"));
                body.ProcessEvent(Event.KeyboardEvent("backspace"));
                body.ProcessEvent(Event.KeyboardEvent("^v"));
                Assert.That(body.text, Is.EqualTo(before), "Typing, deleting and pasting change nothing.");
                body.DeactivateInputField();
            }
        }

        [UnityTest]
        public IEnumerator History_ScrollsManually_FollowsAtTheBottom_AndKeepsAPositionTheUserChose()
        {
            for (var i = 0; i < 8; i++)
            {
                Send($"Nachricht {i} mit etwas mehr Text, damit der Verlauf sicher länger wird als der sichtbare Bereich.");
                session.Answer($"Antwort {i} mit ebenfalls etwas mehr Text, damit gescrollt werden muss.");
                yield return null;
            }

            yield return Frames(3);
            var scroll = history.ScrollRect;
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height), "Precondition: content overflows.");
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(0.01f), "Follows new messages while at the bottom.");

            scroll.verticalNormalizedPosition = 1f; // the user scrolls to the top
            yield return Frames(3);
            Assert.That(scroll.verticalNormalizedPosition, Is.GreaterThan(0.99f), "Manual scrolling works.");

            Send("Noch eine");
            session.Answer("Und noch eine");
            yield return Frames(3);
            Assert.That(scroll.verticalNormalizedPosition, Is.GreaterThan(0.5f), "A user who scrolled up keeps the position.");

            scroll.verticalNormalizedPosition = 0f; // back at the bottom
            yield return Frames(3);
            Send("Letzte");
            session.Answer("Letzte Antwort");
            yield return Frames(3);
            Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(0.01f), "Follows again once back at the bottom.");
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        private void Send(string text)
        {
            input.text = text;
            send.onClick.Invoke();
        }

        /// <summary>Answers each turn only when the test says so.</summary>
        private sealed class ScriptedSession : ISalesGameSession
        {
            private TaskCompletionSource<SalesSessionState> pending;

            public SalesSessionState CurrentState { get; private set; } = SalesSessionState.Empty;

            public int TurnsReceived { get; private set; }

            public Task<SalesSessionState> StartSessionAsync(SessionStartRequest request, CancellationToken cancellationToken = default)
            {
                CurrentState = State("Guten Tag?", 40);
                return Task.FromResult(CurrentState);
            }

            public Task<SalesSessionState> SendPlayerTurnAsync(PlayerTurn turn, CancellationToken cancellationToken = default)
            {
                TurnsReceived++;
                pending = new TaskCompletionSource<SalesSessionState>();
                return pending.Task;
            }

            public void Answer(string customerMessage, float trust = 40)
            {
                CurrentState = State(customerMessage, trust);
                pending.SetResult(CurrentState);
            }

            public void Fail(Exception exception)
            {
                pending.SetException(exception);
            }

            private static SalesSessionState State(string customerMessage, float trust)
            {
                return new SalesSessionState("scripted", customerMessage, new SalesIndicators(trust, 50, 25, 70), "Opening", "Continue", false);
            }
        }
    }
}
