using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// Scrollable chat history of the running conversation (presentation only, not persisted). Follows new messages
    /// only while the user is at or near the bottom; a user who scrolled up keeps their position.
    /// </summary>
    public sealed class ConversationHistoryView : MonoBehaviour
    {
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private ConversationMessageView messagePrefab;
        [SerializeField] private ThinkingIndicatorView thinkingIndicator;
        [SerializeField] private Color playerBubbleColor = new Color(0.22f, 0.36f, 0.58f, 0.95f);
        [SerializeField] private Color customerBubbleColor = new Color(0.2f, 0.22f, 0.27f, 0.95f);
        [Tooltip("Space on the opposite side of a bubble, so player and customer messages are offset.")]
        [SerializeField] private int bubbleIndent = 140;
        [Tooltip("Within this many pixels of the bottom the view keeps following new messages.")]
        [SerializeField] private float followThreshold = 60f;

        private readonly List<ConversationMessageView> messages = new List<ConversationMessageView>();
        private bool following = true;
        private float lastContentHeight;

        public IReadOnlyList<ConversationMessageView> Messages => messages;

        public bool IsThinking => thinkingIndicator.IsVisible;

        public ScrollRect ScrollRect => scrollRect;

        public void Clear()
        {
            foreach (var message in messages)
            {
                Destroy(message.gameObject);
            }

            messages.Clear();
            thinkingIndicator.Hide();
        }

        public ConversationMessageView AddPlayerMessage(string text)
        {
            return Add(ConversationSender.Player, text, playerBubbleColor);
        }

        public ConversationMessageView AddCustomerMessage(string text)
        {
            return Add(ConversationSender.Customer, text, customerBubbleColor);
        }

        public void ShowThinking()
        {
            var follow = IsNearBottom();
            thinkingIndicator.Show();
            KeepFollowing(follow);
        }

        public void HideThinking()
        {
            thinkingIndicator.Hide();
        }

        private ConversationMessageView Add(ConversationSender sender, string text, Color color)
        {
            var follow = IsNearBottom();
            var message = Instantiate(messagePrefab, content);
            message.Show(sender, text, color, bubbleIndent);
            messages.Add(message);
            if (thinkingIndicator.IsVisible)
            {
                thinkingIndicator.transform.SetAsLastSibling();
            }

            KeepFollowing(follow);
            return message;
        }

        // TMP text gets its final height only in a later layout pass, so the view keeps snapping to the bottom while the
        // content still grows. It stops following as soon as the user scrolls away from the bottom.
        private void LateUpdate()
        {
            var height = content.rect.height;
            if (following)
            {
                if (!Mathf.Approximately(height, lastContentHeight))
                {
                    SnapToBottom();
                }
                else if (!IsNearBottom())
                {
                    following = false;
                }
            }

            lastContentHeight = height;
        }

        private bool IsNearBottom()
        {
            var overflow = content.rect.height - scrollRect.viewport.rect.height;
            return overflow <= 0f || scrollRect.verticalNormalizedPosition * overflow <= followThreshold;
        }

        private void KeepFollowing(bool follow)
        {
            if (!follow)
            {
                return;
            }

            following = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            SnapToBottom();
        }

        private void SnapToBottom()
        {
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
