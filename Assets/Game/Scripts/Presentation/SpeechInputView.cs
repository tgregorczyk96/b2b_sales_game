using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    public enum SpeechInputViewState
    {
        /// <summary>Speech input can be started (Enter or the button).</summary>
        Ready,

        /// <summary>The transcription is being opened and the microphone started.</summary>
        Starting,

        /// <summary>The microphone is recording.</summary>
        Recording,

        /// <summary>The recording is being transcribed.</summary>
        Processing,

        /// <summary>The last attempt failed; a new one can be started.</summary>
        Error,

        /// <summary>Speech input cannot be used (not configured).</summary>
        Unavailable,
    }

    /// <summary>
    /// The microphone button next to the input and the status line below it. Shows the speech input's state only; what
    /// happens on a click is the <see cref="SalesConversationController"/>'s decision.
    /// </summary>
    public sealed class SpeechInputView : MonoBehaviour
    {
        private static readonly Color ReadyColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        private static readonly Color RecordingColor = new Color(0.86f, 0.2f, 0.22f, 1f);
        private static readonly Color BusyColor = new Color(0.98f, 0.76f, 0.3f, 1f);
        private static readonly Color DarkLabel = new Color(0.15f, 0.16f, 0.2f, 1f);
        private static readonly Color MutedStatus = new Color(0.78f, 0.81f, 0.88f, 1f);
        private static readonly Color RecordingStatus = new Color(1f, 0.55f, 0.55f, 1f);
        private static readonly Color ErrorStatus = new Color(1f, 0.62f, 0.4f, 1f);

        [SerializeField] private Button button;
        [SerializeField] private Image buttonImage;
        [SerializeField] private TMP_Text buttonLabel;
        [SerializeField] private TMP_Text statusLabel;
        [Tooltip("Pulses while the microphone records.")]
        [SerializeField] private Graphic recordingDot;

        public event Action Clicked;

        public SpeechInputViewState State { get; private set; } = SpeechInputViewState.Ready;

        public string StatusText => statusLabel.text;

        public string ButtonText => buttonLabel.text;

        public bool IsInteractable => button.interactable;

        private void Awake()
        {
            button.onClick.AddListener(OnClicked);
        }

        private void OnDestroy()
        {
            button.onClick.RemoveListener(OnClicked);
        }

        private void Update()
        {
            if (State == SpeechInputViewState.Recording)
            {
                var color = recordingDot.color;
                color.a = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
                recordingDot.color = color;
            }
        }

        public void Show(SpeechInputViewState state, string status)
        {
            State = state;
            statusLabel.text = status ?? string.Empty;
            recordingDot.gameObject.SetActive(state == SpeechInputViewState.Recording);

            switch (state)
            {
                case SpeechInputViewState.Recording:
                    Style("Stopp", RecordingColor, Color.white, RecordingStatus);
                    break;
                case SpeechInputViewState.Starting:
                case SpeechInputViewState.Processing:
                    Style("…", BusyColor, DarkLabel, MutedStatus);
                    break;
                case SpeechInputViewState.Error:
                    Style("Sprechen", ReadyColor, DarkLabel, ErrorStatus);
                    break;
                default:
                    Style("Sprechen", ReadyColor, DarkLabel, MutedStatus);
                    break;
            }
        }

        public void SetInteractable(bool interactable)
        {
            button.interactable = interactable;
        }

        private void Style(string label, Color background, Color labelColor, Color statusColor)
        {
            buttonLabel.text = label;
            buttonLabel.color = labelColor;
            buttonImage.color = background;
            statusLabel.color = statusColor;
        }

        private void OnClicked()
        {
            Clicked?.Invoke();
        }
    }
}
