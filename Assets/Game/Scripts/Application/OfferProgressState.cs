namespace SalesSim.Application
{
    /// <summary>
    /// Read-only snapshot of one offer of the conversation (e.g. the review stand or the website), exactly as the engine reports
    /// it: whether it came up, whether it is the topic right now, the next step agreed for it and whether the customer said no.
    /// Several offers can be discussed and sold in one conversation; each keeps its own result. Purchase, setup and payment of a
    /// product sold directly are in <see cref="SalesSessionState.ProductSale"/>. Values are the engine's names as opaque strings.
    /// </summary>
    public sealed class OfferProgressState
    {
        public OfferProgressState(
            string offerId, string name, bool isDirectSale, bool isOccasion, bool isActive, bool discussed, string commitment, bool declined)
        {
            OfferId = offerId ?? string.Empty;
            Name = name ?? string.Empty;
            IsDirectSale = isDirectSale;
            IsOccasion = isOccasion;
            IsActive = isActive;
            Discussed = discussed;
            Commitment = commitment ?? string.Empty;
            Declined = declined;
        }

        /// <summary>The engine's offer id, e.g. "website".</summary>
        public string OfferId { get; }

        /// <summary>The offer's display name, e.g. "Website" or "Google-Bewertungsaufsteller".</summary>
        public string Name { get; }

        /// <summary>A product sold directly (price, payment, setup); otherwise a lead offer priced by the game economy.</summary>
        public bool IsDirectSale { get; }

        /// <summary>The offer the contact was made for.</summary>
        public bool IsOccasion { get; }

        /// <summary>The offer under discussion right now.</summary>
        public bool IsActive { get; }

        /// <summary>The offer observably came up in the conversation.</summary>
        public bool Discussed { get; }

        /// <summary>The next step agreed for this offer (e.g. "Agreement", "Appointment"); empty without one.</summary>
        public string Commitment { get; }

        /// <summary>The customer said no to this offer and agreed to nothing for it afterwards.</summary>
        public bool Declined { get; }
    }
}
