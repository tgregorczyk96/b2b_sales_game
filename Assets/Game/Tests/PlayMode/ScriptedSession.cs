using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalesSim.Application;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>
    /// <see cref="ISalesGameSession"/> for UI tests: answers each turn only when the test says so, with the values the test
    /// chooses. Guidance: Hint and HintMore answer at once (deterministic, like the engine's rules); the Example waits
    /// for <see cref="AnswerExample"/> or <see cref="FailExample"/>, like the AI call. No engine, no API.
    /// </summary>
    internal sealed class ScriptedSession : ISalesGameSession
    {
        public static readonly SalesIndicators Start = new SalesIndicators(40, 50, 25, 70);

        public const string HintText = "Finde heraus, ob aktuell freie Kapazitäten bestehen.";
        public const string HintMoreText = "Frage konkret nach Tagen oder Zeiten mit freien Terminen.";
        public const string ExampleText = "Gibt es bestimmte Tage, an denen Sie gerne mehr neue Kundinnen hätten?";

        private TaskCompletionSource<SalesSessionState> pending;
        private TaskCompletionSource<GuidanceResult> pendingExample;

        public SalesSessionState CurrentState { get; private set; } = SalesSessionState.Empty;

        public int TurnsReceived { get; private set; }

        public int Starts { get; private set; }

        /// <summary>Every guidance request in order, including refused or failed ones.</summary>
        public List<GuidanceLevel> GuidanceRequests { get; } = new List<GuidanceLevel>();

        /// <summary>What the session reports per level while the conversation runs (default: everything available).</summary>
        public Dictionary<GuidanceLevel, GuidanceAvailability> Availability { get; } = new Dictionary<GuidanceLevel, GuidanceAvailability>
        {
            [GuidanceLevel.Hint] = GuidanceAvailability.Available,
            [GuidanceLevel.HintMore] = GuidanceAvailability.Available,
            [GuidanceLevel.Example] = GuidanceAvailability.Available,
        };

        /// <summary>The request of the latest start (scenario id, player, difficulty).</summary>
        public SessionStartRequest LastStart { get; private set; }

        public Task<SalesSessionState> StartSessionAsync(SessionStartRequest request, CancellationToken cancellationToken = default)
        {
            Starts++;
            LastStart = request;
            // Like the engine: a review stand visit starts with the product unsold and without a lead offer.
            var stand = request.ScenarioId.StartsWith(ScriptedScenarioSource.StandKind + ":", StringComparison.Ordinal);
            // Both offers (visit or call): the stand is the occasion and the topic, the website comes along; a lead only once it came up.
            var call = request.ScenarioId.StartsWith(ScriptedScenarioSource.MultiCallKind + ":", StringComparison.Ordinal);
            var multi = call || request.ScenarioId.StartsWith(ScriptedScenarioSource.MultiVisitKind + ":", StringComparison.Ordinal);
            stand = stand || multi;
            var offers = multi ? Offers(standActive: true) : null;
            var product = stand
                ? new ProductSaleState("Google-Bewertungsaufsteller", 39m, "EUR", false, false, false, false, !call, "NotStarted", 0, 4,
                    string.Empty, "NotStarted", 0m, 0m, "NotSold", string.Empty, string.Empty)
                : null;
            // A prototype visit starts with the draft prepared; a follow-up starts in review, greeted like a returning visitor.
            var draft = request.ScenarioId.StartsWith(ScriptedScenarioSource.PrototypeKind + ":", StringComparison.Ordinal) || request.IsFollowUp
                ? new WebsitePrototypeState(request.IsFollowUp ? "InReview" : "Prepared", false, false, request.IsFollowUp, string.Empty, 0, "Open", 7)
                : null;
            CurrentState = new SalesSessionState("scripted", request.IsFollowUp ? "Ach, hallo – Sie waren doch letzte Woche schon hier." : "Guten Tag?",
                Start, "Opening", string.Empty, false, string.Empty, string.Empty, request.IsFollowUp ? null : product, !stand || request.IsFollowUp, draft, null, false, offers);
            return Task.FromResult(CurrentState);
        }

        /// <summary>Stand and website as the engine reports them; the stand is the occasion.</summary>
        public static OfferProgressState[] Offers(
            bool standActive, bool websiteDiscussed = false, string websiteCommitment = "", bool standDeclined = false, bool websiteDeclined = false) => new[]
        {
            new OfferProgressState("review-stand", "Google-Bewertungsaufsteller", true, true, standActive, true, string.Empty, standDeclined),
            new OfferProgressState("website", "Website", false, false, !standActive, websiteDiscussed || !standActive, websiteCommitment, websiteDeclined),
        };

        public Task<SalesSessionState> SendPlayerTurnAsync(PlayerTurn turn, CancellationToken cancellationToken = default)
        {
            TurnsReceived++;
            pending = new TaskCompletionSource<SalesSessionState>();
            return pending.Task;
        }

        public Task<SalesSessionState> EndSessionAsync(CancellationToken cancellationToken = default)
        {
            CurrentState = new SalesSessionState("scripted", string.Empty, CurrentState.Indicators, "Ended", CurrentState.Intent, true, "NotStated",
                CurrentState.Commitment, CurrentState.ProductSale, CurrentState.HasLeadOffer, CurrentState.Prototype, EndFollowUp, EndFollowUp != null, CurrentState.Offers);
            return Task.FromResult(CurrentState);
        }

        public Task<GuidanceResult> RequestGuidanceAsync(GuidanceLevel level, CancellationToken cancellationToken = default)
        {
            GuidanceRequests.Add(level);

            switch (level)
            {
                case GuidanceLevel.Hint:
                    return Task.FromResult(new GuidanceResult(level, HintText, "ExploreSituation"));
                case GuidanceLevel.HintMore:
                    return Task.FromResult(new GuidanceResult(level, HintMoreText, "ExploreSituation"));
                default:
                    pendingExample = new TaskCompletionSource<GuidanceResult>();
                    return pendingExample.Task;
            }
        }

        public GuidanceAvailability GetGuidanceAvailability(GuidanceLevel level)
        {
            return CurrentState.IsConversationOver ? GuidanceAvailability.ConversationOver : Availability[level];
        }

        public void AnswerExample(string sentence = ExampleText)
        {
            pendingExample.SetResult(new GuidanceResult(GuidanceLevel.Example, sentence, "ExploreSituation"));
        }

        public void FailExample()
        {
            pendingExample.SetException(new GuidanceUnavailableException(GuidanceUnavailableReason.GenerationFailed, "scripted AI failure"));
        }

        public void Answer(string customerMessage, float trust = 40)
        {
            Answer(customerMessage, new SalesIndicators(trust, 50, 25, 70));
        }

        public void Answer(string customerMessage, SalesIndicators indicators, bool over = false, string endReason = "", string commitment = "")
        {
            CurrentState = new SalesSessionState(
                "scripted", customerMessage, indicators, over ? "Ended" : "Opening", "Continue", over, endReason, commitment);
            pending.SetResult(CurrentState);
        }

        /// <summary>What <see cref="EndSessionAsync"/> reports as due follow-up (e.g. after a demo link); <c>null</c> = none.</summary>
        public FollowUpDue EndFollowUp { get; set; }

        /// <summary>Answers the pending turn with a complete state, e.g. one with a product sale.</summary>
        public void Answer(SalesSessionState state)
        {
            CurrentState = state;
            pending.SetResult(CurrentState);
        }

        public void Fail(Exception exception)
        {
            pending.SetException(exception);
        }
    }
}
