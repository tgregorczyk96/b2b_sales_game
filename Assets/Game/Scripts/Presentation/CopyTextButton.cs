using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// Small "Kopieren" button: copies the text of <see cref="Source"/> to <see cref="TextClipboard.Current"/>, shows
    /// "Kopiert" for a moment and then resets. Copies the whole text, so it works without a selection, without focus
    /// and independent of how the keyboard reaches the game. Never changes the copied text.
    /// </summary>
    public sealed class CopyTextButton : MonoBehaviour
    {
        public const string IdleLabel = "Kopieren";
        public const string DoneLabel = "Kopiert";

        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;
        [SerializeField] private float feedbackSeconds = 1.5f;

        private Coroutine reset;

        /// <summary>Supplies the text to copy at the moment of the click.</summary>
        public Func<string> Source { get; set; }

        /// <summary>Raised after the text was copied, e.g. to give the input field its focus back.</summary>
        public event Action Copied;

        public Button Button => button;

        public string Label => label.text;

        private void Awake()
        {
            button.onClick.AddListener(Copy);
            label.text = IdleLabel;
        }

        private void OnDisable()
        {
            // A hidden button starts fresh the next time it is shown.
            reset = null;
            label.text = IdleLabel;
        }

        public void Copy()
        {
            var text = Source?.Invoke();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            TextClipboard.Current.SetText(text);
            label.text = DoneLabel;
            if (reset != null)
            {
                StopCoroutine(reset);
            }

            if (isActiveAndEnabled)
            {
                reset = StartCoroutine(ResetLabel());
            }

            Copied?.Invoke();
        }

        private IEnumerator ResetLabel()
        {
            yield return new WaitForSecondsRealtime(feedbackSeconds);
            label.text = IdleLabel;
            reset = null;
        }
    }
}
