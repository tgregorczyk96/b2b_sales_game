using System;
using System.Collections;
using SalesSim.Application;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// Wires the conversation UI to an <see cref="ISalesGameSession"/>.
    /// Forwards player input to the session and renders whatever state comes back — no sales rules here.
    /// </summary>
    public sealed class SalesConversationController : MonoBehaviour
    {
        [SerializeField] private ConversationHistoryView history;
        [SerializeField] private TMP_InputField playerInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private SalesDebugPanelView debugPanel;
        [SerializeField] private CharacterPlaceholderView character;
        [SerializeField] private float speakingDuration = 1.5f;

        private ISalesGameSession session;
        private Coroutine speakingRoutine;
        private bool turnInProgress;

        /// <summary>True while a player turn is waiting for the session's answer.</summary>
        public bool IsTurnInProgress => turnInProgress;

        private void Awake()
        {
            sendButton.onClick.AddListener(OnSendClicked);
            SetInputEnabled(false);
        }

        private void OnDestroy()
        {
            sendButton.onClick.RemoveListener(OnSendClicked);
        }

        public async void Initialize(ISalesGameSession salesSession, string scenarioId)
        {
            session = salesSession ?? throw new ArgumentNullException(nameof(salesSession));
            history.Clear();
            debugPanel.Render(session.CurrentState);

            try
            {
                var state = await session.StartSessionAsync(new SessionStartRequest(scenarioId), destroyCancellationToken);
                Render(state);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private async void OnSendClicked()
        {
            var message = playerInput.text.Trim();
            if (session == null || turnInProgress || message.Length == 0)
            {
                return;
            }

            // Show the player's line and the waiting indicator right away, before the session answers.
            turnInProgress = true;
            SetInputEnabled(false);
            playerInput.text = string.Empty;
            var playerMessage = history.AddPlayerMessage(message);
            history.ShowThinking();

            var conversationOver = false;
            try
            {
                var state = await session.SendPlayerTurnAsync(new PlayerTurn(message), destroyCancellationToken);
                history.HideThinking();
                Render(state);
                conversationOver = state.IsConversationOver;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // The session did not take the turn: no customer line is invented; the text goes back for a retry.
                Debug.LogException(exception, this);
                playerMessage.MarkNotDelivered();
                if (playerInput.text.Length == 0)
                {
                    playerInput.text = message;
                }
            }
            finally
            {
                if (this != null)
                {
                    history.HideThinking();
                    turnInProgress = false;
                }
            }

            SetInputEnabled(!conversationOver);
            if (!conversationOver)
            {
                playerInput.ActivateInputField();
            }
        }

        private void Render(SalesSessionState state)
        {
            if (state.CustomerMessage.Length > 0)
            {
                history.AddCustomerMessage(state.CustomerMessage);
            }

            debugPanel.Render(state);
            ShowSpeaking();
            SetInputEnabled(!state.IsConversationOver);
        }

        private void ShowSpeaking()
        {
            if (speakingRoutine != null)
            {
                StopCoroutine(speakingRoutine);
            }
            speakingRoutine = StartCoroutine(SpeakingRoutine());
        }

        private IEnumerator SpeakingRoutine()
        {
            character.SetState(CharacterVisualState.Speaking);
            yield return new WaitForSeconds(speakingDuration);
            character.SetState(CharacterVisualState.Idle);
            speakingRoutine = null;
        }

        private void SetInputEnabled(bool enabled)
        {
            playerInput.interactable = enabled;
            sendButton.interactable = enabled;
        }
    }
}
