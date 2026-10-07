using System.Threading;
using System.Threading.Tasks;
using SalesSim.Application;

namespace SalesSim.Infrastructure
{
    /// <summary>
    /// Placeholder adapter used until the external Sales Engine is connected.
    /// Deliberately contains no sales logic: it never changes indicators, stage or intent.
    /// </summary>
    public sealed class NotConnectedSalesGameSession : ISalesGameSession
    {
        private const string NotConnected = "not connected";

        public SalesSessionState CurrentState { get; private set; } = SalesSessionState.Empty;

        public Task<SalesSessionState> StartSessionAsync(SessionStartRequest request, CancellationToken cancellationToken = default)
        {
            CurrentState = CreateState("[Sales Engine nicht verbunden]");
            return Task.FromResult(CurrentState);
        }

        public Task<SalesSessionState> SendPlayerTurnAsync(PlayerTurn turn, CancellationToken cancellationToken = default)
        {
            CurrentState = CreateState($"[Sales Engine nicht verbunden] Empfangen: \"{turn.Message}\"");
            return Task.FromResult(CurrentState);
        }

        public Task<SalesSessionState> EndSessionAsync(CancellationToken cancellationToken = default)
        {
            CurrentState = new SalesSessionState("offline", string.Empty, SalesIndicators.None, NotConnected, NotConnected, true);
            return Task.FromResult(CurrentState);
        }

        public Task<GuidanceResult> RequestGuidanceAsync(GuidanceLevel level, CancellationToken cancellationToken = default)
        {
            return Task.FromException<GuidanceResult>(
                new GuidanceUnavailableException(GuidanceUnavailableReason.NotConnected, "The Sales Engine is not connected."));
        }

        public GuidanceAvailability GetGuidanceAvailability(GuidanceLevel level)
        {
            return GuidanceAvailability.NotConnected;
        }

        private static SalesSessionState CreateState(string customerMessage)
        {
            return new SalesSessionState("offline", customerMessage, SalesIndicators.None, NotConnected, NotConnected, false);
        }
    }
}
