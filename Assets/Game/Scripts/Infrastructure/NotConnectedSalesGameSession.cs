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

        private static SalesSessionState CreateState(string customerMessage)
        {
            return new SalesSessionState("offline", customerMessage, SalesIndicators.None, NotConnected, NotConnected, false);
        }
    }
}
