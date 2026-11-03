using System;

namespace SalesSim.Application
{
    /// <summary>
    /// Read-only snapshot of a product the player sells directly in the conversation (e.g. the Google review stand), exactly as
    /// the engine reports it. Purchase, on-site setup and payment are separate facts; the engine decides all of them. Unity only
    /// displays the values and books <see cref="BookedRevenue"/> — it never derives a purchase, a setup or a payment itself.
    /// Status values are the engine's names, kept as opaque strings like <see cref="SalesSessionState.Stage"/>.
    /// </summary>
    public sealed class ProductSaleState
    {
        public ProductSaleState(
            string productName,
            decimal price,
            string currency,
            bool isSold,
            bool purchaseDeclined,
            bool nfcExplained,
            bool qrCodeExplained,
            bool onSiteSetupPossible,
            string setupStatus,
            int setupStepsDone,
            int setupStepsRequired,
            string paymentMethod,
            string paymentStatus,
            decimal bookedRevenue,
            decimal outstandingRevenue,
            string result,
            string followUpOffer,
            string followUpCommitment,
            int questionableClaims = 0)
        {
            if (price < 0m || bookedRevenue < 0m || outstandingRevenue < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(price), "Amounts are never negative.");
            }

            ProductName = productName ?? string.Empty;
            Price = price;
            Currency = currency ?? string.Empty;
            IsSold = isSold;
            PurchaseDeclined = purchaseDeclined;
            NfcExplained = nfcExplained;
            QrCodeExplained = qrCodeExplained;
            OnSiteSetupPossible = onSiteSetupPossible;
            SetupStatus = setupStatus ?? string.Empty;
            SetupStepsDone = setupStepsDone;
            SetupStepsRequired = setupStepsRequired;
            PaymentMethod = paymentMethod ?? string.Empty;
            PaymentStatus = paymentStatus ?? string.Empty;
            BookedRevenue = bookedRevenue;
            OutstandingRevenue = outstandingRevenue;
            Result = result ?? string.Empty;
            FollowUpOffer = followUpOffer ?? string.Empty;
            FollowUpCommitment = followUpCommitment ?? string.Empty;
            QuestionableClaims = questionableClaims;
        }

        /// <summary>The product's display name, e.g. "Google-Bewertungsaufsteller".</summary>
        public string ProductName { get; }

        /// <summary>The fixed one-time price from the engine's product data, e.g. 39.00.</summary>
        public decimal Price { get; }

        /// <summary>ISO code, e.g. "EUR".</summary>
        public string Currency { get; }

        /// <summary>The customer agreed to buy.</summary>
        public bool IsSold { get; }

        /// <summary>The customer declined to buy (and did not agree later).</summary>
        public bool PurchaseDeclined { get; }

        public bool NfcExplained { get; }

        public bool QrCodeExplained { get; }

        /// <summary>The visit allows setting it up on site (walk-in).</summary>
        public bool OnSiteSetupPossible { get; }

        /// <summary>"NotStarted", "InProgress" or "Completed".</summary>
        public string SetupStatus { get; }

        public bool SetupCompleted => SetupStatus == "Completed";

        /// <summary>Required setup steps done so far (review link, linking, NFC test, QR test).</summary>
        public int SetupStepsDone { get; }

        public int SetupStepsRequired { get; }

        /// <summary>"BankTransfer", "PayPal", "Cash", "Card" or empty while none was agreed.</summary>
        public string PaymentMethod { get; }

        /// <summary>"NotStarted", "Open", "Confirmed" or "Failed".</summary>
        public string PaymentStatus { get; }

        /// <summary>Received money: the price once the payment is confirmed, else 0. The only amount the game books.</summary>
        public decimal BookedRevenue { get; }

        /// <summary>Sold but not (yet) paid. Shown as open, never booked.</summary>
        public decimal OutstandingRevenue { get; }

        /// <summary>"NotSold", "Declined", "SoldSetupOpen" or "SoldAndSetUp".</summary>
        public string Result { get; }

        /// <summary>The offer the player moved on to (e.g. "Website"); empty while none.</summary>
        public string FollowUpOffer { get; }

        public bool FollowUpStarted => FollowUpOffer.Length > 0;

        /// <summary>The next step agreed for the follow-up offer (e.g. "Appointment"); empty without one.</summary>
        public string FollowUpCommitment { get; }

        /// <summary>Product claims of the player that contradict the product data or promise something it does not cover.</summary>
        public int QuestionableClaims { get; }
    }
}
