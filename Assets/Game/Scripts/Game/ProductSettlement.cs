using System;
using SalesSim.Application;

namespace SalesSim.Game
{
    /// <summary>
    /// Books the revenue of a product the player sold directly (e.g. the Google review stand) into the wallet — exactly what the
    /// engine reports as received (<see cref="ProductSaleState.BookedRevenue"/>), once. An open or failed payment is shown as
    /// outstanding and never booked; a training run (rerun of a known scenario) books nothing. Separate from the lead
    /// (<see cref="LeadSale"/>): selling the stand and then a website lead never books the same money twice.
    /// </summary>
    public sealed class ProductSettlement
    {
        private ProductSettlement(ProductSaleState sale, bool isTraining)
        {
            Sale = sale;
            IsTraining = isTraining;
            Booked = sale == null ? 0 : ToEuros(sale.BookedRevenue);
            Outstanding = sale == null ? 0 : ToEuros(sale.OutstandingRevenue);
        }

        /// <summary>The engine's product sale; <c>null</c> when the run had no directly sold product.</summary>
        public ProductSaleState Sale { get; }

        public bool HasProduct => Sale != null;

        public bool IsTraining { get; }

        /// <summary>Received revenue in whole euros (the wallet's unit), rounded half away from zero.</summary>
        public int Booked { get; }

        /// <summary>Sold but not paid, in whole euros. Never credited.</summary>
        public int Outstanding { get; }

        public bool IsCredited { get; private set; }

        /// <summary>What <see cref="CreditTo"/> would add: the booked revenue, unless it is a training run.</summary>
        public int Creditable => IsTraining ? 0 : Booked;

        /// <param name="finalState">The engine's final state of the run.</param>
        /// <param name="isTraining">A rerun of a known scenario: evaluated, never paid.</param>
        public static ProductSettlement Of(SalesSessionState finalState, bool isTraining)
        {
            if (finalState == null)
            {
                throw new ArgumentNullException(nameof(finalState));
            }

            return new ProductSettlement(finalState.ProductSale, isTraining);
        }

        /// <summary>Credits the received product revenue once; returns what was credited (0 for training or nothing received).</summary>
        public int CreditTo(PlayerWallet wallet)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }

            if (IsCredited)
            {
                return 0;
            }

            IsCredited = true;
            var amount = Creditable;
            if (amount > 0)
            {
                wallet.Credit(amount);
            }

            return amount;
        }

        private static int ToEuros(decimal amount) => (int)Math.Round(amount, MidpointRounding.AwayFromZero);
    }
}
