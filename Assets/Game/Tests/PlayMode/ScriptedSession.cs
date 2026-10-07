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
            CurrentState = new SalesSessionState("scripted", "Guten Tag?", Start, "Opening", string.Empty, false);
            return Task.FromResult(CurrentState);
        }

        public Task<SalesSessionState> SendPlayerTurnAsync(PlayerTurn turn, CancellationToken cancellationToken = default)
        {
            TurnsReceived++;
            pending = new TaskCompletionSource<SalesSessionState>();
            return pending.Task;
        }

        public Task<SalesSessionState> EndSessionAsync(CancellationToken cancellationToken = default)
        {
            CurrentState = new SalesSessionState("scripted", string.Empty, CurrentState.Indicators, "Ended", CurrentState.Intent, true, "NotStated");
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

        public void Fail(Exception exception)
        {
            pending.SetException(exception);
        }
    }
}
