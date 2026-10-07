using System;
using SalesSim.Application;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// Live guidance beside the chat: the three request buttons and one section per level (Hinweis, Mehr Hinweis,
    /// Beispiel) showing the latest text, a loading line or an error. Display only — what to request and what the text
    /// says is decided by the controller and the engine. Guidance never goes into the conversation history.
    /// </summary>
    public sealed class GuidancePanelView : MonoBehaviour
    {
        private static readonly string[] LoadingFrames = { ".", "..", "..." };
        private const int RevealFrames = 3;
        private static readonly Color TextColor = Color.white;
        private static readonly Color LoadingColor = new Color(0.68f, 0.72f, 0.8f, 1f);
        private static readonly Color ErrorColor = new Color(1f, 0.62f, 0.55f, 1f);

        [SerializeField] private Button hintButton;
        [SerializeField] private Button hintMoreButton;
        [SerializeField] private Button exampleButton;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private GameObject emptyHint;
        [SerializeField] private Section hint;
        [SerializeField] private Section hintMore;
        [SerializeField] private Section example;
        [SerializeField] private float loadingFrameSeconds = 0.4f;

        private RectTransform revealTarget;
        private int revealFrames;
        private string loadingText = string.Empty;
        private float loadingElapsed;
        private int loadingFrame;

        private readonly bool[] available = { true, true, true };
        private string[] labels;
        private bool interactable = true;

        /// <summary>The player pressed one of the guidance buttons.</summary>
        public event Action<GuidanceLevel> Requested;

        /// <summary>The panel as a whole is unlocked (no customer turn or guidance request running).</summary>
        public bool IsInteractable => interactable;

        /// <summary>Whether the session currently allows <paramref name="level"/>, as last set by the controller.</summary>
        public bool IsAvailable(GuidanceLevel level) => available[(int)level];

        private void Awake()
        {
            hintButton.onClick.AddListener(() => Requested?.Invoke(GuidanceLevel.Hint));
            hintMoreButton.onClick.AddListener(() => Requested?.Invoke(GuidanceLevel.HintMore));
            exampleButton.onClick.AddListener(() => Requested?.Invoke(GuidanceLevel.Example));
            Clear();
        }

        public Button ButtonFor(GuidanceLevel level)
        {
            switch (level)
            {
                case GuidanceLevel.Hint: return hintButton;
                case GuidanceLevel.HintMore: return hintMoreButton;
                default: return exampleButton;
            }
        }

        public void SetInteractable(bool unlocked)
        {
            interactable = unlocked;
            ApplyInteractable();
        }

        /// <summary>
        /// Shows whether the session allows <paramref name="level"/> right now. An unavailable button is disabled and,
        /// with a <paramref name="note"/>, says why (e.g. "nicht auf dieser Stufe").
        /// </summary>
        public void SetAvailability(GuidanceLevel level, bool isAvailable, string note)
        {
            if (labels == null)
            {
                labels = new[] { Label(hintButton).text, Label(hintMoreButton).text, Label(exampleButton).text };
            }

            available[(int)level] = isAvailable;
            Label(ButtonFor(level)).text = isAvailable || string.IsNullOrEmpty(note)
                ? labels[(int)level]
                : $"{labels[(int)level]}\n<size=65%>{note}</size>";
            ApplyInteractable();
        }

        private void ApplyInteractable()
        {
            hintButton.interactable = interactable && available[(int)GuidanceLevel.Hint];
            hintMoreButton.interactable = interactable && available[(int)GuidanceLevel.HintMore];
            exampleButton.interactable = interactable && available[(int)GuidanceLevel.Example];
        }

        private static TMP_Text Label(Button button) => button.GetComponentInChildren<TMP_Text>();

        /// <summary>Removes all guidance, e.g. once the player has used it for a turn or a new run starts.</summary>
        public void Clear()
        {
            foreach (var section in new[] { hint, hintMore, example })
            {
                section.State = GuidanceSectionState.Hidden;
                section.Body.text = string.Empty;
                section.Root.SetActive(false);
            }

            emptyHint.SetActive(true);
            revealTarget = null;
        }

        public void ShowLoading(GuidanceLevel level, string text)
        {
            loadingElapsed = 0f;
            loadingFrame = 0;
            loadingText = text;
            Set(level, GuidanceSectionState.Loading, text + " " + LoadingFrames[0], LoadingColor, FontStyles.Italic);
        }

        public void Show(GuidanceResult guidance)
        {
            Set(guidance.Level, GuidanceSectionState.Shown, guidance.Text, TextColor, FontStyles.Normal);
        }

        public void ShowError(GuidanceLevel level, string message)
        {
            Set(level, GuidanceSectionState.Error, message, ErrorColor, FontStyles.Italic);
        }

        public GuidanceSectionState StateOf(GuidanceLevel level) => SectionFor(level).State;

        /// <summary>The text currently shown for <paramref name="level"/>; empty when the section is hidden.</summary>
        public string TextOf(GuidanceLevel level) => SectionFor(level).Body.text;

        private void Set(GuidanceLevel level, GuidanceSectionState state, string text, Color color, FontStyles style)
        {
            var section = SectionFor(level);
            section.State = state;
            section.Body.text = text;
            section.Body.color = color;
            section.Body.fontStyle = style;
            section.Root.SetActive(true);
            emptyHint.SetActive(false);
            revealTarget = (RectTransform)section.Root.transform;
            revealFrames = RevealFrames;
        }

        private Section SectionFor(GuidanceLevel level)
        {
            switch (level)
            {
                case GuidanceLevel.Hint: return hint;
                case GuidanceLevel.HintMore: return hintMore;
                default: return example;
            }
        }

        private void Update()
        {
            if (example.State != GuidanceSectionState.Loading)
            {
                return;
            }

            loadingElapsed += Time.unscaledDeltaTime;
            if (loadingElapsed < loadingFrameSeconds)
            {
                return;
            }

            loadingElapsed = 0f;
            loadingFrame = (loadingFrame + 1) % LoadingFrames.Length;
            example.Body.text = loadingText + " " + LoadingFrames[loadingFrame];
        }

        /// <summary>Scrolls the section that just changed into view; repeated for a few frames until TMP has its final height.</summary>
        private void LateUpdate()
        {
            if (revealTarget == null)
            {
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            var overflow = content.rect.height - scrollRect.viewport.rect.height;
            if (overflow > 0f)
            {
                var top = -revealTarget.anchoredPosition.y - revealTarget.rect.height * (1f - revealTarget.pivot.y);
                var bottom = top + revealTarget.rect.height;
                var viewTop = (1f - scrollRect.verticalNormalizedPosition) * overflow;
                var viewBottom = viewTop + scrollRect.viewport.rect.height;
                if (bottom > viewBottom)
                {
                    viewTop = Mathf.Min(top, bottom - scrollRect.viewport.rect.height);
                }
                else if (top < viewTop)
                {
                    viewTop = top;
                }

                scrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01(viewTop / overflow);
            }

            if (--revealFrames <= 0)
            {
                revealTarget = null;
            }
        }

        [Serializable]
        private sealed class Section
        {
            [SerializeField] private GameObject root;
            [SerializeField] private TMP_Text body;

            public GameObject Root => root;

            public TMP_Text Body => body;

            public GuidanceSectionState State { get; set; }
        }
    }

    public enum GuidanceSectionState
    {
        Hidden,
        Loading,
        Shown,
        Error,
    }
}
