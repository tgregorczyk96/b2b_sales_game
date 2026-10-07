using System;
using System.Globalization;
using SalesSim.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// Overlay after a finished run: conversation result, lead quality and value, closer share, payout, and how much live
    /// guidance was used (shown only; it does not change value or payout).
    /// </summary>
    public sealed class RunResultView : MonoBehaviour
    {
        [SerializeField] private GameObject overlay;
        [SerializeField] private TMP_Text outcomeText;
        [SerializeField] private TMP_Text qualityText;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private TMP_Text closerText;
        [SerializeField] private TMP_Text payoutText;
        [SerializeField] private TMP_Text balanceText;
        [SerializeField] private TMP_Text guidanceText;
        [SerializeField] private Button sellButton;
        [SerializeField] private Button nextRunButton;

        public event Action SellRequested;
        public event Action NextRunRequested;

        public bool IsVisible => overlay.activeSelf;

        public bool CanSell => sellButton.interactable;

        public Button SellButton => sellButton;

        public Button NextRunButton => nextRunButton;

        private void Awake()
        {
            sellButton.onClick.AddListener(() => SellRequested?.Invoke());
            nextRunButton.onClick.AddListener(() => NextRunRequested?.Invoke());
            overlay.SetActive(false);
        }

        public void Show(LeadSale sale, float closerShare, int balance, GuidanceUsage guidance)
        {
            var lead = sale.Evaluation;
            outcomeText.text = $"Gesprächsergebnis: {EndReasonText(lead.EndReason)} · Vereinbarung: {CommitmentText(lead.Commitment)}";
            qualityText.text = $"Lead-Qualität: {QualityText(lead.Quality)} (Score {lead.Score.ToString("0", CultureInfo.InvariantCulture)})";
            valueText.text = $"Lead-Wert: {Euro(lead.Value)}";
            closerText.text = $"Closer-Anteil ({(closerShare * 100f).ToString("0", CultureInfo.InvariantCulture)} %): −{Euro(lead.CloserCut)}";
            guidanceText.text = GuidanceSummary(guidance);
            overlay.SetActive(true);
            Refresh(sale, balance);
        }

        public void Refresh(LeadSale sale, int balance)
        {
            payoutText.text = sale.IsSold
                ? $"Verkauft – dein Erlös: {Euro(sale.Evaluation.Payout)}"
                : $"Dein Erlös bei Verkauf: {Euro(sale.Evaluation.Payout)}";
            balanceText.text = $"Guthaben: {Euro(balance)}";
            sellButton.interactable = sale.CanSell;
            sellButton.GetComponentInChildren<TMP_Text>().text = sale.IsSold
                ? "Verkauft"
                : sale.Evaluation.IsSellable ? "Lead an Closer verkaufen" : "Kein verkaufbarer Lead";
        }

        public void Hide()
        {
            overlay.SetActive(false);
        }

        public static string Euro(int amount) => amount.ToString("N0", CultureInfo.GetCultureInfo("de-DE")) + " €";

        /// <summary>The "Guidance genutzt" block of the result screen.</summary>
        public static string GuidanceSummary(GuidanceUsage usage)
        {
            var summary = "Guidance genutzt\n"
                          + $"• Hinweise: {usage.HintCount}\n"
                          + $"• Mehr Hinweise: {usage.HintMoreCount}\n"
                          + $"• Beispiele: {usage.ExampleCount}";
            return usage.IsUnassisted ? summary + "\nRun ohne Hilfe" : summary;
        }

        private static string QualityText(LeadQuality quality)
        {
            switch (quality)
            {
                case LeadQuality.Cold: return "Kalt";
                case LeadQuality.Warm: return "Warm";
                case LeadQuality.Hot: return "Heiß";
                case LeadQuality.Qualified: return "Qualifiziert";
                default: return "Kein Lead";
            }
        }

        private static string EndReasonText(string endReason)
        {
            switch (endReason)
            {
                case "Completed": return "abgeschlossen";
                case "Interrupted": return "unterbrochen";
                case "Rejected": return "abgelehnt";
                case "NoContactRequested": return "kein weiterer Kontakt erwünscht";
                default: return "beendet";
            }
        }

        private static string CommitmentText(string commitment)
        {
            switch (commitment)
            {
                case "Agreement": return "Vereinbarung";
                case "Appointment": return "Termin";
                case "FollowUp": return "Folgekontakt";
                case "Information": return "Infomaterial";
                case "NoFurtherAction": return "kein weiterer Schritt";
                default: return "keine";
            }
        }
    }
}
