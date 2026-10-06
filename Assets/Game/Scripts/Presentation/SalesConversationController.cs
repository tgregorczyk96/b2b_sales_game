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
        [SerializeField] private TMP_Text customerText;
        [SerializeField] private TMP_InputField playerInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private SalesDebugPanelView debugPanel;
        [SerializeField] private CharacterPlaceholderView character;
        [SerializeField] private float speakingDuration = 1.5f;

        private ISalesGameSession session;
        private Coroutine speakingRoutine;

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
            if (session == null || message.Length == 0)
            {
                return;
            }

            SetInputEnabled(false);
            try
            {
                var state = await session.SendPlayerTurnAsync(new PlayerTurn(message), destroyCancellationToken);
                playerInput.text = string.Empty;
                Render(state);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                SetInputEnabled(true);
            }
        }

        private void Render(SalesSessionState state)
        {
            customerText.text = state.CustomerMessage;
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
