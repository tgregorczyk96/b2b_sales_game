using System.Collections;
using System.IO;
using NUnit.Framework;
using SalesSim.Application;
using SalesSim.Game;
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
    /// The prepared website draft in SalesTestScene against a scripted session: choosing the prototype visit, the demo link at
    /// the end of the first visit, "7 Tage später" (no real waiting), the follow-up with the saved record, the deferred lead and
    /// the waiting follow-up surviving a restart. No engine, no API — the engine side is covered by the EditMode tests.
    /// </summary>
    public sealed class PrototypeFollowUpFlowTests
    {
        private const string Payload = "{\"engine\":\"first-visit-record\"}";

        private ScriptedSession session;
        private RunResultView result;
        private RunSetupView setup;
        private SalesRunLoop loop;
        private ConversationHistoryView history;
        private TMP_InputField input;
        private Button send;
        private string savePath;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            result = Object.FindAnyObjectByType<RunResultView>();
            setup = Object.FindAnyObjectByType<RunSetupView>();
            loop = Object.FindAnyObjectByType<SalesRunLoop>();
            history = Object.FindAnyObjectByType<ConversationHistoryView>();
            input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            send = GameObject.Find("SendButton").GetComponent<Button>();
            savePath = Path.Combine(Path.GetTempPath(), "salessim-followup-" + System.Guid.NewGuid().ToString("N") + ".json");

            session = new ScriptedSession();
            loop.Configure(session, new ScriptedScenarioSource(), null, new JsonFileGameStateStore(savePath));
            yield return null;
        }

        [TearDown]
        public void DeleteSave()
        {
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(savePath) + "*"))
            {
                File.Delete(file);
            }
        }

        private static WebsitePrototypeState Draft(bool presented = true, bool demo = true, bool followUp = false, string feedback = "", string decision = "Open") =>
            new WebsitePrototypeState(followUp ? (feedback.Length > 0 ? "FeedbackReceived" : "InReview") : demo ? "InReview" : presented ? "Presented" : "Prepared",
                presented, demo, followUp, feedback, feedback == "WantsChanges" ? 1 : 0, decision, 7);

        private void Send(string text)
        {
            input.text = text;
            send.onClick.Invoke();
        }

        private IEnumerator FirstVisitWithDemo()
        {
            setup.KindButton(ScriptedScenarioSource.PrototypeKind).onClick.Invoke();
            setup.SeedText = "5";
            setup.NewRunButton.onClick.Invoke();
            yield return null;

            Send("Ich schicke Ihnen den Demo-Link, schauen Sie ihn sich eine Woche an. Bis nächste Woche!");
            session.Answer(new SalesSessionState("scripted", "Gerne, tschüss!", new SalesIndicators(60, 60, 60, 60), "Ended", "EndConversation", true,
                "Completed", string.Empty, null, true, Draft(), new FollowUpDue(Payload, 7), leadDeferred: true));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FirstVisit_EndsWithTheDemoLink_AsASuccess_WithTheLeadInFollowUp()
        {
            yield return FirstVisitWithDemo();

            Assert.That(session.LastStart.ScenarioId, Is.EqualTo("scripted-prototype:5"));
            Assert.That(result.IsVisible, Is.True);
            Assert.That(result.ProductSummary, Does.Contain("Entwurf vorgestellt: Ja"));
            Assert.That(result.ProductSummary, Does.Contain("Demo-Link übergeben: Ja (7 Tage, unverbindlich)"));
            Assert.That(result.ProductSummary, Does.Contain("Nachfass: Folgegespräch in 7 Tagen offen"));
            Assert.That(GameObject.Find("Quality").GetComponent<TMP_Text>().text, Does.Contain("Lead im Nachfass"));
            Assert.That(result.CanSell, Is.False, "the website lead is priced after the follow-up");
            Assert.That(result.CanStartFollowUp, Is.True);
            Assert.That(result.FollowUpButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("7 Tage später: Folgegespräch"));
            Assert.That(loop.WaitingFollowUps, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SevenDaysLater_TheFollowUpStartsWithTheSavedRecord_AndANotice()
        {
            yield return FirstVisitWithDemo();

            result.FollowUpButton.onClick.Invoke();
            yield return null;

            Assert.That(session.Starts, Is.EqualTo(2));
            Assert.That(session.LastStart.IsFollowUp, Is.True);
            Assert.That(session.LastStart.FollowUpPayload, Is.EqualTo(Payload), "the engine's record is passed back unchanged");
            Assert.That(session.LastStart.ScenarioId, Is.EqualTo("scripted-prototype:5"));
            Assert.That(loop.CurrentRun.IsFollowUp && !loop.CurrentRun.IsRerun, Is.True);
            Assert.That(history.Messages[0].Text, Does.StartWith("7 Tage später"));
            Assert.That(history.Messages[1].Text, Is.EqualTo("Ach, hallo – Sie waren doch letzte Woche schon hier."));
            Assert.That(GameObject.Find("RunLabel").GetComponent<TMP_Text>().text, Does.Contain("Folgegespräch"));
            Assert.That(GameObject.Find("SaleStatusLabel").GetComponent<TMP_Text>().text, Does.Contain("Folgegespräch"));
            Assert.That(loop.WaitingFollowUps, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator FollowUp_ChangesThenOrder_ShowsFeedbackAndDecision_AndTheLeadIsSellable()
        {
            yield return FirstVisitWithDemo();
            result.FollowUpButton.onClick.Invoke();
            yield return null;

            Send("Mit Ihren Fotos – sollen wir es so umsetzen?");
            session.Answer(new SalesSessionState("scripted", "Ja, machen wir.", new SalesIndicators(70, 72, 75, 60), "Ended", "Continue", true, "Completed",
                "Agreement", null, true, Draft(followUp: true, feedback: "WantsChanges", decision: "Bought")));
            yield return null;

            Assert.That(result.ProductSummary, Does.Contain("Website-Prototyp – Folgegespräch"));
            Assert.That(result.ProductSummary, Does.Contain("Feedback zur Demo: wünscht Änderungen"));
            Assert.That(result.ProductSummary, Does.Contain("Entscheidung: Website gekauft"));
            Assert.That(loop.CurrentLead.Evaluation.Value, Is.EqualTo(1000));
            result.SellButton.onClick.Invoke();
            Assert.That(loop.Wallet.Balance, Is.EqualTo(700), "the website lead is paid once, after the follow-up");
            Assert.That(result.CanStartFollowUp, Is.False);
        }

        [UnityTest]
        public IEnumerator AWaitingFollowUp_SurvivesARestart_AndIsOfferedInTheSetup()
        {
            yield return FirstVisitWithDemo();
            result.NextRunButton.onClick.Invoke();
            yield return null;
            Assert.That(setup.FollowUpButton.gameObject.activeSelf, Is.True);

            // A new game session with the same save: the follow-up still waits.
            var restarted = new ScriptedSession();
            loop.Configure(restarted, new ScriptedScenarioSource(), null, new JsonFileGameStateStore(savePath));
            yield return null;

            Assert.That(loop.WaitingFollowUps, Is.EqualTo(1));
            Assert.That(setup.FollowUpButton.gameObject.activeSelf, Is.True);
            setup.FollowUpButton.onClick.Invoke();
            yield return null;
            Assert.That(restarted.LastStart.FollowUpPayload, Is.EqualTo(Payload));
            Assert.That(restarted.LastStart.ScenarioId, Is.EqualTo("scripted-prototype:5"));
        }

        [UnityTest]
        public IEnumerator WithoutADraft_NoFollowUpIsOffered()
        {
            loop.StartNewRun("Easy", 42);
            yield return null;
            Send("Termin?");
            session.Answer("Ja, Dienstag.", new SalesIndicators(70, 72, 75, 60), over: true, endReason: "Completed", commitment: "Appointment");
            yield return null;

            Assert.That(result.CanStartFollowUp, Is.False);
            Assert.That(loop.WaitingFollowUps, Is.EqualTo(0));
            Assert.That(result.ProductSummary, Is.Empty);
        }
    }
}
