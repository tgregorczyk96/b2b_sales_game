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
using SalesSim.Application;
using SalesSim.EngineAdapter;
using SalesSim.Game;
using SalesSim.Presentation;

namespace SalesSim.Tests.EditMode
{
    /// <summary>
    /// One conversation, both offers (review stand and website), through the real engine and the real adapter with a scripted
    /// customer and interpreter (no API): either offer can be the occasion, the player moves between them without a restart,
    /// several deals are possible, each offer keeps its own result, and the economy books each result once.
    /// </summary>
    public sealed class MultiProductSaleTests
    {
        private const string Stand = "Google-Bewertungsaufsteller";
        private const string Web = "Website";

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
                    new SalesEngine.Customers.CustomerReaction(Engagement.Engaged, TrustSignal.Positive, InterestSignal.Positive, PatienceSignal.Stable, ConversationIntent.Continue)));
            }
        }

        private sealed class Interpreter : IConversationInterpreter
        {
            private readonly Queue<ConversationInterpretation> interpretations = new Queue<ConversationInterpretation>();

            public List<ConversationInterpretationContext> Contexts { get; } = new List<ConversationInterpretationContext>();

            public void Sees(ConversationInterpretation interpretation) => interpretations.Enqueue(interpretation);

            public Task<ConversationInterpretation> InterpretAsync(ConversationInterpretationContext context, CancellationToken cancellationToken)
            {
                Contexts.Add(context);
                return Task.FromResult(interpretations.Dequeue());
            }
        }

        private sealed class Talk
        {
            public readonly Customer Customer = new Customer();
            public readonly Interpreter Interpreter = new Interpreter();
            private readonly List<string> turns = new List<string>();

            public int Turns => turns.Count;

            public Talk Turn(string trainee, string reply, ConversationInterpretation seen)
            {
                turns.Add(trainee);
                Customer.Says(reply);
                Interpreter.Sees(seen);
                return this;
            }

            /// <summary>Plays every turn; <paramref name="states"/> collects the state after each turn.</summary>
            public async Task<SalesSessionState> PlayAsync(string scenarioId, List<SalesSessionState> states = null, bool end = true)
            {
                var session = new SalesEngineGameSession(
                    ContentDirectory,
                    () => new SalesSimulationEngine(null, Customer, Interpreter, SimulatedPaymentProcessor.AlwaysSucceeds),
                    DifficultyProfile.Easy);
                var started = await session.StartSessionAsync(new SessionStartRequest(scenarioId));
                states?.Add(started);
                foreach (var turn in turns)
                {
                    var state = await session.SendPlayerTurnAsync(new PlayerTurn(turn));
                    states?.Add(state);
                }

                return end ? await session.EndSessionAsync() : session.CurrentState;
            }
        }

        private static ConversationInterpretation Sees(SaleObservations sale = null, NextStepProposal[] steps = null) =>
            new ConversationInterpretation(new InterpretedCue[0], null, null, null, steps, null, sale);

        private static NextStepProposal Step(int turn, CommitmentKind kind, string proposal, StepResponse response, string answer, string offer = null) =>
            new NextStepProposal(Speaker.Trainee, kind, StepSpecificity.Concrete, turn, proposal, response, answer, offer);

        /// <summary>A seed of <paramref name="pool"/> whose occasion is <paramref name="offerId"/>.</summary>
        private static string ScenarioWith(string pool, string offerId)
        {
            var generator = new ContentRepository(ContentDirectory).LoadGenerator(pool);
            var seed = Enumerable.Range(0, 500).First(s => generator.Generate(s).Scenario.Offer.Id == offerId);
            return $"{pool}:{seed}";
        }

        private static string StandVisit => ScenarioWith(EngineScenarioSource.VisitPool, "review-stand");

        private static string WebsiteVisit => ScenarioWith(EngineScenarioSource.VisitPool, "website");

        private static OfferProgressState Offer(SalesSessionState state, string id) => state.Offers.Single(o => o.OfferId == id);

        // --- Building blocks --------------------------------------------------------------------------------------------------

        private static Talk BuyStand(Talk talk) => talk.Turn(
            "Der Aufsteller kostet einmalig 39 Euro. Wollen Sie einen?", "Okay, den nehme ich.",
            Sees(new SaleObservations(prices: new[] { new PriceStatement(39m, false, "Der Aufsteller kostet einmalig 39 Euro") }),
                new[] { Step(talk.Turns + 1, CommitmentKind.Agreement, "Wollen Sie einen?", StepResponse.Accepted, "den nehme ich", Stand) }));

        private static Talk DeclineStand(Talk talk) => talk.Turn(
            "Wollen Sie einen Aufsteller für 39 Euro?", "Nein danke, Bewertungen sind uns nicht so wichtig.",
            Sees(steps: new[] { Step(talk.Turns + 1, CommitmentKind.Agreement, "Wollen Sie einen Aufsteller für 39 Euro?", StepResponse.Rejected, "Nein danke", Stand) }));

        private static Talk SetUpStand(Talk talk) => talk.Turn(
            "Ist das Ihr Google-Eintrag? Dann verknüpfe ich den Aufsteller jetzt damit. Ich teste kurz per NFC und den QR-Code mit der Kamera.",
            "Ja, das ist unser Eintrag.",
            Sees(new SaleObservations(setup: new[]
            {
                new SetupAction(SetupStep.ReviewLinkConfirmed, "Ist das Ihr Google-Eintrag?", HypothesisResponse.Confirmed, "Ja, das ist unser Eintrag"),
                new SetupAction(SetupStep.StandLinked, "Dann verknüpfe ich den Aufsteller jetzt damit", HypothesisResponse.Unanswered, null),
                new SetupAction(SetupStep.NfcTested, "Ich teste kurz per NFC", HypothesisResponse.Unanswered, null),
                new SetupAction(SetupStep.QrTested, "den QR-Code mit der Kamera", HypothesisResponse.Unanswered, null),
            })));

        private static Talk PayCash(Talk talk) => talk.Turn(
            "Wie möchten Sie bezahlen?", "Ich zahle bar, hier sind 39 Euro.",
            Sees(new SaleObservations(payments: new[]
            {
                new PaymentObservation(PaymentEventKind.MethodAgreed, PaymentMethod.Cash, Speaker.Customer, "Ich zahle bar"),
                new PaymentObservation(PaymentEventKind.PaymentSucceeded, PaymentMethod.Cash, Speaker.Customer, "hier sind 39 Euro"),
            })));

        private static Talk PayByTransfer(Talk talk) => talk.Turn(
            "Wie möchten Sie bezahlen? Ich schicke Ihnen sonst die Rechnung.", "Per Überweisung bitte.",
            Sees(new SaleObservations(payments: new[]
            {
                new PaymentObservation(PaymentEventKind.MethodAgreed, PaymentMethod.BankTransfer, Speaker.Customer, "Per Überweisung bitte"),
                new PaymentObservation(PaymentEventKind.PaymentRequested, PaymentMethod.BankTransfer, Speaker.Trainee, "Ich schicke Ihnen sonst die Rechnung"),
            })));

        private static Talk ToWebsite(Talk talk) => talk.Turn(
            "Wie zufrieden sind Sie eigentlich mit Ihrer Website?", "Ehrlich gesagt nicht so. Die ist ziemlich alt.",
            Sees(new SaleObservations(offerSwitches: new[] { new OfferSwitch(Web, "Wie zufrieden sind Sie eigentlich mit Ihrer Website") })));

        private static Talk ToStand(Talk talk) => talk.Turn(
            "Ich habe übrigens auch einen Aufsteller für Google-Bewertungen dabei, mit NFC und QR-Code.", "Ach, zeigen Sie mal.",
            Sees(new SaleObservations(offerSwitches: new[] { new OfferSwitch(Stand, "Ich habe übrigens auch einen Aufsteller für Google-Bewertungen dabei") })));

        private static Talk WebsiteAppointment(Talk talk) => talk.Turn(
            "Darf ich Ihnen am Donnerstag um 10 Uhr einen Entwurf zeigen?", "Ja, Donnerstag um 10 passt.",
            Sees(steps: new[] { Step(talk.Turns + 1, CommitmentKind.Appointment, "Darf ich Ihnen am Donnerstag um 10 Uhr einen Entwurf zeigen?", StepResponse.Accepted, "Donnerstag um 10 passt") }));

        private static Talk WebsiteOrdered(Talk talk) => talk.Turn(
            "Sollen wir Ihnen die neue Website machen?", "Ja, machen wir.",
            Sees(steps: new[] { Step(talk.Turns + 1, CommitmentKind.Agreement, "Sollen wir Ihnen die neue Website machen?", StepResponse.Accepted, "Ja, machen wir") }));

        private static Talk DeclineWebsite(Talk talk) => talk.Turn(
            "Sollen wir Ihnen eine neue Website machen?", "Nein, die Website reicht uns, wie sie ist.",
            Sees(steps: new[] { Step(talk.Turns + 1, CommitmentKind.Agreement, "Sollen wir Ihnen eine neue Website machen?", StepResponse.Rejected, "Nein, die Website reicht uns") }));

        /// <summary>Books a finished run like the run loop: product revenue when it ends, the lead when sold to the closer.</summary>
        private static (int Balance, LeadEvaluation Lead, ProductSettlement Product) Settle(SalesSessionState final, bool training = false)
        {
            var wallet = new PlayerWallet();
            var lead = new LeadSale(LeadEvaluator.Evaluate(final, EconomySettings.Default), training);
            var product = ProductSettlement.Of(final, training);
            product.CreditTo(wallet);
            product.CreditTo(wallet); // a second call books nothing
            if (lead.CanSell)
            {
                lead.SellTo(wallet);
            }

            return (wallet.Balance, lead.Evaluation, product);
        }

        // --- Stand -> website ------------------------------------------------------------------------------------------------

        [Test]
        public async Task StandThenWebsite_BothSold_BookedSeparately_AndNeverTwice()
        {
            var states = new List<SalesSessionState>();
            var final = await WebsiteOrdered(ToWebsite(PayCash(SetUpStand(BuyStand(new Talk()))))).PlayAsync(StandVisit, states);

            Assert.That(states[0].IsMultiOffer, Is.True);
            Assert.That(states[0].ActiveOffer.OfferId, Is.EqualTo("review-stand"));
            Assert.That(states.Skip(1).Take(states.Count - 2).Any(s => s.IsConversationOver), Is.False, "a sale never ends the conversation");

            Assert.That(final.ProductSale.IsSold, Is.True);
            Assert.That(final.ProductSale.SetupCompleted, Is.True);
            Assert.That(final.ProductSale.PaymentStatus, Is.EqualTo("Confirmed"));
            Assert.That(Offer(final, "website").Commitment, Is.EqualTo("Agreement"));
            Assert.That(final.ActiveOffer.OfferId, Is.EqualTo("website"));
            Assert.That(final.HasLeadOffer, Is.True);
            Assert.That(final.Commitment, Is.EqualTo("Agreement"));

            var (balance, lead, product) = Settle(final);
            Assert.That(product.Booked, Is.EqualTo(39));
            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Qualified));
            Assert.That(balance, Is.EqualTo(39 + lead.Payout), "stand revenue and website payout, each once");
            Assert.That(ProductSaleText.ClosedDealsLine(final), Is.EqualTo($"2 Abschlüsse in diesem Gespräch: {Stand}, {Web}"));
        }

        // --- Website -> stand ------------------------------------------------------------------------------------------------

        [Test]
        public async Task WebsiteThenStand_TheStandIsSoldInAWebsiteVisit_PaymentOpen()
        {
            var final = await PayByTransfer(SetUpStand(BuyStand(ToStand(WebsiteAppointment(new Talk()))))).PlayAsync(WebsiteVisit);

            Assert.That(Offer(final, "website").IsOccasion, Is.True);
            Assert.That(Offer(final, "website").Commitment, Is.EqualTo("Appointment"));
            Assert.That(final.ProductSale.IsSold, Is.True);
            Assert.That(final.ProductSale.SetupCompleted, Is.True);
            Assert.That(final.ProductSale.PaymentStatus, Is.EqualTo("Open"));

            var (balance, lead, product) = Settle(final);
            Assert.That((product.Booked, product.Outstanding), Is.EqualTo((0, 39)), "an open transfer is no revenue yet");
            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Qualified));
            Assert.That(balance, Is.EqualTo(lead.Payout));
        }

        // --- Refusals ---------------------------------------------------------------------------------------------------------

        [Test]
        public async Task StandDeclined_WebsiteInterested()
        {
            var final = await WebsiteAppointment(ToWebsite(DeclineStand(new Talk()))).PlayAsync(StandVisit);

            Assert.That(Offer(final, "review-stand").Declined, Is.True);
            Assert.That(final.ProductSale.Result, Is.EqualTo("Declined"));
            Assert.That(Offer(final, "website").Commitment, Is.EqualTo("Appointment"));
            var (_, lead, product) = Settle(final);
            Assert.That(product.Booked, Is.EqualTo(0));
            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Qualified));
        }

        [Test]
        public async Task WebsiteDeclined_StandBoughtAndPaidCash()
        {
            var final = await PayCash(BuyStand(ToStand(DeclineWebsite(new Talk())))).PlayAsync(WebsiteVisit);

            Assert.That(Offer(final, "website").Declined, Is.True);
            Assert.That(final.ProductSale.IsSold, Is.True);
            var (_, lead, product) = Settle(final);
            Assert.That(product.Booked, Is.EqualTo(39));
            Assert.That(lead.Commitment, Is.Empty);
            Assert.That(ProductSaleText.ClosedDealsLine(final), Is.EqualTo($"Abschluss in diesem Gespräch: {Stand}"));
        }

        [Test]
        public async Task BothDeclined_NothingIsBooked()
        {
            var final = await DeclineWebsite(ToWebsite(DeclineStand(new Talk()))).PlayAsync(StandVisit);

            Assert.That(final.Offers.All(o => o.Declined), Is.True);
            var (balance, lead, product) = Settle(final);
            Assert.That(product.Booked + product.Outstanding, Is.EqualTo(0));
            Assert.That(lead.Commitment, Is.Empty);
            Assert.That(ProductSaleText.ClosedDealsLine(final), Is.EqualTo("Kein Abschluss in diesem Gespräch"));
            Assert.That(balance, Is.EqualTo(lead.Payout));
        }

        [Test]
        public async Task OnlyTheStand_TheWebsiteNeverCameUp_NoLead()
        {
            var final = await PayCash(SetUpStand(BuyStand(new Talk()))).PlayAsync(StandVisit);

            Assert.That(Offer(final, "website").Discussed, Is.False);
            Assert.That(final.HasLeadOffer, Is.False);
            var (balance, lead, product) = Settle(final);
            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Lost), "no website conversation, no website lead");
            Assert.That(balance, Is.EqualTo(39));
            Assert.That(ProductSaleText.LeadStatusText(Offer(final, "website")), Is.EqualTo("noch nicht angesprochen"));
        }

        [Test]
        public async Task OnlyTheWebsite_TheStandStaysUntouched_TheLeadIsPricedAsBefore()
        {
            var final = await WebsiteAppointment(new Talk()).PlayAsync(WebsiteVisit);

            Assert.That(Offer(final, "review-stand").Discussed, Is.False);
            Assert.That(final.ProductSale.Result, Is.EqualTo("NotSold"));
            Assert.That(final.HasLeadOffer, Is.True);
            var (_, lead, product) = Settle(final);
            Assert.That(product.Booked + product.Outstanding, Is.EqualTo(0));
            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Qualified));
        }

        // --- Phone ------------------------------------------------------------------------------------------------------------

        [Test]
        public async Task PhoneCall_TheStandCanBeSold_ButIsNotSetUp()
        {
            var call = ScenarioWith(EngineScenarioSource.CallPool, "website");
            var final = await PayByTransfer(SetUpStand(BuyStand(ToStand(new Talk())))).PlayAsync(call);

            Assert.That(final.ProductSale.IsSold, Is.True);
            Assert.That(final.ProductSale.OnSiteSetupPossible, Is.False);
            Assert.That(final.ProductSale.SetupStepsDone, Is.EqualTo(0));
            Assert.That(final.ProductSale.Result, Is.EqualTo("SoldSetupOpen"));
            Assert.That(ProductSaleText.Status(final.ProductSale), Does.Contain("Einrichtung nicht vor Ort möglich"));
        }

        // --- Training ---------------------------------------------------------------------------------------------------------

        [Test]
        public async Task TrainingRun_BooksNeitherTheStandNorTheWebsite()
        {
            var final = await WebsiteOrdered(ToWebsite(PayCash(BuyStand(new Talk())))).PlayAsync(StandVisit);

            var (balance, lead, product) = Settle(final, training: true);

            Assert.That(balance, Is.EqualTo(0));
            Assert.That(product.Booked, Is.EqualTo(39), "evaluated as usual");
            Assert.That(product.Creditable, Is.EqualTo(0));
            Assert.That(lead.Quality, Is.EqualTo(LeadQuality.Qualified));
        }

        // --- Display ----------------------------------------------------------------------------------------------------------

        [Test]
        public async Task TheStatus_ShowsEveryOffer_AndMarksTheOneUnderDiscussion()
        {
            var states = new List<SalesSessionState>();
            await WebsiteAppointment(ToWebsite(BuyStand(new Talk()))).PlayAsync(StandVisit, states, end: false);

            var start = ProductSaleText.Status(states[0]).Split('\n');
            Assert.That(start[0], Does.StartWith("▶ " + Stand + ": noch nicht verkauft"));
            Assert.That(start[1], Is.EqualTo("• Website: noch nicht angesprochen"));

            var last = ProductSaleText.Status(states[states.Count - 1]).Split('\n');
            Assert.That(last[0], Does.StartWith("• " + Stand + ": verkauft"));
            Assert.That(last[1], Is.EqualTo("▶ Website: Termin vereinbart"));
        }

        [Test]
        public async Task TheCustomer_RemembersTheirDecisions_InTheEngine()
        {
            var talk = ToWebsite(DeclineStand(new Talk())).Turn("Was stört Sie an der Website?", "Sie ist alt.", Sees());

            await talk.PlayAsync(StandVisit);

            var decision = talk.Customer.Contexts[talk.Customer.Contexts.Count - 1].Decisions.Single();
            Assert.That((decision.Offer, decision.Decision), Is.EqualTo((Stand, "you declined to buy it")));
            Assert.That(talk.Interpreter.Contexts[0].IsMultiOffer, Is.True);
        }

        // --- Earlier kinds ----------------------------------------------------------------------------------------------------

        [Test]
        public async Task TheEarlierStandVisit_StillWorks_StandFirstWebsiteAfter()
        {
            var final = await WebsiteAppointment(ToWebsite(PayCash(BuyStand(new Talk())))).PlayAsync(EngineScenarioSource.ReviewStandPool + ":7");

            Assert.That(final.ProductSale.IsSold, Is.True);
            Assert.That(final.ProductSale.FollowUpOffer, Is.EqualTo(Web));
            Assert.That(final.Commitment, Is.EqualTo("Appointment"));
            Assert.That(final.HasLeadOffer, Is.True);
        }

        [Test]
        public async Task TheEarlierWebsitePool_HasOneOffer_AndNoProduct()
        {
            var generator = new ContentRepository(ContentDirectory).LoadGenerator(EngineScenarioSource.WebsitePool);
            var seed = Enumerable.Range(0, 500).First(s => generator.Generate(s).Scenario.Offer.Id == "website");

            var final = await WebsiteAppointment(new Talk()).PlayAsync($"{EngineScenarioSource.WebsitePool}:{seed}");

            Assert.That(final.IsMultiOffer, Is.False);
            Assert.That(final.Offers.Count, Is.EqualTo(1));
            Assert.That(final.ProductSale, Is.Null);
            Assert.That(ProductSaleText.Status(final), Is.Empty);
            Assert.That(ProductSaleText.ClosedDealsLine(final), Is.Empty);
        }
    }
}
