namespace SalesSim.Application
{
    /// <summary>
    /// The website draft the player prepared before the visit, as the engine reports it: status, demo link, feedback, change
    /// wishes and the decision. Values are the engine's names as opaque strings; Unity only displays them.
    /// </summary>
    public sealed class WebsitePrototypeState
    {
        public WebsitePrototypeState(
            string status, bool presented, bool demoHandedOver, bool isFollowUp, string feedback, int changeRequests, string decision, int demoDays)
        {
            Status = status ?? string.Empty;
            Presented = presented;
            DemoHandedOver = demoHandedOver;
            IsFollowUp = isFollowUp;
            Feedback = feedback ?? string.Empty;
            ChangeRequests = changeRequests;
            Decision = decision ?? string.Empty;
            DemoDays = demoDays;
        }

        /// <summary>"Prepared", "Presented", "DemoLinkHandedOver", "InReview", "FeedbackReceived" or "Decided".</summary>
        public string Status { get; }

        public bool Presented { get; }

        /// <summary>The customer accepted the demo link in this (first) conversation.</summary>
        public bool DemoHandedOver { get; }

        /// <summary>This conversation is the follow-up after the demo period.</summary>
        public bool IsFollowUp { get; }

        /// <summary>What the customer said about the demo (e.g. "WantsChanges"); empty until said.</summary>
        public string Feedback { get; }

        public int ChangeRequests { get; }

        /// <summary>"Open", "Bought" or "Declined".</summary>
        public string Decision { get; }

        public int DemoDays { get; }
    }

    /// <summary>
    /// A follow-up the engine considers due after the conversation (demo link handed over, nothing decided). The payload is the
    /// engine's own record of the first visit; Unity stores it unchanged and hands it back to start the follow-up.
    /// </summary>
    public sealed class FollowUpDue
    {
        public FollowUpDue(string payload, int daysLater)
        {
            Payload = payload ?? string.Empty;
            DaysLater = daysLater;
        }

        public string Payload { get; }

        /// <summary>The demo period, e.g. 7 — skipped in the game, no real waiting.</summary>
        public int DaysLater { get; }
    }
}
