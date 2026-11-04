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
    /// One conversation with both offers in SalesTestScene against a scripted session: the setup offers contact types instead of
    /// products, the live status shows every offer and the one under discussion, the player moves between them without a restart,
    /// and the result screen shows one block per offer with the deals closed — the stand booked once, the website as a lead.
    /// No engine, no API — the engine side is covered by the EditMode MultiProductSaleTests.
    /// </summary>
    public sealed class MultiProductFlowTests
    {
        private ScriptedSession session;
        private RunResultView result;
        private RunSetupView setup;
        private SalesRunLoop loop;
        private TMP_InputField input;
        private Button send;

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

            session = new ScriptedSession();
            loop.Configure(session, new ScriptedScenarioSource(ScriptedScenarioSource.ContactTypes), null);
            yield return null;
        }

        private static ProductSaleState Stand(bool sold, bool onSite = true, int setupDone = 0, string method = "", string payment = "NotStarted")
        {
            var paid = sold && payment == "Confirmed";
            return new ProductSaleState(
                "Google-Bewertungsaufsteller", 39m, "EUR", sold, false, true, true, onSite,
                setupDone == 0 ? "NotStarted" : setupDone == 4 ? "Completed" : "InProgress", setupDone, 4,
                method, payment, paid ? 39m : 0m, sold && !paid ? 39m : 0m,
                sold ? (setupDone == 4 ? "SoldAndSetUp" : "SoldSetupOpen") : "NotSold", string.Empty, string.Empty);
        }

        private static SalesSessionState State(
            ProductSaleState stand, OfferProgressState[] offers, bool over = false, string commitment = "", bool hasLead = false) =>
            new SalesSessionState("scripted", over ? "Tschüss!" : "Okay.", new SalesIndicators(60, 60, 60, 60), over ? "Ended" : "Pitch", "Continue", over,
                over ? "Completed" : string.Empty, commitment, stand, hasLead, null, null, false, offers);

        private IEnumerator StartRun(string kind, int seed = 7)
        {
            setup.KindButton(kind).onClick.Invoke();
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
        public IEnumerator Setup_OffersContactTypes_NotProducts()
        {
            Assert.That(setup.IsVisible, Is.True);
            Assert.That(setup.KindButton(ScriptedScenarioSource.MultiVisitKind).GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Vor-Ort-Besuch (Aufsteller & Website)"));
            Assert.That(setup.KindButton(ScriptedScenarioSource.MultiCallKind).GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Telefonat (Aufsteller & Website)"));

            yield return StartRun(ScriptedScenarioSource.MultiCallKind);

            Assert.That(session.LastStart.ScenarioId, Is.EqualTo(ScriptedScenarioSource.MultiCallKind + ":7"));
        }

        [UnityTest]
        public IEnumerator LiveStatus_ShowsBothOffers_AndFollowsTheSwitch_WithoutARestart()
        {
            yield return StartRun(ScriptedScenarioSource.MultiVisitKind);

            Assert.That(SaleStatus, Does.Contain("▶ Google-Bewertungsaufsteller: noch nicht verkauft"));
            Assert.That(SaleStatus, Does.Contain("• Website: noch nicht angesprochen"));

            Send("Den Aufsteller gibt es für 39 Euro – wollen Sie einen?");
            session.Answer(State(Stand(true, setupDone: 2), ScriptedSession.Offers(standActive: true)));
            yield return null;
            Assert.That(SaleStatus, Does.Contain("▶ Google-Bewertungsaufsteller: verkauft · Einrichtung 2/4"));

            Send("Wie zufrieden sind Sie eigentlich mit Ihrer Website?");
            session.Answer(State(Stand(true, setupDone: 2), ScriptedSession.Offers(standActive: false), hasLead: true));
            yield return null;
            Assert.That(SaleStatus, Does.Contain("• Google-Bewertungsaufsteller: verkauft"));
            Assert.That(SaleStatus, Does.Contain("▶ Website: im Gespräch"));

            Send("Darf ich Ihnen Donnerstag einen Entwurf zeigen?");
            session.Answer(State(Stand(true, setupDone: 2), ScriptedSession.Offers(standActive: false, websiteCommitment: "Appointment"), commitment: "Appointment", hasLead: true));
            yield return null;
            Assert.That(SaleStatus, Does.Contain("▶ Website: Termin vereinbart"));
            Assert.That(session.Starts, Is.EqualTo(1), "moving between the offers never restarts the conversation");
        }

        [UnityTest]
        public IEnumerator BothSold_TheResultShowsBothDeals_BooksTheStandOnce_AndTheWebsiteAsALead()
        {
            yield return StartRun(ScriptedScenarioSource.MultiVisitKind);

            Send("Vielen Dank, schönen Tag noch!");
            session.Answer(State(Stand(true, setupDone: 4, method: "Cash", payment: "Confirmed"),
                ScriptedSession.Offers(standActive: false, websiteCommitment: "Agreement"), over: true, commitment: "Agreement", hasLead: true));
            yield return null;

            Assert.That(result.IsVisible, Is.True);
            var summary = result.ProductSummary;
            Assert.That(summary, Does.Contain("2 Abschlüsse in diesem Gespräch: Google-Bewertungsaufsteller, Website"));
            Assert.That(summary, Does.Contain("Aufsteller verkauft: Ja"));
            Assert.That(summary, Does.Contain("Zahlungsstatus: bestätigt – 39 € gutgeschrieben"));
            Assert.That(summary, Does.Not.Contain("Weiterer Verlauf"), "every offer has its own block");
            Assert.That(summary, Does.Contain("Website\n• Angesprochen: Ja"));
            Assert.That(summary, Does.Contain("Ergebnis: beauftragt"));
            Assert.That(result.OutcomeText, Does.Contain("Website: beauftragt"));

            Assert.That(loop.Wallet.Balance, Is.EqualTo(39), "the stand's revenue is booked when the run ends");
            Assert.That(result.CanSell, Is.True);
            result.SellButton.onClick.Invoke();
            yield return null;
            Assert.That(loop.Wallet.Balance, Is.EqualTo(39 + loop.CurrentLead.Evaluation.Payout));
            result.SellButton.onClick.Invoke();
            yield return null;
            Assert.That(loop.Wallet.Balance, Is.EqualTo(39 + loop.CurrentLead.Evaluation.Payout), "nothing is booked twice");
        }

        [UnityTest]
        public IEnumerator OnlyTheStand_TheWebsiteNeverCameUp_NoLead()
        {
            yield return StartRun(ScriptedScenarioSource.MultiVisitKind);

            Send("Danke, tschüss!");
            session.Answer(State(Stand(true, setupDone: 4, method: "BankTransfer", payment: "Open"), ScriptedSession.Offers(standActive: true), over: true));
            yield return null;

            Assert.That(result.ProductSummary, Does.Contain("Abschluss in diesem Gespräch: Google-Bewertungsaufsteller"));
            Assert.That(result.ProductSummary, Does.Contain("Ergebnis: noch nicht angesprochen"));
            Assert.That(result.ProductSummary, Does.Contain("39 € ausstehend, nicht gutgeschrieben"));
            Assert.That(loop.Wallet.Balance, Is.EqualTo(0), "an open transfer is no revenue yet");
            Assert.That(result.CanSell, Is.False);
        }

        [UnityTest]
        public IEnumerator BothDeclined_NoDeal()
        {
            yield return StartRun(ScriptedScenarioSource.MultiVisitKind);

            Send("Schade, dann einen schönen Tag.");
            session.Answer(State(new ProductSaleState("Google-Bewertungsaufsteller", 39m, "EUR", false, true, false, false, true, "NotStarted", 0, 4,
                    string.Empty, "NotStarted", 0m, 0m, "Declined", string.Empty, string.Empty),
                ScriptedSession.Offers(standActive: false, standDeclined: true, websiteDeclined: true), over: true, hasLead: true));
            yield return null;

            Assert.That(result.ProductSummary, Does.Contain("Kein Abschluss in diesem Gespräch"));
            Assert.That(result.ProductSummary, Does.Contain("Ergebnis: abgelehnt"));
            Assert.That(loop.Wallet.Balance, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator PhoneCall_TheStandIsNotSetUpOnSite()
        {
            yield return StartRun(ScriptedScenarioSource.MultiCallKind);

            Send("Wollen Sie einen Aufsteller?");
            session.Answer(State(Stand(true, onSite: false), ScriptedSession.Offers(standActive: true)));
            yield return null;

            Assert.That(SaleStatus, Does.Contain("Einrichtung nicht vor Ort möglich"));
        }
    }
}
