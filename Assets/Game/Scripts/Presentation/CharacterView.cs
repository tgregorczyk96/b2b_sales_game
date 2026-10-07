using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// The on-screen customer. Shows one sprite per <see cref="CharacterVisualState"/>; states without a sprite fall back to
    /// the Idle sprite, and without any sprites to the coloured placeholder (tint + mouth).
    /// </summary>
    public sealed class CharacterView : MonoBehaviour
    {
        [Serializable]
        public struct StateSprite
        {
            public CharacterVisualState state;
            public Sprite sprite;
        }

        [SerializeField] private Image body;
        [SerializeField] private GameObject mouth;
        [SerializeField] private TMP_Text stateLabel;
        [SerializeField] private StateSprite[] sprites = Array.Empty<StateSprite>();

        [Header("Placeholder colours (used only without sprites)")]
        [SerializeField] private Color idleColor = new Color(0.55f, 0.6f, 0.7f);
        [SerializeField] private Color speakingColor = new Color(0.45f, 0.65f, 0.9f);
        [SerializeField] private Color thinkingColor = new Color(0.6f, 0.55f, 0.8f);
        [SerializeField] private Color positiveColor = new Color(0.4f, 0.75f, 0.45f);
        [SerializeField] private Color negativeColor = new Color(0.85f, 0.4f, 0.4f);

        public CharacterVisualState State { get; private set; }

        public bool HasSprites => sprites != null && sprites.Length > 0;

        private void Awake()
        {
            SetState(CharacterVisualState.Idle);
        }

        public void SetState(CharacterVisualState state)
        {
            State = state;
            stateLabel.text = state.ToString();

            var sprite = SpriteFor(state) ?? SpriteFor(CharacterVisualState.Idle);
            if (sprite != null)
            {
                body.sprite = sprite;
                body.color = Color.white;
                body.preserveAspect = true;
                mouth.SetActive(false);
                return;
            }

            body.sprite = null;
            body.color = ColorFor(state);
            mouth.SetActive(state == CharacterVisualState.Speaking);
        }

        private Sprite SpriteFor(CharacterVisualState state)
        {
            foreach (var entry in sprites)
            {
                if (entry.state == state && entry.sprite != null)
                {
                    return entry.sprite;
                }
            }

            return null;
        }

        private Color ColorFor(CharacterVisualState state)
        {
            switch (state)
            {
                case CharacterVisualState.Speaking: return speakingColor;
                case CharacterVisualState.Thinking: return thinkingColor;
                case CharacterVisualState.Positive:
                case CharacterVisualState.VeryPositive: return positiveColor;
                case CharacterVisualState.Negative:
                case CharacterVisualState.VeryNegative: return negativeColor;
                default: return idleColor;
            }
        }
    }
}
