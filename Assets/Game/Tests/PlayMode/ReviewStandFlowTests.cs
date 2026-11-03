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
    /// The review stand in SalesTestScene against a scripted session: choosing the on-site visit, the live sale status, the
    /// result screen (sold, set up, payment method and status, further course), booking only received revenue, and the website
    /// as a separate lead. No engine, no API — the engine side is covered by the EditMode ReviewStandSaleTests.
    /// </summary>
    public sealed class ReviewStandFlowTests
    {
        private ScriptedSession session;
        private ScriptedScenarioSource scenarios;
        private RunResultView result;
        private RunSetupView setup;
        private SalesRunLoop loop;
        private TMP_InputField input;
        private Button send;
        private Button end;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("SalesTestScene");
            yield return null;

            result = Object.FindAnyObjectByType<RunResultView>();
            setup = Object.FindAnyObjectByType<RunSetupView>();
            loop = Object.FindAnyObjectByType<SalesRunLoop>();
            input = GameObject.Find("PlayerInput").GetComponent<TMP_InputField>();
            send = GameObject.Find("SendButton").GetComponent<Button>();
            end = GameObject.Find("EndConversationButton").GetComponent<Button>();

            session = new ScriptedSession();
            scenarios = new ScriptedScenarioSource();
            loop.Configure(session, scenarios, null);
            yield return null;
        }

        private static ProductSaleState Sale(
            bool sold = true, int setupDone = 4, string method = "Cash", string payment = "Confirmed", string followUp = "", string followUpCommitment = "")
        {
            var paid = sold && payment == "Confirmed";
            return new ProductSaleState(
                "Google-Bewertungsaufsteller", 39m, "EUR", sold, false, true, true, true,
                setupDone == 0 ? "NotStarted" : setupDone == 4 ? "Completed" : "InProgress", setupDone, 4,
                method, payment, paid ? 39m : 0m, sold && !paid ? 39m : 0m,
                sold ? (setupDone == 4 ? "SoldAndSetUp" : "SoldSetupOpen") : "NotSold", followUp, followUpCommitment);
        }

        private static SalesSessionState State(ProductSaleState sale, bool over = false, string commitment = "", bool hasLead = false) =>
            new SalesSessionState("scripted", over ? "Tschüss!" : "Okay.", new SalesIndicators(60, 60, 60, 60), over ? "Ended" : "Closing", "Continue", over,
                over ? "Completed" : string.Empty, commitment, sale, hasLead);

        private IEnumerator StartStandRun(int seed = 7)
        {
            setup.KindButton(ScriptedScenarioSource.StandKind).onClick.Invoke();
            setup.SeedText = seed.ToString();
            setup.NewRunButton.onClick.Invoke();
            yield return null;
        }

        private void Send(string text)
        {
            input.text = text;
            send.onClick.Invoke();
        }

        private static string SaleStatus => GameObject.Find("SaleStatusLabel")?.GetComponent<TMP_Text>().text;

        [UnityTest]
        public IEnumerator Setup_OffersTheReviewStandVisit_AndStartsItsScenario()
        {
            Assert.That(setup.IsVisible, Is.True);
            Assert.That(setup.KindButton(ScriptedScenarioSource.StandKind).GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Bewertungsaufsteller (Vor-Ort-Besuch)"));

            yield return StartStandRun();

            Assert.That(session.LastStart.ScenarioId, Is.EqualTo("scripted-stand:7"));
            Assert.That(loop.CurrentRun.IsRerun, Is.False);
        }

        [UnityTest]
        public IEnumerator SameSeed_InTheOtherKind_IsNoTrainingRun()
        {
            loop.StartNewRun("Easy", 7);
            yield return null;
            end.onClick.Invoke();
            yield return null;
            result.NextRunButton.onClick.Invoke();
            yield return null;

            yield return StartStandRun(7);

            Assert.That(loop.CurrentRun.ScenarioId, Is.EqualTo("scripted-stand:7"));
            Assert.That(loop.CurrentRun.IsRerun, Is.False);
        }

        [UnityTest]
        public IEnumerator LiveStatus_ShowsPurchaseSetupAndPayment_DuringTheVisit()
        {
            yield return StartStandRun();

            Send("Wollen Sie einen?");
            session.Answer(State(Sale(setupDone: 2, method: "", payment: "NotStarted")));
            yield return null;

            Assert.That(GameObject.Find("SaleStatusLabel"), Is.Not.Null, "visible during a stand run");
            Assert.That(SaleStatus, Does.Contain("verkauft"));
            Assert.That(SaleStatus, Does.Contain("Einrichtung 2/4"));

            Send("Wie möchten Sie zahlen?");
            session.Answer(State(Sale(method: "Card", payment: "Open")));
            yield return null;
            Assert.That(SaleStatus, Does.Contain("Kartenzahlung (Stripe-Checkout) – offen"));
        }

        [UnityTest]
        public IEnumerator SoldSetUpAndPaidCash_ResultShowsEverything_AndBooks39Euros_WithoutALead()
        {
            yield return StartStandRun();

            Send("Vielen Dank, schönen Tag noch!");
            session.Answer(State(Sale(), over: true));
            yield return null;

            Assert.That(result.IsVisible, Is.True);
            var summary = result.ProductSummary;
            Assert.That(summary, Does.Contain("Google-Bewertungsaufsteller (39,00 €)"));
            Assert.That(summary, Does.Contain("Aufsteller verkauft: Ja"));
            Assert.That(summary, Does.Contain("Vor-Ort-Einrichtung abgeschlossen: Ja (4/4 Schritte)"));
            Assert.That(summary, Does.Contain("Zahlungsmethode: Barzahlung"));
            Assert.That(summary, Does.Contain("Zahlungsstatus: bestätigt – 39 € gutgeschrieben"));
            Assert.That(summary, Does.Contain("Gespräch ohne weiteres Angebot beendet"));
            Assert.That(loop.Wallet.Balance, Is.EqualTo(39));
            Assert.That(GameObject.Find("BalanceLabel").GetComponent<TMP_Text>().text, Is.EqualTo("Guthaben: 39 €"));
            Assert.That(loop.CurrentLead.Evaluation.Value, Is.EqualTo(0), "the stand is no lead");
            Assert.That(result.CanSell, Is.False);
            Assert.That(result.OutcomeText, Does.Not.Contain("Vereinbarung"));
        }

        [UnityTest]
        public IEnumerator OpenTransfer_IsShownAsOutstanding_AndNotBooked()
        {
            yield return StartStandRun();

            Send("Die Rechnung kommt per Mail. Tschüss!");
            session.Answer(State(Sale(method: "BankTransfer", payment: "Open"), over: true));
            yield return null;

            Assert.That(result.ProductSummary, Does.Contain("Überweisung (Rechnung)"));
            Assert.That(result.ProductSummary, Does.Contain("offen – 39 € ausstehend, nicht gutgeschrieben"));
            Assert.That(loop.Wallet.Balance, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SoldButNotSetUp_IsShownAsSuch()
        {
            yield return StartStandRun();

            end.onClick.Invoke();
            yield return null;
            Assert.That(result.ProductSummary, Does.Contain("Aufsteller verkauft: Nein"));

            result.NextRunButton.onClick.Invoke();
            yield return StartStandRun(8);
            Send("Tschüss!");
            session.Answer(State(Sale(setupDone: 1, payment: "Failed"), over: true));
            yield return null;
            Assert.That(result.ProductSummary, Does.Contain("Vor-Ort-Einrichtung abgeschlossen: Nein (1/4 Schritte)"));
            Assert.That(result.ProductSummary, Does.Contain("fehlgeschlagen"));
            Assert.That(loop.Wallet.Balance, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator StandThenWebsiteAppointment_BooksTheStand_AndTheWebsiteIsASeparateLead()
        {
            yield return StartStandRun();

            Send("Donnerstag um 10 für die Website?");
            session.Answer(State(Sale(followUp: "Website", followUpCommitment: "Appointment"), over: true, commitment: "Appointment", hasLead: true));
            yield return null;

            Assert.That(result.ProductSummary, Does.Contain("Website-Gespräch begonnen · Vereinbarung: Termin"));
            Assert.That(result.OutcomeText, Does.Contain("Website-Vereinbarung: Termin"));
            Assert.That(loop.Wallet.Balance, Is.EqualTo(39), "the stand is booked when the run ends");
            Assert.That(result.CanSell, Is.True, "the website appointment is a lead");

            result.SellButton.onClick.Invoke();
            Assert.That(loop.Wallet.Balance, Is.EqualTo(39 + 700), "stand once + website lead payout once");
        }

        [UnityTest]
        public IEnumerator TrainingRun_ShowsTheRevenue_ButBooksNothing()
        {
            yield return StartStandRun();
            end.onClick.Invoke();
            yield return null;
            result.RerunButton.onClick.Invoke();
            yield return null;
            Assert.That(loop.CurrentRun.IsRerun, Is.True);

            Send("Tschüss!");
            session.Answer(State(Sale(), over: true));
            yield return null;

            Assert.That(result.ProductSummary, Does.Contain("Trainingslauf: nicht gutgeschrieben"));
            Assert.That(loop.Wallet.Balance, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator WebsiteRuns_ShowNoProductBlock_AndNoSaleStatus()
        {
            loop.StartNewRun("Easy", 42);
            yield return null;
            Send("Hallo");
            session.Answer("Ja?");
            yield return null;
            Assert.That(GameObject.Find("SaleStatusLabel"), Is.Null, "hidden without a product");

            Send("Termin?");
            session.Answer("Ja, Dienstag.", new SalesIndicators(70, 72, 75, 60), over: true, endReason: "Completed", commitment: "Appointment");
            yield return null;

            Assert.That(result.ProductSummary, Is.Empty);
            Assert.That(result.OutcomeText, Does.Contain("Vereinbarung: Termin"));
            Assert.That(loop.CurrentLead.Evaluation.Value, Is.EqualTo(1000));
        }
    }
}
