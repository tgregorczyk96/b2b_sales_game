using System;
using System.Collections;
using SalesSim.Application;
using SalesSim.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// Wires the conversation UI to an <see cref="ISalesGameSession"/>.
    /// Forwards player input to the session and renders whatever state comes back — no sales rules here.
    /// Character states: Speaking briefly while the player's line is delivered, Thinking while the session works, then the
    /// mood read from the engine's indicator change (<see cref="CustomerReaction"/>).
    /// Live guidance (Hinweis / Mehr Hinweis / Beispiel) is requested from the session between turns and shown in the
    /// <see cref="GuidancePanelView"/>, never in the chat; successful requests are counted per run in <see cref="GuidanceUsage"/>.
    /// </summary>
    public sealed class SalesConversationController : MonoBehaviour
    {
        [SerializeField] private ConversationHistoryView history;
        [SerializeField] private TMP_InputField playerInput;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button endButton;
        [SerializeField] private SalesDebugPanelView debugPanel;
        [SerializeField] private CharacterView character;
        [SerializeField] private GuidancePanelView guidance;
        [Tooltip("How long the character shows Speaking after the player sends, before it switches to Thinking.")]
        [SerializeField] private float speakingSeconds = 0.6f;

        private ISalesGameSession session;
        private Coroutine thinkingRoutine;
        private bool turnInProgress;
        private bool conversationOver;
        private bool guidanceInProgress;
        private bool inputEnabled;
        private int run;
        private GuidanceUsage guidanceUsage = new GuidanceUsage();

        /// <summary>Raised once per run with the engine's final state when the conversation is over.</summary>
        public event Action<SalesSessionState> ConversationEnded;

        /// <summary>True while a player turn is waiting for the session's answer.</summary>
        public bool IsTurnInProgress => turnInProgress;

        /// <summary>True while a guidance request (typically the AI example) is waiting for the session's answer.</summary>
        public bool IsGuidanceInProgress => guidanceInProgress;

        public bool IsConversationOver => conversationOver;

        /// <summary>Guidance used in the current run; a fresh counter per run.</summary>
        public GuidanceUsage GuidanceUsage => guidanceUsage;

        private void Awake()
        {
            sendButton.onClick.AddListener(OnSendClicked);
            endButton.onClick.AddListener(OnEndClicked);
            guidance.Requested += OnGuidanceRequested;
            SetInputEnabled(false);
        }

        private void OnDestroy()
        {
            sendButton.onClick.RemoveListener(OnSendClicked);
            endButton.onClick.RemoveListener(OnEndClicked);
            guidance.Requested -= OnGuidanceRequested;
        }

        /// <summary>Starts a fresh conversation (one run) on <paramref name="salesSession"/>.</summary>
        /// <param name="scenario">The opaque scenario id, see <see cref="IScenarioSource.ScenarioIdFor"/>.</param>
        /// <param name="playerProfile">The player's own name and company for the engine; <c>null</c> without.</param>
        /// <param name="difficulty">One of the engine's difficulty names; <c>null</c> uses the session's default.</param>
        public async void Initialize(
            ISalesGameSession salesSession, string scenario, PlayerProfile playerProfile = null, string difficulty = null)
        {
            session = salesSession ?? throw new ArgumentNullException(nameof(salesSession));
            conversationOver = false;
            turnInProgress = false;
            guidanceInProgress = false;
            run++;
            guidanceUsage = new GuidanceUsage();
            guidance.Clear();
            history.Clear();
            playerInput.text = string.Empty;
            character.SetState(CharacterVisualState.Idle);
            SetInputEnabled(false);
            debugPanel.Render(session.CurrentState);

            try
            {
                var request = new SessionStartRequest(scenario, playerProfile, difficulty);
                var state = await session.StartSessionAsync(request, destroyCancellationToken);
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
            if (session == null || turnInProgress || guidanceInProgress || conversationOver || message.Length == 0)
            {
                return;
            }

            // Show the player's line, the character's Speaking → Thinking and the waiting bubble right away.
            // The guidance was for this turn; it is used up now.
            turnInProgress = true;
            SetInputEnabled(false);
            guidance.Clear();
            playerInput.text = string.Empty;
            var playerMessage = history.AddPlayerMessage(message);
            history.ShowThinking();
            StartThinking();
            var before = session.CurrentState.Indicators;

            try
            {
                var state = await session.SendPlayerTurnAsync(new PlayerTurn(message), destroyCancellationToken);
                StopThinking();
                history.HideThinking();
                Render(state);
                character.SetState(ToVisual(CustomerReaction.MoodAfterTurn(before, state.Indicators)));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // The session did not take the turn: no customer line is invented; the text goes back for a retry.
                Debug.LogException(exception, this);
                StopThinking();
                character.SetState(CharacterVisualState.Idle);
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
            else
            {
                ConversationEnded?.Invoke(session.CurrentState);
            }
        }

        private async void OnEndClicked()
        {
            if (session == null || turnInProgress || guidanceInProgress || conversationOver)
            {
                return;
            }

            SetInputEnabled(false);
            try
            {
                var state = await session.EndSessionAsync(destroyCancellationToken);
                Render(state);
                ConversationEnded?.Invoke(state);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                SetInputEnabled(!conversationOver);
            }
        }

        /// <summary>
        /// One guidance request at a time, only between turns. While it runs (the AI example can take seconds) the player
        /// may keep typing, but cannot send or end, so the guidance always fits the conversation it was asked for.
        /// Levels the engine does not allow right now (e.g. by difficulty) are never requested and never counted.
        /// </summary>
        private async void OnGuidanceRequested(GuidanceLevel level)
        {
            if (session == null || !inputEnabled || turnInProgress || guidanceInProgress || conversationOver
                || session.GetGuidanceAvailability(level) != GuidanceAvailability.Available)
            {
                return;
            }

            var requestRun = run;
            guidanceInProgress = true;
            SetInputEnabled(inputEnabled);
            if (level == GuidanceLevel.Example)
            {
                guidance.ShowLoading(level, "Beispiel wird formuliert");
            }

            try
            {
                var result = await session.RequestGuidanceAsync(level, destroyCancellationToken);
                if (requestRun != run)
                {
                    return;
                }

                guidance.Show(result);
                guidanceUsage.Record(level);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (GuidanceUnavailableException exception)
            {
                // Shown in the guidance panel only: nothing is counted and the conversation is unchanged.
                if (exception.Reason == GuidanceUnavailableReason.GenerationFailed)
                {
                    Debug.LogWarning($"Guidance {level} failed: {(exception.InnerException ?? exception).Message}", this);
                }

                if (requestRun == run)
                {
                    guidance.ShowError(level, UnavailableText(level, exception.Reason));
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                if (requestRun == run)
                {
                    guidance.ShowError(level, "Hilfe konnte nicht geladen werden. Versuch es erneut oder schreib einfach weiter.");
                }
            }
            finally
            {
                if (this != null && requestRun == run)
                {
                    guidanceInProgress = false;
                    SetInputEnabled(inputEnabled);
                }
            }

            if (requestRun == run && inputEnabled)
            {
                // Straight back to writing; the draft is kept (PlayerInput does not select all on focus).
                playerInput.ActivateInputField();
            }
        }

        private static string UnavailableText(GuidanceLevel level, GuidanceUnavailableReason reason)
        {
            switch (reason)
            {
                case GuidanceUnavailableReason.RequiresAi:
                    return "Beispiele brauchen den KI-Kunden (SALESSIM_ANTHROPIC_API_KEY). Hinweis und Mehr Hinweis gehen auch ohne.";
                case GuidanceUnavailableReason.NotAllowedByDifficulty:
                    return "Auf dieser Schwierigkeitsstufe nicht verfügbar.";
                case GuidanceUnavailableReason.ConversationOver:
                    return "Das Gespräch ist beendet.";
                case GuidanceUnavailableReason.NotConnected:
                    return "Keine Sales Engine verbunden.";
                case GuidanceUnavailableReason.Busy:
                    return "Es läuft bereits eine Anfrage.";
                default:
                    return level == GuidanceLevel.Example
                        ? "Beispiel konnte nicht erstellt werden. Versuch es erneut oder schreib einfach weiter."
                        : "Hilfe konnte nicht geladen werden. Versuch es erneut oder schreib einfach weiter.";
            }
        }

        private void Render(SalesSessionState state)
        {
            if (state.CustomerMessage.Length > 0)
            {
                history.AddCustomerMessage(state.CustomerMessage);
            }

            conversationOver = state.IsConversationOver;
            debugPanel.Render(state);
            SetInputEnabled(!conversationOver);
        }

        private void StartThinking()
        {
            StopThinking();
            thinkingRoutine = StartCoroutine(SpeakThenThink());
        }

        private void StopThinking()
        {
            if (thinkingRoutine != null)
            {
                StopCoroutine(thinkingRoutine);
                thinkingRoutine = null;
            }
        }

        private IEnumerator SpeakThenThink()
        {
            character.SetState(CharacterVisualState.Speaking);
            yield return new WaitForSecondsRealtime(speakingSeconds);
            character.SetState(CharacterVisualState.Thinking);
            thinkingRoutine = null;
        }

        private static CharacterVisualState ToVisual(CustomerMood mood)
        {
            switch (mood)
            {
                case CustomerMood.Positive: return CharacterVisualState.Positive;
                case CustomerMood.VeryPositive: return CharacterVisualState.VeryPositive;
                case CustomerMood.Negative: return CharacterVisualState.Negative;
                case CustomerMood.VeryNegative: return CharacterVisualState.VeryNegative;
                default: return CharacterVisualState.Idle;
            }
        }

        private void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
            playerInput.interactable = enabled;
            sendButton.interactable = enabled && !guidanceInProgress;
            endButton.interactable = enabled && !guidanceInProgress;
            foreach (GuidanceLevel level in Enum.GetValues(typeof(GuidanceLevel)))
            {
                var availability = session == null ? GuidanceAvailability.NotStarted : session.GetGuidanceAvailability(level);
                guidance.SetAvailability(level, availability == GuidanceAvailability.Available, UnavailableNote(availability));
            }

            guidance.SetInteractable(enabled && !guidanceInProgress);
        }

        /// <summary>Short note under a button the engine does not allow; empty where a plain disabled button is enough.</summary>
        private static string UnavailableNote(GuidanceAvailability availability)
        {
            switch (availability)
            {
                case GuidanceAvailability.NotAllowedByDifficulty: return "nicht auf dieser Stufe";
                case GuidanceAvailability.RequiresAi: return "nur mit KI";
                default: return string.Empty;
            }
        }
    }
}
