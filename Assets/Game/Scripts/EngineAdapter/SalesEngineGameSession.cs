using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SalesEngine;
using SalesEngine.AI;
using SalesEngine.AI.Anthropic;
using SalesEngine.AI.Customers;
using SalesEngine.AI.Interpretation;
using SalesEngine.AI.Training;
using SalesEngine.Content;
using SalesEngine.Customers;
using SalesEngine.Difficulty;
using SalesEngine.Training;
using SalesSim.Application;
using EngineGuidanceAvailability = SalesEngine.Training.GuidanceAvailability;
using GuidanceAvailability = SalesSim.Application.GuidanceAvailability;

namespace SalesSim.EngineAdapter
{
    /// <summary>
    /// <see cref="ISalesGameSession"/> backed by the real Sales Engine. Only translates between Unity's boundary types and
    /// the engine API; every sales rule (customer reaction, state changes, stages, ending) stays in the engine.
    /// </summary>
    /// <remarks>
    /// Scenario ids have the form <c>{generationPool}:{seed}</c>, e.g. <c>hair-salon:1406361028</c>: the origin scenario is
    /// regenerated from the engine's curated content and the seed, exactly as the engine's console does.
    /// The engine has no numeric engagement value; <see cref="SalesIndicators.Engagement"/> shows the engine's Interest.
    /// Live guidance comes from the engine's <see cref="TrainingCoach"/> (one per session): Hint = Orientation and
    /// HintMore = Specific are deterministic; Example = FullExampleSentence needs the LLM example generator.
    /// Diagnostics go to the optional log callback, prefixed with <see cref="LogPrefix"/>; they never contain the API key.
    /// </remarks>
    public sealed class SalesEngineGameSession : ISalesGameSession
    {
        public const string LogPrefix = "[SalesClient] ";

        private static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        private readonly string contentDirectory;
        private readonly Func<ISalesEngine> createEngine;
        private readonly DifficultyProfile difficulty;
        private readonly Action<string> log;
        private readonly string fallbackReason;
        private readonly IExampleSentenceGenerator exampleGenerator;
        private ISalesEngine engine;
        private TrainingCoach coach;
        private SalesEngine.Conversations.ConversationSession session;
        private string sessionId = string.Empty;

        /// <param name="contentDirectory">The engine's <c>content</c> folder (curated scenario JSON).</param>
        /// <param name="createEngine">Creates a fresh engine per session, with the customer simulator and interpreter wired in.</param>
        /// <param name="difficulty">Run difficulty passed to the engine.</param>
        /// <param name="exampleGenerator">Phrases example sentences; without one, <see cref="GuidanceLevel.Example"/> is unavailable.</param>
        public SalesEngineGameSession(
            string contentDirectory, Func<ISalesEngine> createEngine, DifficultyProfile difficulty, IExampleSentenceGenerator exampleGenerator = null)
            : this(contentDirectory, createEngine, difficulty, null, null, exampleGenerator)
        {
        }

        private SalesEngineGameSession(
            string contentDirectory, Func<ISalesEngine> createEngine, DifficultyProfile difficulty, Action<string> log, string fallbackReason,
            IExampleSentenceGenerator exampleGenerator)
        {
            this.contentDirectory = contentDirectory ?? throw new ArgumentNullException(nameof(contentDirectory));
            this.createEngine = createEngine ?? throw new ArgumentNullException(nameof(createEngine));
            this.difficulty = difficulty ?? throw new ArgumentNullException(nameof(difficulty));
            this.log = log ?? (_ => { });
            this.fallbackReason = fallbackReason;
            this.exampleGenerator = exampleGenerator;
        }

        public SalesSessionState CurrentState { get; private set; } = SalesSessionState.Empty;

        /// <summary>True when the engine's placeholder customer answers instead of the LLM customer.</summary>
        public bool UsesFallbackCustomer => fallbackReason != null;

        /// <summary>
        /// The engine with the Anthropic-played customer and conversation interpreter when
        /// <c>SALESSIM_ANTHROPIC_API_KEY</c> is set; otherwise the engine's placeholder customer, with the reason logged.
        /// Reads the engine's <see cref="LlmOptions"/> settings (key, model, timeout) through <paramref name="getSetting"/>.
        /// </summary>
        /// <param name="httpHandler">Replaces the network for tests; <c>null</c> uses the real HTTP stack.</param>
        /// <exception cref="AiConfigurationException">A setting is present but invalid (e.g. a non-numeric timeout).</exception>
        public static SalesEngineGameSession Create(
            string contentDirectory, Func<string, string> getSetting, Action<string> log, HttpMessageHandler httpHandler = null)
        {
            if (getSetting == null)
            {
                throw new ArgumentNullException(nameof(getSetting));
            }

            if (string.IsNullOrWhiteSpace(getSetting(LlmOptions.AnthropicApiKeyVariable)))
            {
                return CreateWithoutLlm(contentDirectory, log,
                    $"{LlmOptions.AnthropicApiKeyVariable} is not set (environment or .env)");
            }

            var options = LlmOptions.FromEnvironment(LlmProvider.Anthropic, getSetting);
            var trace = log == null ? null : new Action<string>(line => log(LogPrefix + line));
            var httpClient = httpHandler == null ? SharedHttpClient : new HttpClient(httpHandler) { Timeout = Timeout.InfiniteTimeSpan };
            var client = new AnthropicMessagesClient(httpClient, options, trace);
            log?.Invoke($"{LogPrefix}Customer: Anthropic LLM (model {options.Model}, timeout {options.Timeout.TotalSeconds:0}s)");
            return new SalesEngineGameSession(
                contentDirectory,
                () => new SalesSimulationEngine(
                    customerSimulator: new LlmCustomerSimulator(client, options.Timeout),
                    conversationInterpreter: new LlmConversationInterpreter(client, options.Timeout)),
                DifficultyProfile.Easy,
                log,
                null,
                new LlmExampleSentenceGenerator(client, options.Timeout));
        }

        /// <summary>
        /// The engine without an LLM: the engine's built-in placeholder customer answers (scripted lines, no intent, no
        /// stage changes), while origin, customer state and all rules are the engine's own.
        /// </summary>
        public static SalesEngineGameSession CreateWithoutLlm(string contentDirectory, Action<string> log = null, string reason = null)
        {
            var fallbackReason = reason ?? "no LLM customer configured";
            log?.Invoke($"{LogPrefix}Customer: engine placeholder (no LLM)");
            log?.Invoke($"{LogPrefix}Fallback reason: {fallbackReason}");
            return new SalesEngineGameSession(contentDirectory, () => new SalesSimulationEngine(), DifficultyProfile.Easy, log, fallbackReason, null);
        }

        public Task<SalesSessionState> StartSessionAsync(SessionStartRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var (pool, seed) = ParseScenarioId(request.ScenarioId);
            var runDifficulty = request.Difficulty.Length == 0 ? difficulty : DifficultyNamed(request.Difficulty);
            var origin = new ContentRepository(contentDirectory).LoadGenerator(pool).Generate(seed);
            var newEngine = createEngine();
            var newSession = newEngine.StartScenario(origin.Scenario, runDifficulty);
            log($"{LogPrefix}Run: {pool}:{seed} (generator v{origin.GeneratorVersion}, origin {origin.Source}, "
                + $"customer {origin.Blocks.Customer}), difficulty {runDifficulty.Name}");

            engine = newEngine;
            session = newSession;
            coach = new TrainingCoach(newSession, exampleGenerator, ToTrainee(request.Player));
            sessionId = Guid.NewGuid().ToString("N");
            var opening = newSession.Turns[newSession.Turns.Count - 1].Text;
            CurrentState = new SalesSessionState(
                sessionId, opening, ToIndicators(newSession.CustomerState), newSession.Stage.ToString(), string.Empty, newSession.IsEnded);
            return Task.FromResult(CurrentState);
        }

        public async Task<SalesSessionState> SendPlayerTurnAsync(PlayerTurn turn, CancellationToken cancellationToken = default)
        {
            if (turn == null)
            {
                throw new ArgumentNullException(nameof(turn));
            }

            if (engine == null)
            {
                throw new InvalidOperationException("Start a session before sending a player turn.");
            }

            SalesEngine.Conversations.TurnResult result;
            try
            {
                result = await engine.ProcessTurnAsync(turn.Message, cancellationToken);
            }
            catch (CustomerSimulationFailedException exception)
            {
                // The engine does not fall back to scripted lines: the turn is not recorded and can be sent again.
                var cause = exception.InnerException ?? exception;
                if (cause is InvalidLlmResponseException)
                {
                    log($"{LogPrefix}Deserialization successful: no (customer answer does not match the schema)");
                }

                log($"{LogPrefix}Turn failed: {cause.GetType().Name}: {cause.Message}");
                log($"{LogPrefix}Fallback used: no");
                log($"{LogPrefix}Fallback reason: - (turn not recorded; it can be sent again)");
                throw;
            }

            log($"{LogPrefix}Fallback used: {(UsesFallbackCustomer ? "yes" : "no")}");
            log($"{LogPrefix}Fallback reason: {fallbackReason ?? "-"}");

            var intent = result.CustomerReaction == null ? string.Empty : result.CustomerReaction.Intent.ToString();
            CurrentState = new SalesSessionState(
                sessionId, result.CustomerReply, ToIndicators(result.StateAfter), result.Stage.ToString(), intent, result.End != null,
                result.End == null ? string.Empty : result.End.Reason.ToString(), CurrentCommitment());
            return CurrentState;
        }

        public Task<SalesSessionState> EndSessionAsync(CancellationToken cancellationToken = default)
        {
            if (engine == null || session == null)
            {
                throw new InvalidOperationException("Start a session before ending it.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var ended = engine.EndScenario();
            CurrentState = new SalesSessionState(
                sessionId, string.Empty, ToIndicators(ended.CustomerState), ended.Stage.ToString(), CurrentState.Intent, ended.IsEnded,
                ended.End == null ? string.Empty : ended.End.Reason.ToString(), CurrentCommitment());
            return Task.FromResult(CurrentState);
        }

        public async Task<GuidanceResult> RequestGuidanceAsync(GuidanceLevel level, CancellationToken cancellationToken = default)
        {
            if (coach == null)
            {
                throw new InvalidOperationException("Start a session before requesting guidance.");
            }

            var type = ToGuidanceType(level);
            var availability = coach.Check(type);
            if (availability != EngineGuidanceAvailability.Available)
            {
                log($"{LogPrefix}Guidance {type}: not available ({availability})");
                throw new GuidanceUnavailableException(ToReason(availability), $"Guidance {type} is not available: {availability}.");
            }

            Guidance guidance;
            try
            {
                guidance = await coach.RequestAsync(type, cancellationToken);
            }
            catch (GuidanceFailedException exception)
            {
                // The coach recorded nothing and the conversation is unchanged; the request can be repeated.
                var cause = exception.InnerException ?? exception;
                log($"{LogPrefix}Guidance {type} failed: {cause.GetType().Name}: {cause.Message}");
                throw new GuidanceUnavailableException(GuidanceUnavailableReason.GenerationFailed, exception.Message, exception);
            }
            catch (InvalidOperationException exception)
            {
                // Availability was checked above, so this is the coach refusing a second request while one is running.
                throw new GuidanceUnavailableException(GuidanceUnavailableReason.Busy, exception.Message, exception);
            }

            log($"{LogPrefix}Guidance {type}: focus {guidance.Focus}, for turn {guidance.TraineeTurn}");
            return new GuidanceResult(level, guidance.Text, guidance.Focus.ToString());
        }

        private static GuidanceType ToGuidanceType(GuidanceLevel level)
        {
            switch (level)
            {
                case GuidanceLevel.Hint: return GuidanceType.Orientation;
                case GuidanceLevel.HintMore: return GuidanceType.Specific;
                case GuidanceLevel.Example: return GuidanceType.FullExampleSentence;
                default: throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }

        /// <summary>The engine's own availability check (difficulty, example generator, conversation running).</summary>
        public GuidanceAvailability GetGuidanceAvailability(GuidanceLevel level)
        {
            if (coach == null)
            {
                return GuidanceAvailability.NotStarted;
            }

            switch (coach.Check(ToGuidanceType(level)))
            {
                case EngineGuidanceAvailability.Available: return GuidanceAvailability.Available;
                case EngineGuidanceAvailability.NotAllowedByDifficulty: return GuidanceAvailability.NotAllowedByDifficulty;
                case EngineGuidanceAvailability.NoGenerator: return GuidanceAvailability.RequiresAi;
                default: return GuidanceAvailability.ConversationOver;
            }
        }

        private static GuidanceUnavailableReason ToReason(EngineGuidanceAvailability availability)
        {
            switch (availability)
            {
                case EngineGuidanceAvailability.NotAllowedByDifficulty: return GuidanceUnavailableReason.NotAllowedByDifficulty;
                case EngineGuidanceAvailability.NoGenerator: return GuidanceUnavailableReason.RequiresAi;
                default: return GuidanceUnavailableReason.ConversationOver;
            }
        }

        /// <summary>The player's name and company for the engine's coach; incomplete profiles fall back to none.</summary>
        private static TraineeProfile ToTrainee(PlayerProfile player)
        {
            return player != null && player.IsComplete ? new TraineeProfile(player.Name, player.Company) : null;
        }

        /// <summary>The next step the engine considers agreed right now (its closing outcome); empty without one.</summary>
        private string CurrentCommitment()
        {
            var current = session.ClosingOutcome.Current;
            return current == null ? string.Empty : current.Kind.ToString();
        }

        private static SalesIndicators ToIndicators(CustomerState state)
        {
            return new SalesIndicators(state.Trust, state.Openness, state.Interest, state.Patience);
        }

        /// <summary>The engine's run difficulty presets, easiest first.</summary>
        internal static readonly DifficultyProfile[] Difficulties = { DifficultyProfile.Easy, DifficultyProfile.Medium, DifficultyProfile.Hard };

        private static DifficultyProfile DifficultyNamed(string name)
        {
            foreach (var preset in Difficulties)
            {
                if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }

            throw new ArgumentException($"Unknown difficulty '{name}'. Known: Easy, Medium, Hard.", nameof(name));
        }

        internal static (string Pool, int Seed) ParseScenarioId(string scenarioId)
        {
            var separator = scenarioId.LastIndexOf(':');
            if (separator > 0
                && int.TryParse(scenarioId.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var seed))
            {
                return (scenarioId.Substring(0, separator), seed);
            }

            throw new ArgumentException(
                $"Scenario id '{scenarioId}' is not of the form '<generation pool>:<seed>', e.g. 'hair-salon:1406361028'.",
                nameof(scenarioId));
        }
    }
}
