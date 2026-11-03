using System;
using System.Collections.Generic;
using System.Globalization;
using SalesSim.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SalesSim.Presentation
{
    /// <summary>
    /// Run setup before a conversation: difficulty (the engine's presets), seed (empty = random, or typed in, or rolled
    /// with the dice button), what is sold (the engine's scenario kinds, e.g. website or review stand on site), "Neuer Run" and
    /// "Seed wiederholen". Display and input only — the run loop starts runs.
    /// </summary>
    public sealed class RunSetupView : MonoBehaviour
    {
        private static readonly Color SelectedColor = new Color(0.55f, 0.78f, 1f, 1f);
        private static readonly Color NormalColor = new Color(0.93f, 0.94f, 0.96f, 1f);

        [SerializeField] private GameObject overlay;
        [SerializeField] private Button[] difficultyButtons;
        [SerializeField] private Button[] kindButtons = new Button[0];
        [SerializeField] private TMP_InputField seedInput;
        [SerializeField] private Button randomSeedButton;
        [SerializeField] private Button newRunButton;
        [SerializeField] private Button rerunButton;
        [SerializeField] private TMP_Text lastRunText;
        [SerializeField] private TMP_Text messageText;

        private readonly List<string> difficulties = new List<string>();
        private readonly List<string> kinds = new List<string>();
        private int selected;
        private int selectedKind;

        /// <summary>"Neuer Run" with the selected difficulty and the seed field's text (empty = random).</summary>
        public event Action<string, string> NewRunRequested;

        public event Action RerunRequested;

        public event Action RandomSeedRequested;

        public bool IsVisible => overlay.activeSelf;

        public string SelectedDifficulty => selected < difficulties.Count ? difficulties[selected] : string.Empty;

        /// <summary>The selected scenario kind's id; empty when the scene has no kind buttons or only one kind exists.</summary>
        public string SelectedKind => selectedKind < kinds.Count ? kinds[selectedKind] : string.Empty;

        public string SeedText
        {
            get => seedInput.text;
            set => seedInput.text = value ?? string.Empty;
        }

        public Button NewRunButton => newRunButton;

        public Button RerunButton => rerunButton;

        public Button RandomSeedButton => randomSeedButton;

        public string Message => messageText.text;

        public string LastRunText => lastRunText.text;

        private void Awake()
        {
            for (var i = 0; i < difficultyButtons.Length; i++)
            {
                var index = i;
                difficultyButtons[i].onClick.AddListener(() => SelectDifficulty(index));
            }

            for (var i = 0; i < kindButtons.Length; i++)
            {
                var index = i;
                kindButtons[i].onClick.AddListener(() => SelectKind(index));
            }

            randomSeedButton.onClick.AddListener(() => RandomSeedRequested?.Invoke());
            newRunButton.onClick.AddListener(() => NewRunRequested?.Invoke(SelectedDifficulty, seedInput.text));
            rerunButton.onClick.AddListener(() => RerunRequested?.Invoke());
        }

        /// <summary>Labels the difficulty buttons with the engine's presets; surplus buttons are hidden.</summary>
        public void SetDifficulties(IReadOnlyList<string> names)
        {
            difficulties.Clear();
            difficulties.AddRange(names);
            for (var i = 0; i < difficultyButtons.Length; i++)
            {
                var used = i < difficulties.Count;
                difficultyButtons[i].gameObject.SetActive(used);
                if (used)
                {
                    difficultyButtons[i].GetComponentInChildren<TMP_Text>().text = difficulties[i];
                }
            }

            SelectDifficulty(Mathf.Clamp(selected, 0, Math.Max(0, difficulties.Count - 1)));
        }

        /// <summary>Labels the kind buttons with the engine's scenario kinds; surplus buttons are hidden. Keeps the selection.</summary>
        public void SetKinds(IReadOnlyList<SalesSim.Application.ScenarioKind> available, string selectedId = null)
        {
            var previous = SelectedKind;
            kinds.Clear();
            for (var i = 0; i < kindButtons.Length; i++)
            {
                var used = available != null && i < available.Count && available.Count > 1;
                kindButtons[i].gameObject.SetActive(used);
                if (used)
                {
                    kinds.Add(available[i].Id);
                    kindButtons[i].GetComponentInChildren<TMP_Text>().text = available[i].Label;
                }
            }

            var wanted = string.IsNullOrEmpty(selectedId) ? previous : selectedId;
            SelectKind(Math.Max(0, kinds.IndexOf(wanted)));
        }

        public Button KindButton(string id)
        {
            var index = kinds.IndexOf(id);
            return index < 0 ? null : kindButtons[index];
        }

        public Button DifficultyButton(string name)
        {
            var index = difficulties.IndexOf(name);
            return index < 0 ? null : difficultyButtons[index];
        }

        /// <summary>Shows the setup; the seed field is empty (= random), the last run can be repeated.</summary>
        public void Show(RunInfo lastRun, int balance = 0, int playedSeeds = 0)
        {
            seedInput.text = string.Empty;
            messageText.text = string.Empty;
            var last = lastRun == null
                ? "Noch kein Run gespielt."
                : $"Letzter Run: {lastRun.Difficulty} · Seed {lastRun.Seed.ToString(CultureInfo.InvariantCulture)}";
            var seeds = playedSeeds == 1 ? "1 Seed gespielt" : $"{playedSeeds} Seeds gespielt";
            lastRunText.text = $"{last}\nGuthaben: {RunResultView.Euro(balance)} · {seeds}";
            rerunButton.interactable = lastRun != null;
            overlay.SetActive(true);
        }

        public void Hide()
        {
            overlay.SetActive(false);
        }

        public void ShowMessage(string message)
        {
            messageText.text = message ?? string.Empty;
        }

        private void SelectKind(int index)
        {
            selectedKind = index;
            for (var i = 0; i < kindButtons.Length; i++)
            {
                kindButtons[i].image.color = i == selectedKind ? SelectedColor : NormalColor;
            }
        }

        private void SelectDifficulty(int index)
        {
            selected = index;
            for (var i = 0; i < difficultyButtons.Length; i++)
            {
                difficultyButtons[i].image.color = i == selected ? SelectedColor : NormalColor;
            }
        }
    }
}
