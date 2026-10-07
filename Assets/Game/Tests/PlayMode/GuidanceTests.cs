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
    /// Live guidance UI in SalesTestScene against a scripted session: buttons, guidance panel (never the chat), loading
    /// and errors, locking against parallel requests, usage counting and the result screen. No engine, no API — the
    /// engine mapping and the AI path are covered by <c>SalesEngineGameSessionTests</c> (EditMode).
    /// </summary>
    public sealed class GuidanceTests
    {
        private ScriptedSession session;
        private SalesConversationController controller;
        private ConversationHistoryView history;
        private GuidancePanelView guidance;
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
            guidance = Object.FindAnyObjectByType<GuidancePanelView>();
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
        public IEnumerator Buttons_AreAvailable_DuringTheConversation_WithGermanLabels()
        {
            Assert.That(guidance.IsInteractable, Is.True);
            Assert.That(Label(GuidanceLevel.Hint), Is.EqualTo("Hinweis"));
            Assert.That(Label(GuidanceLevel.HintMore), Is.EqualTo("Mehr Hinweis"));
            Assert.That(Label(GuidanceLevel.Example), Is.EqualTo("Beispiel"));
            Assert.That(guidance.StateOf(GuidanceLevel.Hint), Is.EqualTo(GuidanceSectionState.Hidden));
            yield break;
        }

        [UnityTest]
        public IEnumerator Hint_ShowsTheGuidance_InThePanel_NotInTheChat()
        {
            Press(GuidanceLevel.Hint);
            yield return null;

            Assert.That(session.GuidanceRequests, Is.EqualTo(new[] { GuidanceLevel.Hint }));
            Assert.That(guidance.StateOf(GuidanceLevel.Hint), Is.EqualTo(GuidanceSectionState.Shown));
            Assert.That(guidance.TextOf(GuidanceLevel.Hint), Is.EqualTo(ScriptedSession.HintText));
            AssertChatHasNoGuidance();
            Assert.That(session.TurnsReceived, Is.EqualTo(0), "Guidance is never sent to the customer.");
        }

        [UnityTest]
        public IEnumerator HintMore_ShowsTheConcreteGuidance_BesideTheHint()
        {
            Press(GuidanceLevel.Hint);
            Press(GuidanceLevel.HintMore);
            yield return null;

            Assert.That(guidance.TextOf(GuidanceLevel.Hint), Is.EqualTo(ScriptedSession.HintText), "The hint stays visible.");
            Assert.That(guidance.TextOf(GuidanceLevel.HintMore), Is.EqualTo(ScriptedSession.HintMoreText));
            AssertChatHasNoGuidance();
        }

        [UnityTest]
        public IEnumerator Example_ShowsLoading_ThenTheFullSentence()
        {
            Press(GuidanceLevel.Example);
            yield return null;

            Assert.That(guidance.StateOf(GuidanceLevel.Example), Is.EqualTo(GuidanceSectionState.Loading));
            Assert.That(guidance.TextOf(GuidanceLevel.Example), Does.StartWith("Beispiel wird formuliert"));
            Assert.That(controller.IsGuidanceInProgress, Is.True);

            session.AnswerExample();
            yield return null;

            Assert.That(guidance.StateOf(GuidanceLevel.Example), Is.EqualTo(GuidanceSectionState.Shown));
            Assert.That(guidance.TextOf(GuidanceLevel.Example), Is.EqualTo(ScriptedSession.ExampleText));
            Assert.That(controller.IsGuidanceInProgress, Is.False);
            AssertChatHasNoGuidance();
        }

        [UnityTest]
        public IEnumerator WhileTheExampleLoads_NoSecondRequest_NoSendOrEnd_ButTypingGoesOn()
        {
            Press(GuidanceLevel.Example);
            yield return null;

            Assert.That(guidance.IsInteractable, Is.False);
            Assert.That(send.interactable, Is.False);
            Assert.That(end.interactable, Is.False);
            Assert.That(input.interactable, Is.True, "The player can keep writing while the example is generated.");

            Press(GuidanceLevel.Example);
            Press(GuidanceLevel.Hint);
            input.text = "Hallo";
            send.onClick.Invoke();
            end.onClick.Invoke();
            Assert.That(session.GuidanceRequests.Count, Is.EqualTo(1), "No parallel guidance request.");
            Assert.That(session.TurnsReceived, Is.EqualTo(0));
            Assert.That(result.IsVisible, Is.False);

            session.AnswerExample();
            yield return null;
            Assert.That(guidance.IsInteractable, Is.True);
            Assert.That(send.interactable, Is.True);
            Assert.That(input.text, Is.EqualTo("Hallo"), "The draft survives the guidance.");
        }

        [UnityTest]
        public IEnumerator Guidance_IsLocked_WhileACustomerTurnIsRunning()
        {
            input.text = "Hallo";
            send.onClick.Invoke();

            Assert.That(guidance.IsInteractable, Is.False);
            Press(GuidanceLevel.Hint);
            Press(GuidanceLevel.Example);
            Assert.That(session.GuidanceRequests, Is.Empty);

            session.Answer("Ja, bitte?");
            yield return null;
            Assert.That(guidance.IsInteractable, Is.True);
        }

        [UnityTest]
        public IEnumerator FailedExample_ShowsAnErrorInThePanel_NotInTheChat_AndIsNotCounted()
        {
            Press(GuidanceLevel.Example);
            session.FailExample();
            yield return null;

            Assert.That(guidance.StateOf(GuidanceLevel.Example), Is.EqualTo(GuidanceSectionState.Error));
            Assert.That(guidance.TextOf(GuidanceLevel.Example), Does.Contain("nicht erstellt"));
            AssertChatHasNoGuidance();
            Assert.That(controller.GuidanceUsage.ExampleCount, Is.EqualTo(0));
            Assert.That(guidance.IsInteractable, Is.True, "The request can be repeated.");

            Press(GuidanceLevel.Example);
            session.AnswerExample();
            yield return null;
            Assert.That(guidance.TextOf(GuidanceLevel.Example), Is.EqualTo(ScriptedSession.ExampleText));
            Assert.That(controller.GuidanceUsage.ExampleCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ExampleWithoutAi_IsDisabledBeforeTheClick_WithANote()
        {
            session.Availability[GuidanceLevel.Example] = GuidanceAvailability.RequiresAi;
            loop.StartNewRun("Easy", 43);
            yield return null;

            Assert.That(guidance.ButtonFor(GuidanceLevel.Example).interactable, Is.False);
            Assert.That(Label(GuidanceLevel.Example), Does.Contain("nur mit KI"));
            Assert.That(guidance.ButtonFor(GuidanceLevel.Hint).interactable, Is.True, "Hinweis works without AI.");

            Press(GuidanceLevel.Example);
            yield return null;
            Assert.That(session.GuidanceRequests, Is.Empty);
            Assert.That(guidance.StateOf(GuidanceLevel.Example), Is.EqualTo(GuidanceSectionState.Hidden));
            Assert.That(controller.GuidanceUsage.Total, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator RepeatedRequests_Work_AndAreCountedPerLevel()
        {
            Press(GuidanceLevel.Hint);
            Press(GuidanceLevel.Hint);
            Press(GuidanceLevel.HintMore);
            Press(GuidanceLevel.Example);
            session.AnswerExample();
            yield return null;
            Press(GuidanceLevel.Hint);
            yield return null;

            Assert.That(session.GuidanceRequests.Count, Is.EqualTo(5));
            Assert.That(controller.GuidanceUsage.HintCount, Is.EqualTo(3));
            Assert.That(controller.GuidanceUsage.HintMoreCount, Is.EqualTo(1));
            Assert.That(controller.GuidanceUsage.ExampleCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SendingATurn_UsesUpTheGuidance_ButKeepsTheCount()
        {
            Press(GuidanceLevel.Hint);
            input.text = "Haben Sie freie Termine am Dienstag?";
            send.onClick.Invoke();

            Assert.That(guidance.StateOf(GuidanceLevel.Hint), Is.EqualTo(GuidanceSectionState.Hidden));
            session.Answer("Dienstags ist es eher ruhig.");
            yield return null;

            Assert.That(controller.GuidanceUsage.HintCount, Is.EqualTo(1));
            Press(GuidanceLevel.Hint);
            Assert.That(guidance.TextOf(GuidanceLevel.Hint), Is.EqualTo(ScriptedSession.HintText), "Guidance for the next turn.");
        }

        [UnityTest]
        public IEnumerator ResultScreen_ShowsTheUsage_AndTheNextRunStartsAtZero()
        {
            Press(GuidanceLevel.Hint);
            Press(GuidanceLevel.Hint);
            Press(GuidanceLevel.HintMore);
            Press(GuidanceLevel.Example);
            session.AnswerExample();
            yield return null;

            end.onClick.Invoke();
            yield return null;

            Assert.That(result.IsVisible, Is.True);
            var text = GuidanceResultText();
            Assert.That(text, Does.Contain("Guidance genutzt"));
            Assert.That(text, Does.Contain("Hinweise: 2"));
            Assert.That(text, Does.Contain("Mehr Hinweise: 1"));
            Assert.That(text, Does.Contain("Beispiele: 1"));
            Assert.That(text, Does.Not.Contain("Run ohne Hilfe"));

            result.NextRunButton.onClick.Invoke();
            Object.FindAnyObjectByType<RunSetupView>().NewRunButton.onClick.Invoke();
            yield return null;
            Assert.That(controller.GuidanceUsage.Total, Is.EqualTo(0));
            Assert.That(guidance.StateOf(GuidanceLevel.Hint), Is.EqualTo(GuidanceSectionState.Hidden));
            Assert.That(guidance.StateOf(GuidanceLevel.Example), Is.EqualTo(GuidanceSectionState.Hidden));
        }

        [UnityTest]
        public IEnumerator ResultScreen_WithoutGuidance_ShowsRunOhneHilfe()
        {
            end.onClick.Invoke();
            yield return null;

            var text = GuidanceResultText();
            Assert.That(text, Does.Contain("Hinweise: 0"));
            Assert.That(text, Does.Contain("Mehr Hinweise: 0"));
            Assert.That(text, Does.Contain("Beispiele: 0"));
            Assert.That(text, Does.Contain("Run ohne Hilfe"));
        }

        [UnityTest]
        public IEnumerator Guidance_IsLocked_AfterTheConversationEnded()
        {
            end.onClick.Invoke();
            yield return null;

            Assert.That(guidance.IsInteractable, Is.False);
            Press(GuidanceLevel.Hint);
            Assert.That(session.GuidanceRequests, Is.Empty);
        }

        private void Press(GuidanceLevel level)
        {
            guidance.ButtonFor(level).onClick.Invoke();
        }

        private string Label(GuidanceLevel level)
        {
            return guidance.ButtonFor(level).GetComponentInChildren<TMP_Text>().text;
        }

        private void AssertChatHasNoGuidance()
        {
            var texts = history.Messages.Select(m => m.Text).ToList();
            Assert.That(texts, Does.Not.Contain(ScriptedSession.HintText));
            Assert.That(texts, Does.Not.Contain(ScriptedSession.HintMoreText));
            Assert.That(texts, Does.Not.Contain(ScriptedSession.ExampleText));
            Assert.That(history.Messages.Count(m => m.Sender == ConversationSender.Customer), Is.EqualTo(1), "Only the opening line.");
        }

        private static string GuidanceResultText()
        {
            return GameObject.Find("ResultOverlay").transform.Find("Panel/Guidance").GetComponent<TMP_Text>().text;
        }
    }
}
