using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// Read-only TMP input field used as selectable chat text: the mouse selects, Ctrl+C copies (TMP's own clipboard
    /// handling), typing, pasting and deleting are blocked by <see cref="TMP_InputField.readOnly"/>.
    /// Only addition: mouse-wheel events are passed to the surrounding <see cref="ScrollRect"/>; a multi-line TMP input
    /// field otherwise swallows them when its text fits, so the chat would not scroll over a message.
    /// </summary>
    public sealed class SelectableMessageText : TMP_InputField
    {
        private ScrollRect parentScroll;

        public void SetMessage(string message)
        {
            readOnly = true;
            richText = false;
            onFocusSelectAll = false;
            SetTextWithoutNotify(message ?? string.Empty);
        }

        public override void OnScroll(PointerEventData eventData)
        {
            if (parentScroll == null && transform.parent != null)
            {
                parentScroll = transform.parent.GetComponentInParent<ScrollRect>();
            }

            if (parentScroll != null)
            {
                parentScroll.OnScroll(eventData);
            }
        }
    }
}
