using System.Threading;
using System.Threading.Tasks;

namespace SalesSim.Application
{
    /// <summary>
    /// Thin, provider-independent port to the external Sales Simulator Engine.
    /// Unity only forwards player input and renders the returned state; all sales rules
    /// (trust calculation, customer behavior, conversation evaluation) live in the engine.
    /// </summary>
    public interface ISalesGameSession
    {
        /// <summary>Latest state reported by the engine. <see cref="SalesSessionState.Empty"/> before a session is started.</summary>
        SalesSessionState CurrentState { get; }

        Task<SalesSessionState> StartSessionAsync(SessionStartRequest request, CancellationToken cancellationToken = default);

        Task<SalesSessionState> SendPlayerTurnAsync(PlayerTurn turn, CancellationToken cancellationToken = default);
    }
}
