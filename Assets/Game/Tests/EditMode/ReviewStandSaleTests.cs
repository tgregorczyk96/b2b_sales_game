using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SalesEngine;
using SalesEngine.Closing;
using SalesEngine.Content;
using SalesEngine.Conversations;
using SalesEngine.Customers;
using SalesEngine.Difficulty;
using SalesEngine.Discovery;
using SalesEngine.Interpretation;
using SalesEngine.Sales;
using SalesEngine.Scenarios;
using SalesSim.Application;
using SalesSim.EngineAdapter;
using SalesSim.Game;

namespace SalesSim.Tests.EditMode
{
    /// <summary>
    /// The review stand through the real engine and the real adapter, with a scripted customer and interpreter (no API): the
    /// engine decides purchase, setup and payment; Unity receives them as <see cref="ProductSaleState"/> and books only what the
    /// engine reports as received.
    /// </summary>
    public sealed class ReviewStandSaleTests
    {
        private static string ContentDirectory => Path.Combine(UnityEngine.Application.streamingAssetsPath, "SalesEngine", "content");

        // --- Scripted engine parts --------------------------------------------------------------------------------------------

        private sealed class Customer : ICustomerSimulator
        {
            private readonly Queue<string> replies = new Queue<string>();

            public List<CustomerSimulationContext> Contexts { get; } = new List<CustomerSimulationContext>();

            public void Says(string reply) => replies.Enqueue(reply);

            public Task<CustomerSimulationResponse> SimulateAsync(CustomerSimulationContext context, CancellationToken cancellationToken)
            {
                Contexts.Add(context);
                return Task.FromResult(new CustomerSimulationResponse(
                    replies.Dequeue(),
                    new SalesEngine.Customers.CustomerReaction(Engagement.Engaged, TrustSignal.Neutral, InterestSignal.Neutral, PatienceSignal.Stable, ConversationIntent.Continue)));
            }
        }

        private sealed class Interpreter : IConversationInterpreter
        {
            private readonly Queue<ConversationInterpretation> interpretations = new Queue<ConversationInterpretation>();

            public void Sees(ConversationInterpretation interpretation) => interpretations.Enqueue(interpretation);

            public Task<ConversationInterpretation> InterpretAsync(ConversationInterpretationContext context, CancellationToken cancellationToken) =>
                Task.FromResult(interpretations.Dequeue());
        }

        private sealed class Visit
        {
            private readonly Customer customer = new Customer();
            private readonly Interpreter interpreter = new Interpreter();
            private readonly List<string> turns = new List<string>();

            public Visit Turn(string trainee, string reply, ConversationInterpretation seen)
            {
                turns.Add(trainee);
                customer.Says(reply);
                interpreter.Sees(seen);
                return this;
            }

            public IReadOnlyList<string> Utterances => turns;

            public SalesEngineGameSession Session(IPaymentProcessor processor = null) => new SalesEngineGameSession(
                ContentDirectory,
                () => new SalesSimulationEngine(null, customer, interpreter, processor ?? SimulatedPaymentProcessor.AlwaysSucceeds),
                DifficultyProfile.Easy);

            public async Task<(SalesEngineGameSession Session, SalesSessionState Final)> PlayAsync(string scenarioId, IPaymentProcessor processor = null, bool end = false)
            {
                var session = Session(processor);
                await session.StartSessionAsync(new SessionStartRequest(scenarioId));
                foreach (var turn in turns)
                {
                    await session.SendPlayerTurnAsync(new PlayerTurn(turn));
                }

                var final = end ? await session.EndSessionAsync() : session.CurrentState;
                return (session, final);
            }
        }

        private static ConversationInterpretation Sees(SaleObservations sale = null, NextStepProposal[] steps = null) =>
            new ConversationInterpretation(new InterpretedCue[0], null, null, null, steps, null, sale);

        private static NextStepProposal Step(int turn, CommitmentKind kind, string proposal, string answer) =>
            new NextStepProposal(Speaker.Trainee, kind, StepSpecificity.Concrete, turn, proposal, StepResponse.Accepted, answer);

        private static PaymentObservation Pay(PaymentEventKind kind, PaymentMethod method, Speaker by, string quote) =>
            new PaymentObservation(kind, method, by, quote);

        /// <summary>A seed of the on-site review stand pool.</summary>
        private static string StandScenario => EngineScenarioSource.ReviewStandPool + ":7";

        private static Visit SoldAndSetUp() => new Visit()
            .Turn("Ihre Kundinnen halten das Handy dran, per NFC öffnet sich Ihre Google-Bewertungsseite. Ohne NFC scannen sie den QR-Code mit der Kamera und landen auf derselben Seite.",
                "Ach so, und das kostet?",
                Sees(new SaleObservations(explanations: new[]
                {
                    new FeatureExplanation(ProductFeature.Nfc, "per NFC öffnet sich Ihre Google-Bewertungsseite", true),
                    new FeatureExplanation(ProductFeature.QrCode, "Ohne NFC scannen sie den QR-Code mit der Kamera und landen auf derselben Seite", true),
                })))
            .Turn("Einmalig 39 Euro, keine laufenden Kosten. Wollen Sie einen?",
                "Okay, den nehme ich.",
                Sees(new SaleObservations(prices: new[] { new PriceStatement(39m, false, "Einmalig 39 Euro") },
                        claims: new[] { new ProductClaim(ProductQuestion.RecurringCosts, false, "keine laufenden Kosten") }),
                    new[] { Step(2, CommitmentKind.Agreement, "Wollen Sie einen?", "den nehme ich") }))
            .Turn("Ist das Ihr Bewertungslink? Dann verknüpfe ich den Aufsteller und teste NFC und den QR-Code.",
                "Ja, das ist er. Klappt ja.",
                Sees(new SaleObservations(setup: new[]
                {
                    new SetupAction(SetupStep.ReviewLinkConfirmed, "Ist das Ihr Bewertungslink", HypothesisResponse.Confirmed, "Ja, das ist er"),
                    new SetupAction(SetupStep.StandLinked, "Dann verknüpfe ich den Aufsteller", HypothesisResponse.Unanswered, null),
                    new SetupAction(SetupStep.NfcTested, "teste NFC", HypothesisResponse.Unanswered, null),
                    new SetupAction(SetupStep.QrTested, "den QR-Code", HypothesisResponse.Unanswered, null),
                })));

        // --- Adapter: the engine's verdicts reach Unity unchanged ----------------------------------------------------------------

        [Test]
        public async Task SoldSetUpAndPaidCash_IsReported_AsAProductSale_WithoutALead()
        {
            var visit = SoldAndSetUp().Turn("Wie möchten Sie zahlen?", "Bar, hier sind 39 Euro.",
                Sees(new SaleObservations(payments: new[]
                {
                    Pay(PaymentEventKind.MethodAgreed, PaymentMethod.Cash, Speaker.Customer, "Bar"),
                    Pay(PaymentEventKind.PaymentSucceeded, PaymentMethod.Cash, Speaker.Customer, "hier sind 39 Euro"),
                })));

            var (_, final) = await visit.PlayAsync(StandScenario, end: true);

            var sale = final.ProductSale;
            Assert.That(sale, Is.Not.Null);
            Assert.That(sale.ProductName, Is.EqualTo("Google-Bewertungsaufsteller"));
            Assert.That((sale.Price, sale.Currency), Is.EqualTo((39.00m, "EUR")));
            Assert.That(sale.IsSold && sale.NfcExplained && sale.QrCodeExplained && sale.OnSiteSetupPossible, Is.True);
            Assert.That(sale.SetupCompleted, Is.True);
            Assert.That((sale.SetupStepsDone, sale.SetupStepsRequired), Is.EqualTo((4, 4)));
            Assert.That((sale.PaymentMethod, sale.PaymentStatus), Is.EqualTo(("Cash", "Confirmed")));
            Assert.That((sale.BookedRevenue, sale.OutstandingRevenue), Is.EqualTo((39.00m, 0m)));
            Assert.That(sale.Result, Is.EqualTo("SoldAndSetUp"));
            Assert.That(sale.FollowUpStarted, Is.False);
            Assert.That(sale.QuestionableClaims, Is.EqualTo(0));
            Assert.That(final.HasLeadOffer, Is.False, "the stand is product revenue, not a lead");
            Assert.That(final.Commitment, Is.Empty);
            Assert.That(final.IsConversationOver, Is.True);
        }

        [Test]
        public async Task BankTransfer_StaysOpen_AndIsNotBooked()
        {
            var visit = SoldAndSetUp()
                .Turn("Wie möchten Sie zahlen?", "Per Überweisung bitte.",
                    Sees(new SaleObservations(payments: new[] { Pay(PaymentEventKind.MethodAgreed, PaymentMethod.BankTransfer, Speaker.Customer, "Per Überweisung") })))
                .Turn("Die Rechnung schicke ich Ihnen per Mail.", "Gut.",
                    Sees(new SaleObservations(payments: new[] { Pay(PaymentEventKind.PaymentRequested, PaymentMethod.BankTransfer, Speaker.Trainee, "Die Rechnung schicke ich Ihnen per Mail") })));

            var (_, final) = await visit.PlayAsync(StandScenario, end: true);

            Assert.That((final.ProductSale.PaymentMethod, final.ProductSale.PaymentStatus), Is.EqualTo(("BankTransfer", "Open")));
            Assert.That((final.ProductSale.BookedRevenue, final.ProductSale.OutstandingRevenue), Is.EqualTo((0m, 39.00m)));
            Assert.That(final.ProductSale.SetupCompleted, Is.True, "set up although the transfer is open");
        }

        [TestCase("Card", true)]
        [TestCase("PayPal", true)]
        [TestCase("Card", false)]
        public async Task ElectronicPayment_LinkOpen_ThenConfirmedOrFailed(string methodName, bool succeeds)
        {
            var method = (PaymentMethod)Enum.Parse(typeof(PaymentMethod), methodName);
            var visit = SoldAndSetUp()
                .Turn("Ich schicke Ihnen den Zahlungslink.", "Okay, mache ich auf.",
                    Sees(new SaleObservations(payments: new[] { Pay(PaymentEventKind.PaymentRequested, method, Speaker.Trainee, "Ich schicke Ihnen den Zahlungslink") })))
                .Turn("Hat es geklappt?", succeeds ? "Ja, ist bezahlt." : "Nein, abgelehnt.",
                    Sees(new SaleObservations(payments: new[]
                    {
                        succeeds
                            ? Pay(PaymentEventKind.PaymentSucceeded, method, Speaker.Customer, "ist bezahlt")
                            : Pay(PaymentEventKind.PaymentFailed, method, Speaker.Customer, "abgelehnt"),
                    })));
            var session = visit.Session(succeeds ? SimulatedPaymentProcessor.AlwaysSucceeds : SimulatedPaymentProcessor.AlwaysFails);
            await session.StartSessionAsync(new SessionStartRequest(StandScenario));
            for (var i = 0; i < 3; i++)
            {
                await session.SendPlayerTurnAsync(new PlayerTurn(visit.Utterances[i]));
            }

            var afterLink = await session.SendPlayerTurnAsync(new PlayerTurn(visit.Utterances[3]));
            Assert.That(afterLink.ProductSale.PaymentStatus, Is.EqualTo("Open"), "a sent link is not a payment");
            Assert.That(afterLink.ProductSale.BookedRevenue, Is.EqualTo(0m));

            var after = await session.SendPlayerTurnAsync(new PlayerTurn(visit.Utterances[4]));
            Assert.That(after.ProductSale.PaymentMethod, Is.EqualTo(methodName));
            Assert.That(after.ProductSale.PaymentStatus, Is.EqualTo(succeeds ? "Confirmed" : "Failed"));
            Assert.That(after.ProductSale.BookedRevenue, Is.EqualTo(succeeds ? 39.00m : 0m));
        }

        [Test]
        public async Task AfterTheStand_TheWebsite_IsTheLead_WithItsOwnCommitment()
        {
            var visit = SoldAndSetUp()
                .Turn("Wie zufrieden sind Sie eigentlich mit Ihrer Website?", "Ehrlich gesagt gar nicht.",
                    Sees(new SaleObservations(offerSwitches: new[] { new OfferSwitch("Website", "Wie zufrieden sind Sie eigentlich mit Ihrer Website") })))
                .Turn("Sollen wir uns das Donnerstag um 10 Uhr in Ruhe anschauen?", "Ja, Donnerstag passt.",
                    Sees(steps: new[] { Step(5, CommitmentKind.Appointment, "Sollen wir uns das Donnerstag um 10 Uhr in Ruhe anschauen?", "Donnerstag passt") }));

            var (_, final) = await visit.PlayAsync(StandScenario, end: true);

            Assert.That(final.HasLeadOffer, Is.True);
            Assert.That(final.Commitment, Is.EqualTo("Appointment"), "the website's next step, not the stand purchase");
            Assert.That(final.ProductSale.FollowUpOffer, Is.EqualTo("Website"));
            Assert.That(final.ProductSale.FollowUpCommitment, Is.EqualTo("Appointment"));
            Assert.That(final.ProductSale.IsSold, Is.True);
        }

        [Test]
        public async Task StandDeclined_WebsiteStillPossible()
        {
            var visit = new Visit()
                .Turn("Wollen Sie einen Aufsteller für 39 Euro?", "Nein danke, brauchen wir nicht.",
                    Sees(steps: new[] { new NextStepProposal(Speaker.Trainee, CommitmentKind.Agreement, StepSpecificity.Concrete, 1,
                        "Wollen Sie einen Aufsteller für 39 Euro?", StepResponse.Rejected, "Nein danke") }))
                .Turn("Verstehe. Wie zufrieden sind Sie mit Ihrer Website?", "Geht so.",
                    Sees(new SaleObservations(offerSwitches: new[] { new OfferSwitch("Website", "Wie zufrieden sind Sie mit Ihrer Website") })));

            var (_, final) = await visit.PlayAsync(StandScenario, end: true);

            Assert.That(final.ProductSale.IsSold, Is.False);
            Assert.That(final.ProductSale.PurchaseDeclined, Is.True);
            Assert.That(final.ProductSale.Result, Is.EqualTo("Declined"));
            Assert.That(final.HasLeadOffer, Is.True);
            Assert.That((final.ProductSale.BookedRevenue, final.ProductSale.OutstandingRevenue), Is.EqualTo((0m, 0m)));
        }

        [Test]
        public async Task WebsiteScenarios_HaveNoProductSale_AndKeepTheirCommitment()
        {
            var generator = new ContentRepository(ContentDirectory).LoadGenerator(EngineScenarioSource.DefaultPool);
            var seed = Enumerable.Range(0, 500).First(s => generator.Generate(s).Scenario.Offer.Id == "website");
            var visit = new Visit().Turn("Darf ich Ihnen Donnerstag einen Termin vorschlagen?", "Ja, gerne.",
                Sees(steps: new[] { Step(1, CommitmentKind.Appointment, "Darf ich Ihnen Donnerstag einen Termin vorschlagen?", "Ja, gerne") }));

            var (_, final) = await visit.PlayAsync($"{EngineScenarioSource.DefaultPool}:{seed}");

            Assert.That(final.ProductSale, Is.Null);
            Assert.That(final.HasLeadOffer, Is.True);
            Assert.That(final.Commitment, Is.EqualTo("Appointment"));
        }

        // --- Scenario kinds -----------------------------------------------------------------------------------------------------

        [Test]
        public void ScenarioSource_OffersTheWebsiteAndTheReviewStandVisit()
        {
            var source = new EngineScenarioSource(ContentDirectory, random: new Random(3));

            Assert.That(source.Kinds.Select(k => k.Id), Is.EqualTo(new[] { EngineScenarioSource.DefaultPool, EngineScenarioSource.ReviewStandPool }));
            Assert.That(source.SelectedKind, Is.EqualTo(EngineScenarioSource.DefaultPool));

            source.Select(EngineScenarioSource.ReviewStandPool);
            var seed = source.PickRandomSeed();
            var scenarioId = source.ScenarioIdFor(seed);

            Assert.That(scenarioId, Does.StartWith(EngineScenarioSource.ReviewStandPool + ":"));
            var scenario = new ContentRepository(ContentDirectory).LoadGenerator(EngineScenarioSource.ReviewStandPool).Generate(seed).Scenario;
            Assert.That(scenario.Offer.Id, Is.EqualTo("review-stand"));
            Assert.That(scenario.Situation.Channel, Is.EqualTo(ContactChannel.WalkIn));
            Assert.That(source.OriginOf(scenarioId), Does.Contain("/review-stand"));
            Assert.Throws<ArgumentException>(() => source.Select("unknown"));
        }

        [Test]
        public async Task ReviewStandScenario_StartsAsAVisit_WithTheProductInTheState()
        {
            var session = new Visit().Session();

            var start = await session.StartSessionAsync(new SessionStartRequest(StandScenario));

            Assert.That(start.ProductSale, Is.Not.Null);
            Assert.That(start.ProductSale.Result, Is.EqualTo("NotSold"));
            Assert.That(start.ProductSale.PaymentStatus, Is.EqualTo("NotStarted"));
            Assert.That(start.HasLeadOffer, Is.False);
        }

        // --- Economy ------------------------------------------------------------------------------------------------------------

        private static ProductSaleState Sale(string payment = "Confirmed", decimal booked = 39m, decimal outstanding = 0m, string followUp = "", string followUpCommitment = "") =>
            new ProductSaleState("Google-Bewertungsaufsteller", 39m, "EUR", true, false, true, true, true, "Completed", 4, 4,
                "Cash", payment, booked, outstanding, "SoldAndSetUp", followUp, followUpCommitment);

        private static SalesSessionState Final(ProductSaleState sale, bool hasLead, string commitment = "", SalesIndicators indicators = null) =>
            new SalesSessionState("s", string.Empty, indicators ?? new SalesIndicators(80, 80, 80, 80), "Ended", "Continue", true, "Completed", commitment, sale, hasLead);

        [Test]
        public void ConfirmedStand_IsCreditedOnce_AndIsNoLead()
        {
            var final = Final(Sale(), hasLead: false);
            var wallet = new PlayerWallet();
            var settlement = ProductSettlement.Of(final, isTraining: false);

            Assert.That(settlement.CreditTo(wallet), Is.EqualTo(39));
            Assert.That(settlement.CreditTo(wallet), Is.EqualTo(0), "booked once");
            Assert.That(wallet.Balance, Is.EqualTo(39));
            var lead = LeadEvaluator.Evaluate(final, EconomySettings.Default);
            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Lost));
            Assert.That(lead.Value, Is.EqualTo(0), "high indicators alone never turn the stand into a lead");
        }

        [Test]
        public void OpenOrFailedPayment_IsOutstanding_NotCredited()
        {
            foreach (var status in new[] { "Open", "Failed", "NotStarted" })
            {
                var wallet = new PlayerWallet();
                var settlement = ProductSettlement.Of(Final(Sale(status, 0m, 39m), false), false);

                Assert.That(settlement.CreditTo(wallet), Is.EqualTo(0), status);
                Assert.That(settlement.Outstanding, Is.EqualTo(39), status);
                Assert.That(wallet.Balance, Is.EqualTo(0), status);
            }
        }

        [Test]
        public void TrainingRun_BooksNothing()
        {
            var wallet = new PlayerWallet();
            var settlement = ProductSettlement.Of(Final(Sale(), false), isTraining: true);

            Assert.That(settlement.Booked, Is.EqualTo(39));
            Assert.That(settlement.CreditTo(wallet), Is.EqualTo(0));
            Assert.That(wallet.Balance, Is.EqualTo(0));
        }

        [Test]
        public void StandAndWebsite_AreBookedSeparately_WithoutDoubleRevenue()
        {
            var final = Final(Sale(followUp: "Website", followUpCommitment: "Agreement"), hasLead: true, commitment: "Agreement");
            var wallet = new PlayerWallet();

            ProductSettlement.Of(final, false).CreditTo(wallet);
            var lead = new LeadSale(LeadEvaluator.Evaluate(final, EconomySettings.Default));
            lead.SellTo(wallet);

            Assert.That(lead.Evaluation.Quality, Is.EqualTo(LeadQuality.Qualified));
            Assert.That(wallet.Balance, Is.EqualTo(39 + 700), "stand revenue once + website lead payout once");
        }

        [Test]
        public void WithoutAProduct_NothingIsSettled_AndTheLeadIsUnchanged()
        {
            var final = new SalesSessionState("s", string.Empty, new SalesIndicators(80, 80, 80, 80), "Ended", "Continue", true, "Completed", "Appointment");
            var settlement = ProductSettlement.Of(final, false);

            Assert.That(settlement.HasProduct, Is.False);
            Assert.That(settlement.CreditTo(new PlayerWallet()), Is.EqualTo(0));
            Assert.That(final.HasLeadOffer, Is.True);
            Assert.That(LeadEvaluator.Evaluate(final, EconomySettings.Default).Quality, Is.EqualTo(LeadQuality.Qualified));
        }

        // --- Runs per scenario kind ---------------------------------------------------------------------------------------------

        [Test]
        public void SameSeed_InAnotherKind_IsANewScenario_NotATrainingRun()
        {
            var runs = new RunRegistry();
            runs.Begin(42, "Easy", "hair-salon:42");

            Assert.That(runs.Begin(42, "Easy", "review-stand-visit:42").IsRerun, Is.False);
            Assert.That(runs.Begin(42, "Hard", "review-stand-visit:42").IsRerun, Is.True);
            Assert.That(runs.WasPlayed("review-stand-visit:42"), Is.True);
        }

        [Test]
        public void ScoresAreKeptPerScenario_AndSurviveASave()
        {
            var runs = new RunRegistry();
            var website = runs.Begin(42, "Easy", "hair-salon:42");
            runs.RecordScore(website, 58f);
            var stand = runs.Begin(42, "Easy", "review-stand-visit:42");

            Assert.That(runs.LastScore(stand), Is.Null);
            runs.RecordScore(stand, 71f);
            var data = new SaveData();
            runs.WriteTo(data);
            var restored = RunRegistry.Restore(data);

            Assert.That(restored.LastScore(new RunInfo(9, 42, "Easy", "hair-salon:42", true)), Is.EqualTo(58f));
            Assert.That(restored.LastScore(new RunInfo(9, 42, "Easy", "review-stand-visit:42", true)), Is.EqualTo(71f));
            Assert.That(restored.Begin(42, "Easy", "review-stand-visit:42").IsRerun, Is.True);
        }

        [Test]
        public void ASaveFromBeforeKinds_KeepsItsSeedsPlayed_InEveryKind()
        {
            var old = new SaveData { playedSeeds = new List<int> { 42 }, lastScores = new List<SaveData.SeedScore> { new SaveData.SeedScore { seed = 42, score = 50f } } };
            old.playedScenarios = null;
            old.scenarioScores = null;

            var restored = RunRegistry.Restore(old);

            Assert.That(restored.Begin(42, "Easy", "review-stand-visit:42").IsRerun, Is.True, "conservative: no payout for a possibly played seed");
            Assert.That(restored.Begin(43, "Easy", "review-stand-visit:43").IsRerun, Is.False);
        }
    }
}
