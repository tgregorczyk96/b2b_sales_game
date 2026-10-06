using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// Placeholder for the custom 2D character. Shows the current visual state via tint and label.
    /// The mouth object is the hook for a later simple mouth animation while speaking.
    /// </summary>
    public sealed class CharacterPlaceholderView : MonoBehaviour
    {
        [SerializeField] private Image body;
        [SerializeField] private GameObject mouth;
        [SerializeField] private TMP_Text stateLabel;

        [SerializeField] private Color idleColor = new Color(0.55f, 0.6f, 0.7f);
        [SerializeField] private Color speakingColor = new Color(0.45f, 0.65f, 0.9f);
        [SerializeField] private Color successColor = new Color(0.4f, 0.75f, 0.45f);
        [SerializeField] private Color failureColor = new Color(0.85f, 0.4f, 0.4f);

        public CharacterVisualState State { get; private set; }

        private void Awake()
        {
            SetState(CharacterVisualState.Idle);
        }

        public void SetState(CharacterVisualState state)
        {
            State = state;
            body.color = ColorFor(state);
            mouth.SetActive(state == CharacterVisualState.Speaking);
            stateLabel.text = state.ToString();
        }

        private Color ColorFor(CharacterVisualState state)
        {
            switch (state)
            {
                case CharacterVisualState.Speaking: return speakingColor;
                case CharacterVisualState.SuccessFeedback: return successColor;
                case CharacterVisualState.FailureFeedback: return failureColor;
                default: return idleColor;
            }
        }
    }
}
