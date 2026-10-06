namespace SalesSim.Application
{
    /// <summary>
    /// Read-only snapshot of a sales conversation as reported by the engine.
    /// Stage and Intent are kept as opaque strings so Unity does not mirror the engine's domain model.
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

        public SalesSessionState(
            string sessionId,
            string customerMessage,
            SalesIndicators indicators,
            string stage,
            string intent,
            bool isConversationOver)
        {
            SessionId = sessionId ?? string.Empty;
            CustomerMessage = customerMessage ?? string.Empty;
            Indicators = indicators ?? SalesIndicators.None;
            Stage = stage ?? string.Empty;
            Intent = intent ?? string.Empty;
            IsConversationOver = isConversationOver;
        }
    }
}
