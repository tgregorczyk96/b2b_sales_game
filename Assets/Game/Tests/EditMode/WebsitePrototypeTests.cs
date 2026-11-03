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

namespace SalesSim.Tests.EditMode
{
    /// <summary>
    /// The prepared website draft through the real engine and adapter (scripted customer and interpreter, no API): presenting,
    /// the 7-day demo link, the follow-up started from the engine's saved record, immediate and delayed sales, and the lead
    /// priced exactly once — also together with the review stand.
    /// </summary>
    public sealed class WebsitePrototypeTests
    {
        private static string ContentDirectory => Path.Combine(UnityEngine.Application.streamingAssetsPath, "SalesEngine", "content");

        private sealed class Customer : ICustomerSimulator
        {
            private readonly Queue<string> replies = new Queue<string>();

            public List<CustomerSimulationContext> Contexts { get; } = new List<CustomerSimulationContext>();

            public void Says(string reply) => replies.Enqueue(reply);

            public Task<CustomerSimulationResponse> SimulateAsync(CustomerSimulationContext context, CancellationToken cancellationToken)
            {
                Contexts.Add(context);
                return Task.FromResult(new CustomerSimulationResponse(replies.Dequeue(),
                    new SalesEngine.Customers.CustomerReaction(Engagement.Engaged, TrustSignal.Positive, InterestSignal.Positive, PatienceSignal.Stable, ConversationIntent.Continue)));
            }
        }

        private sealed class Interpreter : IConversationInterpreter
        {
            private readonly Queue<ConversationInterpretation> seen = new Queue<ConversationInterpretation>();

            public List<ConversationInterpretationContext> Contexts { get; } = new List<ConversationInterpretationContext>();

            public void Sees(ConversationInterpretation interpretation) => seen.Enqueue(interpretation);

            public Task<ConversationInterpretation> InterpretAsync(ConversationInterpretationContext context, CancellationToken cancellationToken)
            {
                Contexts.Add(context);
                return Task.FromResult(seen.Dequeue());
            }
        }

        private sealed class Visit
        {
            public readonly Customer Customer = new Customer();
            public readonly Interpreter Interpreter = new Interpreter();
            private readonly List<string> turns = new List<string>();

            public Visit Turn(string trainee, string reply, ConversationInterpretation seen)
            {
                turns.Add(trainee);
                Customer.Says(reply);
                Interpreter.Sees(seen);
                return this;
            }

            public async Task<SalesSessionState> PlayAsync(string scenarioId, string followUpPayload = null, bool end = true)
            {
                var session = new SalesEngineGameSession(ContentDirectory,
                    () => new SalesSimulationEngine(null, Customer, Interpreter, SimulatedPaymentProcessor.AlwaysSucceeds), DifficultyProfile.Easy);
                Start = await session.StartSessionAsync(new SessionStartRequest(scenarioId, null, null, followUpPayload));
                foreach (var turn in turns)
                {
                    await session.SendPlayerTurnAsync(new PlayerTurn(turn));
                }

                return end ? await session.EndSessionAsync() : session.CurrentState;
            }

            public SalesSessionState Start { get; private set; }
        }

        private static ConversationInterpretation Sees(PrototypeObservation[] prototype = null, NextStepProposal[] steps = null, SaleObservations sale = null) =>
            new ConversationInterpretation(new InterpretedCue[0], null, null, null, steps, null, sale, prototype);

        private static PrototypeObservation Event(PrototypeEventKind kind, string quote, Speaker by = Speaker.Trainee, HypothesisResponse response = HypothesisResponse.Unanswered,
            string customerQuote = null, string detail = "") => new PrototypeObservation(kind, detail, by, quote, response, customerQuote);

        private static NextStepProposal Order(int turn, string proposal, string answer) =>
            new NextStepProposal(Speaker.Trainee, CommitmentKind.Agreement, StepSpecificity.Concrete, turn, proposal, StepResponse.Accepted, answer);

        private const string PrototypeScenario = EngineScenarioSource.PrototypePool + ":3";
        private const string StandScenario = EngineScenarioSource.ReviewStandPool + ":3";

        private static Visit PresentAndHandOver(Visit visit) => visit
            .Turn("Ich habe Ihnen schon einen Entwurf für eine neue Website vorbereitet, hier auf dem Tablet.", "Oh, zeigen Sie mal.",
                Sees(new[] { Event(PrototypeEventKind.Presented, "hier auf dem Tablet", response: HypothesisResponse.Confirmed, customerQuote: "zeigen Sie mal") }))
            .Turn("Ich schicke Ihnen einen Demo-Link, schauen Sie ihn sich sieben Tage in Ruhe an.", "Ja, gerne.",
                Sees(new[] { Event(PrototypeEventKind.DemoLinkOffered, "Ich schicke Ihnen einen Demo-Link", response: HypothesisResponse.Confirmed, customerQuote: "Ja, gerne") }));

        [Test]
        public async Task NamedPrices_AreCheckedAgainstThePublishedPriceList_AndShownInTheResult()
        {
            var visit = new Visit()
                .Turn("Als Gründungspartner zahlen Sie 799 Euro einmalig und 99 Euro im Monat.", "Hm, und Premium?",
                    Sees(new[]
                    {
                        Event(PrototypeEventKind.PriceNamed, "799 Euro einmalig", detail: "799"),
                        Event(PrototypeEventKind.PriceNamed, "99 Euro im Monat", detail: "99"),
                    }))
                .Turn("Premium kostet 1.499 Euro.", "Aha.", Sees(new[] { Event(PrototypeEventKind.PriceNamed, "Premium kostet 1.499 Euro", detail: "1.499") }));

            var final = await visit.PlayAsync(PrototypeScenario);

            Assert.That(final.Prototype.PricesNamed, Is.EqualTo("799 €, 99 €, 1.499 €"));
            Assert.That(final.Prototype.WrongPrices, Is.EqualTo("1.499 €"), "Premium costs 1.799 € once");
        }

        [Test]
        public async Task NotPrepared_HasNoPrototype_AndNoFollowUp()
        {
            var final = await new Visit().Turn("Hallo!", "Hallo.", Sees()).PlayAsync(EngineScenarioSource.DefaultPool + ":1406361028");

            Assert.That(final.Prototype, Is.Null);
            Assert.That(final.FollowUp, Is.Null);
            Assert.That(final.LeadDeferred, Is.False);
        }

        [Test]
        public async Task Prepared_Presented_DemoHandedOver_EndsWithAFollowUpDue_AndADeferredLead()
        {
            var visit = PresentAndHandOver(new Visit());

            var final = await visit.PlayAsync(PrototypeScenario);

            Assert.That(visit.Start.Prototype.Status, Is.EqualTo("Prepared"));
            Assert.That(final.Prototype.Presented && final.Prototype.DemoHandedOver, Is.True);
            Assert.That(final.Prototype.Status, Is.EqualTo("InReview"));
            Assert.That(final.Prototype.Decision, Is.EqualTo("Open"));
            Assert.That(final.FollowUp, Is.Not.Null);
            Assert.That(final.FollowUp.DaysLater, Is.EqualTo(7));
            Assert.That(final.FollowUp.Payload, Is.Not.Empty);
            Assert.That(final.LeadDeferred, Is.True);
            var lead = LeadEvaluator.Evaluate(final, EconomySettings.Default);
            Assert.That((lead.IsDeferred, lead.Value), Is.EqualTo((true, 0)), "the website lead is priced after the follow-up, not now");
        }

        [Test]
        public async Task FollowUp_StartsFromTheSavedRecord_ChangeWishes_ThenTheOrder_PricesTheLeadOnce()
        {
            var first = await PresentAndHandOver(new Visit()).PlayAsync(PrototypeScenario);

            var visit = new Visit()
                .Turn("Haben Sie sich den Entwurf angeschaut?", "Ja, aber ich hätte gern eigene Fotos.",
                    Sees(new[]
                    {
                        Event(PrototypeEventKind.Feedback, "Ja", Speaker.Customer, detail: "WantsChanges"),
                        Event(PrototypeEventKind.ChangeRequested, "ich hätte gern eigene Fotos", Speaker.Customer, detail: "eigene Fotos"),
                    }))
                .Turn("Machen wir. Sollen wir die Website so umsetzen?", "Ja, machen wir.",
                    Sees(steps: new[] { Order(2, "Sollen wir die Website so umsetzen?", "Ja, machen wir") }));
            var final = await visit.PlayAsync(PrototypeScenario, first.FollowUp.Payload);

            Assert.That(visit.Start.CustomerMessage, Is.EqualTo("Ach, hallo – Sie waren doch letzte Woche schon hier."));
            Assert.That(visit.Start.Prototype.IsFollowUp, Is.True);
            Assert.That(visit.Start.Prototype.Status, Is.EqualTo("InReview"));
            Assert.That(visit.Customer.Contexts[0].Scenario.Continuation.PreviousTurns.Count, Is.GreaterThan(4), "the customer remembers the first visit");
            Assert.That(visit.Interpreter.Contexts[0].FollowUpAfterDays, Is.EqualTo(7));
            Assert.That(final.Prototype.Feedback, Is.EqualTo("WantsChanges"));
            Assert.That(final.Prototype.ChangeRequests, Is.EqualTo(1));
            Assert.That(final.Prototype.Decision, Is.EqualTo("Bought"));
            Assert.That(final.FollowUp, Is.Null, "no second follow-up");
            Assert.That(final.LeadDeferred, Is.False);
            Assert.That(final.Commitment, Is.EqualTo("Agreement"));
            Assert.That(LeadEvaluator.Evaluate(final, EconomySettings.Default).Quality, Is.EqualTo(LeadQuality.Qualified));
        }

        [Test]
        public async Task FollowUp_NotViewed_ThenEnded_IsOpenOrDeclined_ButNeverASale()
        {
            var first = await PresentAndHandOver(new Visit()).PlayAsync(PrototypeScenario);
            var visit = new Visit().Turn("Wie fanden Sie den Entwurf?", "Hab ihn ehrlich gesagt noch nicht angeschaut.",
                Sees(new[] { Event(PrototypeEventKind.Feedback, "noch nicht angeschaut", Speaker.Customer, detail: "NotViewed") }));

            var final = await visit.PlayAsync(PrototypeScenario, first.FollowUp.Payload);

            Assert.That(final.Prototype.Feedback, Is.EqualTo("NotViewed"));
            Assert.That(final.Prototype.Decision, Is.Not.EqualTo("Bought"));
            Assert.That(final.Commitment, Is.Not.EqualTo("Agreement"));
        }

        [Test]
        public async Task ImmediateSale_InTheFirstVisit_PricesTheLeadNow_WithoutFollowUp()
        {
            var visit = new Visit()
                .Turn("Ich habe Ihnen schon einen Entwurf vorbereitet, hier auf dem Tablet.", "Oh, schön.",
                    Sees(new[] { Event(PrototypeEventKind.Presented, "hier auf dem Tablet", response: HypothesisResponse.Confirmed, customerQuote: "Oh, schön") }))
                .Turn("Wollen wir das so umsetzen?", "Ja, sofort.", Sees(steps: new[] { Order(2, "Wollen wir das so umsetzen?", "Ja, sofort") }));

            var final = await visit.PlayAsync(PrototypeScenario);

            Assert.That(final.Prototype.Decision, Is.EqualTo("Bought"));
            Assert.That(final.FollowUp, Is.Null);
            Assert.That(final.LeadDeferred, Is.False);
            Assert.That(LeadEvaluator.Evaluate(final, EconomySettings.Default).Value, Is.EqualTo(1000));
        }

        [Test]
        public async Task WithTheReviewStand_StandBookedNow_WebsiteLeadAfterTheFollowUp_NothingTwice()
        {
            var visit = PresentAndHandOver(new Visit().Turn("Der Aufsteller kostet einmalig 39 Euro. Wollen Sie einen?", "Ja, hier sind 39 Euro bar.",
                Sees(steps: new[] { Order(1, "Wollen Sie einen?", "Ja") },
                    sale: new SaleObservations(payments: new[] { new PaymentObservation(PaymentEventKind.PaymentSucceeded, PaymentMethod.Cash, Speaker.Customer, "hier sind 39 Euro bar") }))));
            var first = await visit.PlayAsync(StandScenario);
            var wallet = new PlayerWallet();
            ProductSettlement.Of(first, false).CreditTo(wallet);
            var firstLead = new LeadSale(LeadEvaluator.Evaluate(first, EconomySettings.Default));

            Assert.That(first.ProductSale.IsSold, Is.True);
            Assert.That(first.ProductSale.FollowUpOffer, Is.EqualTo("Website"));
            Assert.That(first.FollowUp, Is.Not.Null);
            Assert.That(firstLead.CanSell, Is.False, "deferred to the follow-up");
            Assert.That(wallet.Balance, Is.EqualTo(39));

            var followUp = await new Visit().Turn("Sollen wir die Website umsetzen?", "Ja.",
                Sees(steps: new[] { Order(1, "Sollen wir die Website umsetzen?", "Ja") })).PlayAsync(StandScenario, first.FollowUp.Payload);
            Assert.That(followUp.ProductSale, Is.Null, "the stand is not sold again");
            Assert.That(ProductSettlement.Of(followUp, false).CreditTo(wallet), Is.EqualTo(0));
            new LeadSale(LeadEvaluator.Evaluate(followUp, EconomySettings.Default)).SellTo(wallet);
            Assert.That(wallet.Balance, Is.EqualTo(39 + 700));
        }

        [Test]
        public void FollowUpRuns_ContinueTheScenario_AndInheritTraining()
        {
            var runs = new RunRegistry();
            var first = runs.Begin(3, "Easy", PrototypeScenario);

            var followUp = runs.BeginFollowUp(3, "Easy", PrototypeScenario, first.IsRerun);
            var trainingFollowUp = runs.BeginFollowUp(3, "Easy", PrototypeScenario, isTraining: true);

            Assert.That((followUp.IsFollowUp, followUp.IsRerun), Is.EqualTo((true, false)));
            Assert.That(trainingFollowUp.IsRerun, Is.True);
        }

        [Test]
        public void ScenarioSource_OffersThePrototypeVisit()
        {
            var source = new EngineScenarioSource(ContentDirectory);
            source.Select(EngineScenarioSource.PrototypePool);
            var id = source.ScenarioIdFor(source.PickRandomSeed());

            Assert.That(id, Does.StartWith(EngineScenarioSource.PrototypePool + ":"));
            Assert.That(new ContentRepository(ContentDirectory).LoadGenerator(EngineScenarioSource.PrototypePool).Generate(3).Scenario.Prototype.DemoDays, Is.EqualTo(7));
        }
    }
}
