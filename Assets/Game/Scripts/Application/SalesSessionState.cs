namespace SalesSim.Application
{
    /// <summary>
    /// Read-only snapshot of a sales conversation as reported by the engine.
    /// Stage, Intent, EndReason and Commitment are kept as opaque strings so Unity does not mirror the engine's domain model.
    /// A product sold directly (purchase, setup, payment) comes as <see cref="ProductSale"/>.
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

        /// <summary>
        /// The next step the engine considers agreed for the lead offer (e.g. "Appointment"); empty when none was secured. In a
        /// conversation with a directly sold product this is the follow-up offer's commitment — buying the product is not a lead.
        /// </summary>
        public string Commitment { get; }

        /// <summary>
        /// Whether the conversation was about an offer that is sold as a lead (e.g. a website): always for ordinary scenarios; with
        /// a directly sold product only once the player moved on to the follow-up offer. Without it there is no lead to price.
        /// </summary>
        public bool HasLeadOffer { get; }

        /// <summary>The directly sold product as the engine reports it; <c>null</c> in ordinary scenarios.</summary>
        public ProductSaleState ProductSale { get; }

        /// <summary>The prepared website draft; <c>null</c> when none was prepared.</summary>
        public WebsitePrototypeState Prototype { get; }

        /// <summary>Set once the conversation ended with the demo link and no decision: the follow-up the engine wants played.</summary>
        public FollowUpDue FollowUp { get; }

        /// <summary>The website lead is decided in the follow-up, not now (so it is never priced twice).</summary>
        public bool LeadDeferred { get; }

        public SalesSessionState(
            string sessionId,
            string customerMessage,
            SalesIndicators indicators,
            string stage,
            string intent,
            bool isConversationOver,
            string endReason = "",
            string commitment = "",
            ProductSaleState productSale = null,
            bool hasLeadOffer = true,
            WebsitePrototypeState prototype = null,
            FollowUpDue followUp = null,
            bool leadDeferred = false)
        {
            SessionId = sessionId ?? string.Empty;
            CustomerMessage = customerMessage ?? string.Empty;
            Indicators = indicators ?? SalesIndicators.None;
            Stage = stage ?? string.Empty;
            Intent = intent ?? string.Empty;
            IsConversationOver = isConversationOver;
            EndReason = endReason ?? string.Empty;
            Commitment = commitment ?? string.Empty;
            ProductSale = productSale;
            HasLeadOffer = hasLeadOffer;
            Prototype = prototype;
            FollowUp = followUp;
            LeadDeferred = leadDeferred;
        }
    }
}
