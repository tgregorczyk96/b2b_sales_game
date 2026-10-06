using System.Globalization;
using SalesSim.Application;
using TMPro;
using UnityEngine;

namespace SalesSim.Presentation
{
    /// <summary>Debug readout of the engine-reported state. Displays values only; never derives or changes them.</summary>
    public sealed class SalesDebugPanelView : MonoBehaviour
    {
        [SerializeField] private TMP_Text trustText;
        [SerializeField] private TMP_Text opennessText;
        [SerializeField] private TMP_Text engagementText;
        [SerializeField] private TMP_Text patienceText;
        [SerializeField] private TMP_Text stageText;
        [SerializeField] private TMP_Text intentText;

        public void Render(SalesSessionState state)
        {
            trustText.text = $"Trust: {Format(state.Indicators.Trust)}";
            opennessText.text = $"Openness: {Format(state.Indicators.Openness)}";
            engagementText.text = $"Engagement: {Format(state.Indicators.Engagement)}";
            patienceText.text = $"Patience: {Format(state.Indicators.Patience)}";
            stageText.text = $"Stage: {OrDash(state.Stage)}";
            intentText.text = $"Intent: {OrDash(state.Intent)}";
        }

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string OrDash(string value) => string.IsNullOrEmpty(value) ? "-" : value;
    }
}
