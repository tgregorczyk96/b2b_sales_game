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

        /// <summary>The player ends the running conversation; the engine decides how it ended. Returns the final state.</summary>
        Task<SalesSessionState> EndSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Live guidance for the player's next turn, from the engine's training coach. Read-only towards the conversation:
        /// it is never sent to the customer and changes no state. Request it between turns, one at a time.
        /// </summary>
        /// <exception cref="GuidanceUnavailableException">The level is not available right now or could not be produced.</exception>
        Task<GuidanceResult> RequestGuidanceAsync(GuidanceLevel level, CancellationToken cancellationToken = default);

        /// <summary>Whether <paramref name="level"/> can be requested now, according to the engine. Cheap; no AI call.</summary>
        GuidanceAvailability GetGuidanceAvailability(GuidanceLevel level);
    }
}
