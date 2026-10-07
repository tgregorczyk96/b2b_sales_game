using SalesSim.Application;
using SalesSim.Game;
using TMPro;
using UnityEngine;

namespace SalesSim.Presentation
{
    /// <summary>
    /// The first game loop: conversation → run ends → lead is priced → sold to the closer → money → next run.
    /// Pricing and selling live in <see cref="SalesSim.Game"/>; this only connects them to the UI. The wallet lives as long
    /// as the scene (no persistence).
    /// </summary>
    public sealed class SalesRunLoop : MonoBehaviour
    {
        [SerializeField] private SalesConversationController conversation;
        [SerializeField] private RunResultView resultView;
        [SerializeField] private TMP_Text balanceLabel;
        [SerializeField] private EconomySettings economy = new EconomySettings();

        private readonly PlayerWallet wallet = new PlayerWallet();
        private LeadSale currentLead;

        public PlayerWallet Wallet => wallet;

        public LeadSale CurrentLead => currentLead;

        private void Awake()
        {
            conversation.ConversationEnded += OnConversationEnded;
            resultView.SellRequested += OnSellRequested;
            resultView.NextRunRequested += OnNextRunRequested;
            UpdateBalance();
        }

        private void OnDestroy()
        {
            conversation.ConversationEnded -= OnConversationEnded;
            resultView.SellRequested -= OnSellRequested;
            resultView.NextRunRequested -= OnNextRunRequested;
        }

        private void OnConversationEnded(SalesSessionState finalState)
        {
            currentLead = new LeadSale(LeadEvaluator.Evaluate(finalState, economy));
            resultView.Show(currentLead, economy.closerShare, wallet.Balance, conversation.GuidanceUsage);
        }

        private void OnSellRequested()
        {
            if (currentLead == null || !currentLead.CanSell)
            {
                return;
            }

            currentLead.SellTo(wallet);
            resultView.Refresh(currentLead, wallet.Balance);
            UpdateBalance();
        }

        private void OnNextRunRequested()
        {
            currentLead = null;
            resultView.Hide();
            conversation.StartNextRun();
        }

        private void UpdateBalance()
        {
            balanceLabel.text = $"Guthaben: {RunResultView.Euro(wallet.Balance)}";
        }
    }
}
