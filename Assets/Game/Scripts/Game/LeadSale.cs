using System;

namespace SalesSim.Game
{
    /// <summary>The player's money. Persisted by the run loop through <see cref="IGameStateStore"/>.</summary>
    public sealed class PlayerWallet
    {
        /// <param name="balance">A saved balance to continue with; negative values count as 0.</param>
        public PlayerWallet(int balance = 0)
        {
            Balance = Math.Max(0, balance);
        }

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
    /// A lead from a training run (rerun of a known seed) is evaluated the same way but can never be sold.
    /// </summary>
    public sealed class LeadSale
    {
        /// <param name="isTraining">A rerun: the lead is shown, but there is no payout.</param>
        public LeadSale(LeadEvaluation evaluation, bool isTraining = false)
        {
            Evaluation = evaluation ?? throw new ArgumentNullException(nameof(evaluation));
            IsTraining = isTraining;
        }

        public LeadEvaluation Evaluation { get; }

        public bool IsSold { get; private set; }

        /// <summary>From a training run: never sold, no payout.</summary>
        public bool IsTraining { get; }

        public bool CanSell => !IsTraining && !IsSold && Evaluation.IsSellable;

        /// <summary>Hands the lead to the closer and credits the player's payout.</summary>
        /// <returns>The credited payout.</returns>
        /// <exception cref="InvalidOperationException">A training lead, already sold, or worthless.</exception>
        public int SellTo(PlayerWallet wallet)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }

            if (IsTraining)
            {
                throw new InvalidOperationException("A training run pays nothing; its lead cannot be sold.");
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
