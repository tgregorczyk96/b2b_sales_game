using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>Who wrote a chat message. Purely visual: decides alignment, colour and sender label.</summary>
    public enum ConversationSender
    {
        Player,
        Customer
    }

    /// <summary>One chat bubble: sender label plus selectable, read-only message text.</summary>
    public sealed class ConversationMessageView : MonoBehaviour
    {
        [SerializeField] private HorizontalLayoutGroup row;
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text senderLabel;
        [SerializeField] private SelectableMessageText body;

        public ConversationSender Sender { get; private set; }

        public string Text => body.text;

        public string SenderLabel => senderLabel.text;

        public bool IsReadOnly => body.readOnly;

        public void Show(ConversationSender sender, string message, Color bubbleColor, int indent)
        {
            Sender = sender;
            senderLabel.text = sender == ConversationSender.Player ? "Du" : "Kunde";
            senderLabel.alignment = sender == ConversationSender.Player ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            background.color = bubbleColor;
            row.padding = sender == ConversationSender.Player
                ? new RectOffset(indent, 0, 0, 0)
                : new RectOffset(0, indent, 0, 0);
            body.SetMessage(message);
        }

        /// <summary>The engine did not take this message (e.g. the request failed); it stays visible, marked as such.</summary>
        public void MarkNotDelivered()
        {
            senderLabel.text = "Du · nicht zugestellt – bitte erneut senden";
            background.color = new Color(background.color.r, background.color.g, background.color.b, background.color.a * 0.5f);
        }
    }
}
