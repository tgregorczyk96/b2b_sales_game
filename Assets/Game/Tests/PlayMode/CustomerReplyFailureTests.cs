using System.Collections;
using System.Linq;
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
    /// What the player sees when the customer cannot answer (expired key, key without workspace, timeout, ...): the line
    /// stays marked "nicht zugestellt", a notice in the chat says why and what to do, nothing is invented, the input is free
    /// again and the same line can be sent once more. Scripted session; the engine mapping is covered in EditMode
    /// (<c>SalesEngineGameSessionTests</c>).
    /// </summary>
    public sealed class CustomerReplyFailureTests
    {
        private ScriptedSession session;
        private ConversationHistoryView history;
        private CharacterView character;
        private TMP_InputField input;
        private Button send;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            history = Object.FindAnyObjectByType<ConversationHistoryView>();
            character = Object.FindAnyObjectByType<CharacterView>();
            input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            send = GameObject.Find("SendButton").GetComponent<Button>();

            session = new ScriptedSession();
            var loop = Object.FindAnyObjectByType<SalesRunLoop>();
            loop.Configure(session, new ScriptedScenarioSource(), null);
            loop.StartNewRun("Easy", 42);
            yield return null;
        }

        [TestCase(SalesTurnFailure.WorkspaceRequired, "SALESSIM_ANTHROPIC_WORKSPACE_ID", ExpectedResult = null)]
        [TestCase(SalesTurnFailure.Authentication, "API-Key", ExpectedResult = null)]
        [TestCase(SalesTurnFailure.Timeout, "nicht rechtzeitig", ExpectedResult = null)]
        [TestCase(SalesTurnFailure.Network, "nicht erreichbar", ExpectedResult = null)]
        [TestCase(SalesTurnFailure.QuotaExceeded, "Guthaben", ExpectedResult = null)]
        [UnityTest]
        public IEnumerator FailedReply_IsExplainedInTheChat_AndTheInputIsFreeAgain(SalesTurnFailure failure, string expected)
        {
            Send("Guten Tag, haben Sie kurz Zeit?");
            session.Fail(new SalesTurnFailedException(failure, "scripted"));
            yield return null;

            var notice = history.Messages.Last();
            Assert.That(notice.Sender, Is.EqualTo(ConversationSender.Notice));
            Assert.That(notice.SenderLabel, Is.EqualTo("Hinweis"));
            Assert.That(notice.Text, Does.Contain(expected));
            Assert.That(history.Messages.Count(m => m.Sender == ConversationSender.Customer), Is.EqualTo(1), "No reply is invented.");
            Assert.That(history.Messages.Single(m => m.Sender == ConversationSender.Player).SenderLabel, Does.Contain("nicht zugestellt"));
            Assert.That(history.IsThinking, Is.False);
            Assert.That(character.State, Is.EqualTo(CharacterVisualState.Idle));
            Assert.That(input.interactable, Is.True, "The input is never left blocked.");
            Assert.That(send.interactable, Is.True);
            Assert.That(input.text, Is.EqualTo("Guten Tag, haben Sie kurz Zeit?"), "Offered again for a retry.");
        }

        [UnityTest]
        public IEnumerator Retry_AfterAFailure_IsANormalTurn_WithTheCustomersReply()
        {
            Send("Guten Tag");
            session.Fail(new SalesTurnFailedException(SalesTurnFailure.Timeout, "scripted"));
            yield return null;

            send.onClick.Invoke(); // the text is still in the input
            yield return null;
            session.Answer("Ja, worum geht's?", new SalesIndicators(45, 55, 30, 68));
            yield return null;

            Assert.That(session.TurnsReceived, Is.EqualTo(2));
            Assert.That(history.Messages.Last().Sender, Is.EqualTo(ConversationSender.Customer));
            Assert.That(history.Messages.Last().Text, Is.EqualTo("Ja, worum geht's?"));
            Assert.That(session.CurrentState.Indicators.Trust, Is.EqualTo(45));
            Assert.That(input.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator WhileTheCustomerThinks_SendingAgain_IsNotASecondTurn()
        {
            Send("Guten Tag");
            Send("Guten Tag");
            send.onClick.Invoke();
            yield return null;

            Assert.That(session.TurnsReceived, Is.EqualTo(1));
            Assert.That(send.interactable, Is.False);
            Assert.That(input.interactable, Is.False);

            session.Answer("Hallo?", 40);
            yield return null;
            Assert.That(input.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator UnexpectedError_IsStillExplained_AndReleasesTheInput()
        {
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("scripted crash"));
            Send("Hallo");
            session.Fail(new System.InvalidOperationException("scripted crash"));
            yield return null;

            Assert.That(history.Messages.Last().Sender, Is.EqualTo(ConversationSender.Notice));
            Assert.That(history.Messages.Last().Text, Does.Contain("konnte nicht antworten"));
            Assert.That(input.interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator CancelledRequest_WhileTheSceneLives_DoesNotBlockTheInput()
        {
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("canceled|cancelled", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            Send("Hallo");
            session.Fail(new System.OperationCanceledException("cancelled by the provider"));
            yield return null;

            Assert.That(history.IsThinking, Is.False);
            Assert.That(input.interactable, Is.True, "Before the fix, a stray cancellation left the input disabled for good.");
            Assert.That(send.interactable, Is.True);
        }

        private void Send(string text)
        {
            input.text = text;
            send.onClick.Invoke();
        }
    }
}
