using System.Globalization;
using SalesSim.Application;
using SalesSim.Game;
using TMPro;
using UnityEngine;

namespace SalesSim.Presentation
{
    /// <summary>
    /// The game loop: run setup (difficulty, seed) → conversation → run ends → lead is priced → sold to the closer, or
    /// the same seed is trained again → back to setup. Pricing and selling live in <see cref="SalesSim.Game"/>; seeds,
    /// scenarios and difficulties come from the engine via <see cref="IScenarioSource"/>. A rerun of a seed is a
    /// training run (<see cref="RunInfo.IsRerun"/>): evaluated as usual, never paid out. Wallet and run registry are the
    /// runtime state; with an <see cref="IGameStateStore"/> they are loaded on <see cref="Configure"/> and saved whenever
    /// a run starts (its seed counts as played from then on), ends (score) or a lead is sold.
    /// </summary>
    public sealed class SalesRunLoop : MonoBehaviour
    {
        public const string InvalidSeedMessage = "Seed muss eine ganze Zahl von 0 bis 2147483647 sein (leer = zufällig).";

        [SerializeField] private SalesConversationController conversation;
        [SerializeField] private RunResultView resultView;
        [SerializeField] private RunSetupView setupView;
        [SerializeField] private TMP_Text balanceLabel;
        [SerializeField] private TMP_Text runLabel;
        [SerializeField] private EconomySettings economy = new EconomySettings();

        private PlayerWallet wallet = new PlayerWallet();
        private RunRegistry runs = new RunRegistry();
        private IGameStateStore store;
        private ISalesGameSession session;
        private IScenarioSource scenarios;
        private PlayerProfile player;
        private LeadSale currentLead;

        public PlayerWallet Wallet => wallet;

        public RunRegistry Runs => runs;

        /// <summary>The run being played or just finished; <c>null</c> before the first run.</summary>
        public RunInfo CurrentRun { get; private set; }

        public LeadSale CurrentLead => currentLead;

        private void Awake()
        {
            conversation.ConversationEnded += OnConversationEnded;
            resultView.SellRequested += OnSellRequested;
            resultView.NextRunRequested += ShowSetup;
            resultView.RerunRequested += OnRerunRequested;
            setupView.NewRunRequested += OnNewRunRequested;
            setupView.RerunRequested += OnRerunRequested;
            setupView.RandomSeedRequested += OnRandomSeedRequested;
            runLabel.text = string.Empty;
            UpdateBalance();
        }

        private void OnDestroy()
        {
            conversation.ConversationEnded -= OnConversationEnded;
            resultView.SellRequested -= OnSellRequested;
            resultView.NextRunRequested -= ShowSetup;
            resultView.RerunRequested -= OnRerunRequested;
            setupView.NewRunRequested -= OnNewRunRequested;
            setupView.RerunRequested -= OnRerunRequested;
            setupView.RandomSeedRequested -= OnRandomSeedRequested;
        }

        /// <summary>
        /// Connects the loop to the engine, continues the saved game (if a store is given) and shows the run setup.
        /// Called once by the composition root; tests call it again with their own session and store.
        /// </summary>
        public void Configure(
            ISalesGameSession salesSession, IScenarioSource scenarioSource, PlayerProfile playerProfile, IGameStateStore gameStateStore = null)
        {
            session = salesSession;
            scenarios = scenarioSource;
            player = playerProfile;
            store = gameStateStore;
            var saved = store?.Load() ?? new SaveData();
            wallet = new PlayerWallet(saved.balance);
            runs = RunRegistry.Restore(saved);
            CurrentRun = null;
            currentLead = null;
            UpdateBalance();
            setupView.SetDifficulties(scenarios.Difficulties);
            ShowSetup();
        }

        /// <summary>Starts a run of <paramref name="seed"/> (a random one from the engine when <c>null</c>).</summary>
        public RunInfo StartNewRun(string difficulty, int? seed = null)
        {
            var chosen = seed ?? scenarios.PickRandomSeed();
            return StartRun(runs.Begin(chosen, difficulty, scenarios.ScenarioIdFor(chosen)));
        }

        /// <summary>Plays the last run's seed again at its difficulty: a training run. <c>null</c> before the first run.</summary>
        public RunInfo Rerun()
        {
            var last = runs.Last;
            return last == null ? null : StartRun(runs.Begin(last.Seed, last.Difficulty, last.ScenarioId));
        }

        private RunInfo StartRun(RunInfo run)
        {
            CurrentRun = run;
            currentLead = null;
            resultView.Hide();
            setupView.Hide();
            runLabel.text = RunResultView.RunSummary(run, 0f, null);
            Save(); // from now on the seed is played, also if the game is closed mid-run
            conversation.Initialize(session, run.ScenarioId, player, run.Difficulty);
            return run;
        }

        private void ShowSetup()
        {
            resultView.Hide();
            runLabel.text = string.Empty;
            setupView.Show(runs.Last, wallet.Balance, runs.PlayedSeedCount);
        }

        private void OnNewRunRequested(string difficulty, string seedText)
        {
            if (!RunInfo.TryParseSeed(seedText, out var seed))
            {
                setupView.ShowMessage(InvalidSeedMessage);
                return;
            }

            StartNewRun(difficulty, seed);
        }

        private void OnRerunRequested()
        {
            Rerun();
        }

        private void OnRandomSeedRequested()
        {
            setupView.SeedText = scenarios.PickRandomSeed().ToString(CultureInfo.InvariantCulture);
            setupView.ShowMessage(string.Empty);
        }

        private void OnConversationEnded(SalesSessionState finalState)
        {
            var evaluation = LeadEvaluator.Evaluate(finalState, economy);
            currentLead = new LeadSale(evaluation, CurrentRun != null && CurrentRun.IsRerun);
            float? previousScore = null;
            if (CurrentRun != null)
            {
                previousScore = runs.LastScore(CurrentRun.Seed);
                runs.RecordScore(CurrentRun.Seed, evaluation.Score);
            }

            Save();

            resultView.Show(currentLead, economy.closerShare, wallet.Balance, conversation.GuidanceUsage, CurrentRun, previousScore);
        }

        private void OnSellRequested()
        {
            if (currentLead == null || !currentLead.CanSell)
            {
                return;
            }

            currentLead.SellTo(wallet);
            Save();
            resultView.Refresh(currentLead, wallet.Balance);
            UpdateBalance();
        }

        private void Save()
        {
            if (store == null)
            {
                return;
            }

            var data = new SaveData { balance = wallet.Balance };
            runs.WriteTo(data);
            store.Save(data);
        }

        private void UpdateBalance()
        {
            balanceLabel.text = $"Guthaben: {RunResultView.Euro(wallet.Balance)}";
        }
    }
}
