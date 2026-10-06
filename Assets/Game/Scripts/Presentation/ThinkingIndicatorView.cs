using TMPro;
using UnityEngine;

namespace SalesSim.Presentation
{
    /// <summary>Small customer-side bubble with looping dots (. → .. → ...) while a reply is pending.</summary>
    public sealed class ThinkingIndicatorView : MonoBehaviour
    {
        private static readonly string[] Frames = { ".", "..", "..." };

        [SerializeField] private TMP_Text dots;
        [SerializeField] private float frameSeconds = 0.4f;

        private float elapsed;
        private int frame;

        public bool IsVisible => gameObject.activeSelf;

        public void Show()
        {
            elapsed = 0f;
            frame = 0;
            dots.text = Frames[0];
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < frameSeconds)
            {
                return;
            }

            elapsed = 0f;
            frame = (frame + 1) % Frames.Length;
            dots.text = Frames[frame];
        }
    }
}
