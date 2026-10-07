using System;

namespace SalesSim.Game
{
    /// <summary>The player's money within the running game session (not persisted).</summary>
    public sealed class PlayerWallet
    {
        public int Balance { get; private set; }

        public void Credit(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Only non-negative amounts can be credited.");
            }

            Balance += amount;
        }
    }

    /// <summary>
    /// One lead from a finished run that can be handed to the closer exactly once. The closer is a fixed mechanism
    /// (keeps its share, see <see cref="EconomySettings.closerShare"/>), not an NPC.
    /// </summary>
    public sealed class LeadSale
    {
        public LeadSale(LeadEvaluation evaluation)
        {
            Evaluation = evaluation ?? throw new ArgumentNullException(nameof(evaluation));
        }

        public LeadEvaluation Evaluation { get; }

        public bool IsSold { get; private set; }

        public bool CanSell => !IsSold && Evaluation.IsSellable;

        /// <summary>Hands the lead to the closer and credits the player's payout.</summary>
        /// <returns>The credited payout.</returns>
        /// <exception cref="InvalidOperationException">Already sold, or worthless.</exception>
        public int SellTo(PlayerWallet wallet)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }

            if (IsSold)
            {
                throw new InvalidOperationException("This lead has already been sold.");
            }

            if (!Evaluation.IsSellable)
            {
                throw new InvalidOperationException("This lead has no value and cannot be sold.");
            }

            IsSold = true;
            wallet.Credit(Evaluation.Payout);
            return Evaluation.Payout;
        }
    }
}
