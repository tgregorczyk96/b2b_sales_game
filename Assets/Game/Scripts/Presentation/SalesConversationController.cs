using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using SalesSim.Application;
using SalesSim.Game;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
    /// Push-to-talk (<see cref="ISpeechInput"/>): Enter on an empty input or the microphone button starts recording, Enter
    /// or the button again sends it, Escape discards it. The transcript is shown in the input and then sent exactly like a
    /// typed line; a discarded, empty or failed recording never becomes a turn.
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
        [SerializeField] private SpeechInputView speech;
        [Tooltip("How long the character shows Speaking after the player sends, before it switches to Thinking.")]
        [SerializeField] private float speakingSeconds = 0.6f;

        private enum SpeechPhase
        {
            Idle,
            Starting,
            Recording,
            Processing,
        }

        private ISalesGameSession session;
        private ISpeechInput speechInput = new UnavailableSpeechInput("Spracheingabe nicht verbunden.");
        private SpeechPhase speechPhase;
        private int speechAttempt;
        private ISpeechRecording recording;
        private CancellationTokenSource speechCancellation;
        private TaskCompletionSource<bool> speechStopRequested;
        private string draftBeforeSpeech = string.Empty;
        private int lastSpeechToggleFrame = -1;
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

        /// <summary>True from the start of a recording until its transcript is sent or it is discarded or fails.</summary>
        public bool IsSpeechInProgress => speechPhase != SpeechPhase.Idle;

        /// <summary>True while the microphone records (Enter sends, Escape discards).</summary>
        public bool IsRecording => speechPhase == SpeechPhase.Recording;

        private void Awake()
        {
            sendButton.onClick.AddListener(OnSendClicked);
            endButton.onClick.AddListener(OnEndClicked);
            guidance.Requested += OnGuidanceRequested;
            guidance.Copied += ReturnFocusToInput;
            history.MessageCopied += ReturnFocusToInput;
            speech.Clicked += OnMicrophoneClicked;
            ShowSpeechReady();
            SetInputEnabled(false);
        }

        private void OnDestroy()
        {
            sendButton.onClick.RemoveListener(OnSendClicked);
            endButton.onClick.RemoveListener(OnEndClicked);
            guidance.Requested -= OnGuidanceRequested;
            guidance.Copied -= ReturnFocusToInput;
            history.MessageCopied -= ReturnFocusToInput;
            speech.Clicked -= OnMicrophoneClicked;
            // Stops the microphone and drops the recording; the UI is going away, so nothing is shown.
            speechAttempt++;
            speechCancellation?.Cancel();
            EndSpeech();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                PressTalkKey();
            }
            else if (keyboard.escapeKey.wasPressedThisFrame)
            {
                PressCancelKey();
            }
        }

        /// <summary>The speech input to record with; replaces the "not connected" default. Called by the composition root.</summary>
        public void SetSpeechInput(ISpeechInput input)
        {
            DiscardSpeech(null);
            speechInput = input ?? new UnavailableSpeechInput("Spracheingabe nicht verbunden.");
            ShowSpeechReady();
            SetInputEnabled(inputEnabled);
        }

        /// <summary>
        /// Enter: starts recording when the input is empty (a typed draft is never taken over by Enter), sends the running
        /// recording, and is ignored otherwise.
        /// </summary>
        public void PressTalkKey()
        {
            if (speechPhase == SpeechPhase.Recording)
            {
                ToggleSpeech();
            }
            else if (speechPhase == SpeechPhase.Idle && playerInput.text.Trim().Length == 0)
            {
                ToggleSpeech();
            }
        }

        /// <summary>Escape: discards a running recording or transcription; nothing of it is sent.</summary>
        public void PressCancelKey()
        {
            if (speechPhase != SpeechPhase.Idle)
            {
                DiscardSpeech("Aufnahme abgebrochen – nichts gesendet.");
            }
        }

        private void OnMicrophoneClicked()
        {
            // The Input System UI module sends Enter as "submit" to the selected button; without this, the next Enter
            // would press the microphone button a second time on top of the Enter key itself.
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            ToggleSpeech();
        }

        /// <summary>One toggle per frame, whether it came from the key, the button or both.</summary>
        private void ToggleSpeech()
        {
            if (lastSpeechToggleFrame == Time.frameCount)
            {
                return;
            }

            lastSpeechToggleFrame = Time.frameCount;
            if (speechPhase == SpeechPhase.Recording)
            {
                speechStopRequested?.TrySetResult(true);
            }
            else if (speechPhase == SpeechPhase.Idle && CanStartSpeech())
            {
                RecordAndSend();
            }
        }

        private bool CanStartSpeech()
        {
            return speechInput.IsAvailable && session != null && inputEnabled && !turnInProgress && !guidanceInProgress && !conversationOver;
        }

        /// <summary>
        /// One push-to-talk turn: record until the player stops, transcribe, show the transcript in the input and send it
        /// like a typed line. Every await re-checks <see cref="speechAttempt"/>: a discard (Escape, end of the conversation,
        /// new run) bumps it, and nothing of the old attempt may reach the conversation after that.
        /// </summary>
        private async void RecordAndSend()
        {
            var attempt = ++speechAttempt;
            speechPhase = SpeechPhase.Starting;
            draftBeforeSpeech = playerInput.text;
            speechCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            speechStopRequested = new TaskCompletionSource<bool>();
            speech.Show(SpeechInputViewState.Starting, "Mikrofon wird gestartet …");
            SetInputEnabled(inputEnabled);

            var partials = new Progress<string>(text =>
            {
                if (attempt == speechAttempt && speechPhase == SpeechPhase.Recording)
                {
                    playerInput.text = text;
                }
            });

            string transcript;
            try
            {
                var started = await speechInput.StartRecordingAsync(partials, speechCancellation.Token);
                if (attempt != speechAttempt)
                {
                    started.Dispose();
                    return;
                }

                recording = started;
                speechPhase = SpeechPhase.Recording;
                playerInput.text = string.Empty;
                speech.Show(SpeechInputViewState.Recording, "Aufnahme läuft – Enter: senden · Esc: abbrechen");
                SetInputEnabled(inputEnabled);

                var ended = await Task.WhenAny(speechStopRequested.Task, started.Interrupted);
                if (attempt != speechAttempt)
                {
                    return;
                }

                if (ended == started.Interrupted)
                {
                    await started.Interrupted; // throws the reason
                }

                speechPhase = SpeechPhase.Processing;
                speech.Show(SpeechInputViewState.Processing, "Sprache wird erkannt …");
                SetInputEnabled(inputEnabled);
                transcript = await started.FinishAsync(speechCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                if (attempt == speechAttempt)
                {
                    FailSpeech(exception);
                }

                return;
            }

            if (attempt != speechAttempt)
            {
                return;
            }

            EndSpeech();
            if (transcript.Length == 0)
            {
                playerInput.text = draftBeforeSpeech;
                speech.Show(SpeechInputViewState.Error, "Keine Sprache erkannt – nichts gesendet. Nochmal mit Enter oder tippen.");
                SetInputEnabled(inputEnabled);
                return;
            }

            // The transcript is the player's line: visible in the input, then the ordinary send path.
            playerInput.text = transcript;
            ShowSpeechReady();
            SetInputEnabled(inputEnabled);
            SendTurn(transcript);
        }

        private void FailSpeech(Exception exception)
        {
            var error = exception as SpeechInputException;
            if (error == null)
            {
                Debug.LogException(exception, this);
            }
            else
            {
                Debug.LogWarning($"Speech input failed ({error.Error}): {error.Message}", this);
            }

            EndSpeech();
            playerInput.text = draftBeforeSpeech;
            speech.Show(SpeechInputViewState.Error, SpeechErrorText(error?.Error ?? SpeechInputError.ServiceError));
            SetInputEnabled(inputEnabled);
        }

        /// <summary>Stops any recording or transcription without a turn; <paramref name="status"/> is shown if given.</summary>
        private void DiscardSpeech(string status)
        {
            if (speechPhase == SpeechPhase.Idle)
            {
                return;
            }

            speechAttempt++;
            speechCancellation?.Cancel();
            EndSpeech();
            playerInput.text = draftBeforeSpeech;
            if (status == null)
            {
                ShowSpeechReady();
            }
            else
            {
                speech.Show(SpeechInputViewState.Ready, status);
            }

            SetInputEnabled(inputEnabled);
        }

        private void EndSpeech()
        {
            speechPhase = SpeechPhase.Idle;
            speechStopRequested?.TrySetCanceled(); // lets a discarded attempt's wait return instead of hanging
            recording?.Dispose();
            recording = null;
            speechCancellation?.Dispose();
            speechCancellation = null;
            speechStopRequested = null;
        }

        private void ShowSpeechReady()
        {
            if (speechInput.IsAvailable)
            {
                speech.Show(SpeechInputViewState.Ready, "Enter (leeres Feld) oder „Sprechen“: Spracheingabe");
            }
            else
            {
                speech.Show(SpeechInputViewState.Unavailable, speechInput.UnavailableReason);
            }
        }

        private static string SpeechErrorText(SpeechInputError error)
        {
            switch (error)
            {
                case SpeechInputError.NotConfigured:
                    return "Spracheingabe ist nicht eingerichtet (SALESSIM_ELEVENLABS_API_KEY). Tippen geht weiterhin.";
                case SpeechInputError.NoMicrophone:
                    return "Kein Mikrofon gefunden – nichts gesendet. Schließ eins an oder tipp deine Antwort.";
                case SpeechInputError.MicrophoneDenied:
                    return "Kein Zugriff aufs Mikrofon – nichts gesendet. Erlaube ihn in den Windows-Datenschutzeinstellungen.";
                case SpeechInputError.MicrophoneFailed:
                    return "Das Mikrofon konnte nicht aufnehmen – nichts gesendet. Versuch es erneut oder tipp.";
                case SpeechInputError.Authentication:
                    return "ElevenLabs hat den API-Key abgelehnt (SALESSIM_ELEVENLABS_API_KEY) – nichts gesendet.";
                case SpeechInputError.Unreachable:
                    return "ElevenLabs ist nicht erreichbar – nichts gesendet. Prüf die Internetverbindung oder tipp.";
                case SpeechInputError.Timeout:
                    return "ElevenLabs hat nicht rechtzeitig geantwortet – nichts gesendet. Versuch es erneut.";
                case SpeechInputError.Busy:
                    return "ElevenLabs ist gerade ausgelastet – nichts gesendet. Versuch es gleich noch einmal.";
                case SpeechInputError.QuotaExceeded:
                    return "Das ElevenLabs-Kontingent ist aufgebraucht – nichts gesendet. Tippen geht weiterhin.";
                default:
                    return "Spracherkennung fehlgeschlagen – nichts gesendet. Versuch es erneut oder tipp.";
            }
        }

        /// <summary>Starts a fresh conversation (one run) on <paramref name="salesSession"/>.</summary>
        /// <param name="scenario">The opaque scenario id, see <see cref="IScenarioSource.ScenarioIdFor"/>.</param>
        /// <param name="playerProfile">The player's own name and company for the engine; <c>null</c> without.</param>
        /// <param name="difficulty">One of the engine's difficulty names; <c>null</c> uses the session's default.</param>
        public async void Initialize(
            ISalesGameSession salesSession, string scenario, PlayerProfile playerProfile = null, string difficulty = null)
        {
            session = salesSession ?? throw new ArgumentNullException(nameof(salesSession));
            DiscardSpeech(null);
            draftBeforeSpeech = string.Empty;
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

        private void OnSendClicked()
        {
            if (speechPhase != SpeechPhase.Idle)
            {
                return;
            }

            SendTurn(playerInput.text.Trim());
        }

        /// <summary>The one path for a player's line, typed or spoken.</summary>
        private async void SendTurn(string message)
        {
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
            catch (OperationCanceledException) when (this == null || destroyCancellationToken.IsCancellationRequested)
            {
                // The scene is going away; there is no UI left to release.
                return;
            }
            catch (SalesTurnFailedException exception)
            {
                // Expected provider problems (key, workspace, timeout, ...): a warning, and the player is told why.
                Debug.LogWarning($"Customer could not answer ({exception.Failure}): {exception.Message}", this);
                TurnNotTaken(playerMessage, message, TurnFailureText(exception.Failure));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                TurnNotTaken(playerMessage, message, TurnFailureText(SalesTurnFailure.Unknown));
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
                FocusInput();
            }
            else
            {
                ConversationEnded?.Invoke(session.CurrentState);
            }
        }

        /// <summary>
        /// The session did not take the turn: no customer line is invented, the reason is shown in the chat as a notice, and
        /// the text goes back into the input for a retry (unless the player already typed something new).
        /// </summary>
        private void TurnNotTaken(ConversationMessageView playerMessage, string message, string reason)
        {
            StopThinking();
            history.HideThinking();
            character.SetState(CharacterVisualState.Idle);
            playerMessage.MarkNotDelivered();
            history.AddNotice(reason);
            if (playerInput.text.Length == 0)
            {
                playerInput.text = message;
            }
        }

        private static string TurnFailureText(SalesTurnFailure failure)
        {
            switch (failure)
            {
                case SalesTurnFailure.Authentication:
                    return "Der Kunde konnte nicht antworten: Anthropic hat den API-Key abgelehnt (abgelaufen oder ungültig). "
                           + "Trag einen gültigen Key als SALESSIM_ANTHROPIC_API_KEY in die .env ein und starte Play neu.";
                case SalesTurnFailure.WorkspaceRequired:
                    return "Der Kunde konnte nicht antworten: Der Anthropic-Key gilt für die ganze Organisation, nicht für einen Workspace. "
                           + "Trag die Workspace-ID (wrkspc_…, Console → Settings → Workspaces) als SALESSIM_ANTHROPIC_WORKSPACE_ID in die .env ein "
                           + "oder nutz einen Key aus einem Workspace, dann Play neu starten.";
                case SalesTurnFailure.BadRequest:
                    return "Der Kunde konnte nicht antworten: Anthropic hat die Anfrage abgelehnt (z. B. unbekanntes Modell in SALESSIM_ANTHROPIC_MODEL). Details in der Console.";
                case SalesTurnFailure.QuotaExceeded:
                    return "Der Kunde konnte nicht antworten: Das Anthropic-Konto hat kein Guthaben mehr. Prüf Plan und Abrechnung.";
                case SalesTurnFailure.RateLimited:
                    return "Der Kunde konnte nicht antworten: Zu viele Anfragen. Warte einen Moment und sende die Nachricht erneut.";
                case SalesTurnFailure.Timeout:
                    return "Der Kunde hat nicht rechtzeitig geantwortet. Sende die Nachricht erneut.";
                case SalesTurnFailure.Network:
                    return "Der Kunde konnte nicht antworten: Anthropic ist nicht erreichbar. Prüf die Internetverbindung und sende erneut.";
                case SalesTurnFailure.ServiceUnavailable:
                    return "Der Kunde konnte nicht antworten: Anthropic hat gerade ein Problem. Sende die Nachricht gleich noch einmal.";
                case SalesTurnFailure.InvalidAnswer:
                    return "Der Kunde konnte nicht antworten: Die Antwort war nicht verwendbar. Sende die Nachricht erneut.";
                default:
                    return "Der Kunde konnte nicht antworten. Sende die Nachricht erneut; Details stehen in der Console.";
            }
        }

        private async void OnEndClicked()
        {
            if (session == null || turnInProgress || guidanceInProgress || conversationOver)
            {
                return;
            }

            // Ending while talking discards the recording: it never becomes a turn of this or any later conversation.
            DiscardSpeech(null);
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
                || speechPhase != SpeechPhase.Idle
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
                FocusInput();
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

        /// <summary>
        /// Gives the input the focus with the caret at the end of the draft. TMP activates the field only in its next
        /// update and would leave the caret at the start, so typing on would insert in front of the draft.
        /// </summary>
        private void FocusInput()
        {
            playerInput.ActivateInputField();
            StartCoroutine(MoveCaretToEndAfterActivation());
        }

        private IEnumerator MoveCaretToEndAfterActivation()
        {
            yield return null;
            if (playerInput.isFocused)
            {
                playerInput.MoveTextEnd(false);
            }
        }

        /// <summary>After a "Kopieren" click the player goes straight back to writing; the draft is kept.</summary>
        private void ReturnFocusToInput()
        {
            if (inputEnabled)
            {
                FocusInput();
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
            var speaking = speechPhase != SpeechPhase.Idle;
            // While talking, the input shows the live transcript and only the recording itself can be sent.
            playerInput.interactable = enabled && !speaking;
            sendButton.interactable = enabled && !guidanceInProgress && !speaking;
            endButton.interactable = enabled && !guidanceInProgress;
            speech.SetInteractable(speechPhase == SpeechPhase.Recording
                                   || (speechPhase == SpeechPhase.Idle && enabled && !guidanceInProgress && speechInput.IsAvailable));
            foreach (GuidanceLevel level in Enum.GetValues(typeof(GuidanceLevel)))
            {
                var availability = session == null ? GuidanceAvailability.NotStarted : session.GetGuidanceAvailability(level);
                guidance.SetAvailability(level, availability == GuidanceAvailability.Available, UnavailableNote(availability));
            }

            guidance.SetInteractable(enabled && !guidanceInProgress && !speaking);
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
