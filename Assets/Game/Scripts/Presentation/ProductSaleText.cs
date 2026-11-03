using System.Globalization;
using SalesSim.Application;
using SalesSim.Game;

namespace SalesSim.Presentation
{
    /// <summary>
    /// German wording for a directly sold product (live status line and result screen). Wording only: every value comes from
    /// the engine's <see cref="ProductSaleState"/>; nothing is derived here.
    /// </summary>
    public static class ProductSaleText
    {
        /// <summary>The product with its price, e.g. "Google-Bewertungsaufsteller (39,00 €)".</summary>
        public static string Title(ProductSaleState sale) => $"{sale.ProductName} ({Amount(sale.Price, sale.Currency)})";

        /// <summary>One line during the conversation, e.g. "Aufsteller: verkauft · Einrichtung 2/4 · Zahlung: Karte – offen".</summary>
        public static string Status(ProductSaleState sale)
        {
            if (sale == null)
            {
                return string.Empty;
            }

            var purchase = sale.IsSold ? "verkauft" : sale.PurchaseDeclined ? "abgelehnt" : "noch nicht verkauft";
            var line = $"{sale.ProductName}: {purchase}";
            if (sale.IsSold)
            {
                line += sale.OnSiteSetupPossible
                    ? $" · Einrichtung {sale.SetupStepsDone}/{sale.SetupStepsRequired}"
                    : " · Einrichtung nicht vor Ort möglich";
                line += $" · Zahlung: {PaymentLine(sale)}";
            }
            else
            {
                line += $" · erklärt: NFC {Check(sale.NfcExplained)}, QR {Check(sale.QrCodeExplained)}";
            }

            if (sale.FollowUpStarted)
            {
                line += $" · {sale.FollowUpOffer}: im Gespräch";
            }

            return line;
        }

        /// <summary>The product block of the result screen.</summary>
        public static string Summary(ProductSettlement settlement)
        {
            var sale = settlement?.Sale;
            if (sale == null)
            {
                return string.Empty;
            }

            var text = Title(sale)
                       + $"\n• Aufsteller verkauft: {YesNo(sale.IsSold)}{(sale.PurchaseDeclined ? " (abgelehnt)" : string.Empty)}"
                       + $"\n• Vor-Ort-Einrichtung abgeschlossen: {SetupLine(sale)}"
                       + $"\n• Zahlungsmethode: {MethodText(sale.PaymentMethod)}"
                       + $"\n• Zahlungsstatus: {PaymentStatusText(sale.PaymentStatus)}{RevenueLine(settlement)}"
                       + $"\n• Weiterer Verlauf: {FollowUpLine(sale)}";
            if (sale.QuestionableClaims > 0)
            {
                text += sale.QuestionableClaims == 1
                    ? "\n• Achtung: 1 Produktaussage war falsch oder nicht durch die Produktdaten gedeckt"
                    : $"\n• Achtung: {sale.QuestionableClaims} Produktaussagen waren falsch oder nicht durch die Produktdaten gedeckt";
            }

            return text;
        }

        public static string MethodText(string method)
        {
            switch (method)
            {
                case "BankTransfer": return "Überweisung (Rechnung)";
                case "PayPal": return "PayPal";
                case "Cash": return "Barzahlung";
                case "Card": return "Kartenzahlung (Stripe-Checkout)";
                default: return "keine";
            }
        }

        public static string PaymentStatusText(string status)
        {
            switch (status)
            {
                case "Open": return "offen";
                case "Confirmed": return "bestätigt";
                case "Failed": return "fehlgeschlagen";
                default: return "nicht begonnen";
            }
        }

        public static string Amount(decimal amount, string currency) =>
            amount.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',') + (currency == "EUR" || currency.Length == 0 ? " €" : " " + currency);

        private static string PaymentLine(ProductSaleState sale) =>
            sale.PaymentMethod.Length == 0
                ? PaymentStatusText(sale.PaymentStatus)
                : $"{MethodText(sale.PaymentMethod)} – {PaymentStatusText(sale.PaymentStatus)}";

        private static string SetupLine(ProductSaleState sale)
        {
            if (!sale.IsSold)
            {
                return "Nein";
            }

            if (!sale.OnSiteSetupPossible)
            {
                return "Nein (nicht vor Ort)";
            }

            return $"{YesNo(sale.SetupCompleted)} ({sale.SetupStepsDone}/{sale.SetupStepsRequired} Schritte)";
        }

        private static string RevenueLine(ProductSettlement settlement)
        {
            if (settlement.Booked > 0)
            {
                return settlement.IsTraining
                    ? $" – {RunResultView.Euro(settlement.Booked)} erhalten, Trainingslauf: nicht gutgeschrieben"
                    : $" – {RunResultView.Euro(settlement.Booked)} gutgeschrieben";
            }

            return settlement.Outstanding > 0 ? $" – {RunResultView.Euro(settlement.Outstanding)} ausstehend, nicht gutgeschrieben" : string.Empty;
        }

        private static string FollowUpLine(ProductSaleState sale)
        {
            if (!sale.FollowUpStarted)
            {
                return "Gespräch ohne weiteres Angebot beendet";
            }

            var commitment = sale.FollowUpCommitment;
            return commitment.Length == 0
                ? $"{sale.FollowUpOffer}-Gespräch begonnen, nichts vereinbart"
                : commitment == "Agreement"
                    ? $"{sale.FollowUpOffer} verkauft"
                    : $"{sale.FollowUpOffer}-Gespräch begonnen · Vereinbarung: {RunResultView.CommitmentText(commitment)}";
        }

        private static string YesNo(bool value) => value ? "Ja" : "Nein";

        private static string Check(bool value) => value ? "ja" : "nein";
    }
}
