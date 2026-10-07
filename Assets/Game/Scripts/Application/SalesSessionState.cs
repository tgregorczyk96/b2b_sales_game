namespace SalesSim.Application
{
    /// <summary>
    /// Read-only snapshot of a sales conversation as reported by the engine.
    /// Stage, Intent, EndReason and Commitment are kept as opaque strings so Unity does not mirror the engine's domain model.
    /// </summary>
    public sealed class SalesSessionState
    {
        public static readonly SalesSessionState Empty =
            new SalesSessionState(string.Empty, string.Empty, SalesIndicators.None, string.Empty, string.Empty, false);

        public string SessionId { get; }
        public string CustomerMessage { get; }
        public SalesIndicators Indicators { get; }
        public string Stage { get; }
        public string Intent { get; }
        public bool IsConversationOver { get; }

        /// <summary>Why the conversation ended, as the engine reports it (e.g. "Completed"); empty while it runs.</summary>
        public string EndReason { get; }

        /// <summary>The next step the engine considers agreed (e.g. "Appointment"); empty when none was secured.</summary>
        public string Commitment { get; }

        public SalesSessionState(
            string sessionId,
            string customerMessage,
            SalesIndicators indicators,
            string stage,
            string intent,
            bool isConversationOver,
            string endReason = "",
            string commitment = "")
        {
            SessionId = sessionId ?? string.Empty;
            CustomerMessage = customerMessage ?? string.Empty;
            Indicators = indicators ?? SalesIndicators.None;
            Stage = stage ?? string.Empty;
            Intent = intent ?? string.Empty;
            IsConversationOver = isConversationOver;
            EndReason = endReason ?? string.Empty;
            Commitment = commitment ?? string.Empty;
        }
    }
}
