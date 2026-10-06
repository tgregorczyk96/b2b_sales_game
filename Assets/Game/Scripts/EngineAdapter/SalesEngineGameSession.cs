using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SalesEngine;
using SalesEngine.Content;
using SalesEngine.Customers;
using SalesEngine.Difficulty;
using SalesSim.Application;

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
    /// </remarks>
    public sealed class SalesEngineGameSession : ISalesGameSession
    {
        private readonly string contentDirectory;
        private readonly Func<ISalesEngine> createEngine;
        private readonly DifficultyProfile difficulty;
        private ISalesEngine engine;
        private string sessionId = string.Empty;

        /// <param name="contentDirectory">The engine's <c>content</c> folder (curated scenario JSON).</param>
        /// <param name="createEngine">Creates a fresh engine per session, with the customer simulator and interpreter wired in.</param>
        /// <param name="difficulty">Run difficulty passed to the engine.</param>
        public SalesEngineGameSession(string contentDirectory, Func<ISalesEngine> createEngine, DifficultyProfile difficulty)
        {
            this.contentDirectory = contentDirectory ?? throw new ArgumentNullException(nameof(contentDirectory));
            this.createEngine = createEngine ?? throw new ArgumentNullException(nameof(createEngine));
            this.difficulty = difficulty ?? throw new ArgumentNullException(nameof(difficulty));
        }

        public SalesSessionState CurrentState { get; private set; } = SalesSessionState.Empty;

        /// <summary>
        /// The engine without an LLM: the engine's built-in placeholder customer answers (scripted lines, no intent, no
        /// stage changes), while origin, customer state and all rules are the engine's own. Stand-in until an LLM
        /// provider can run inside Unity (the Anthropic SDK needs System.Text.Json 10; Unity ships 8).
        /// </summary>
        public static SalesEngineGameSession CreateWithoutLlm(string contentDirectory)
        {
            return new SalesEngineGameSession(contentDirectory, () => new SalesSimulationEngine(), DifficultyProfile.Easy);
        }

        public Task<SalesSessionState> StartSessionAsync(SessionStartRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var (pool, seed) = ParseScenarioId(request.ScenarioId);
            var origin = new ContentRepository(contentDirectory).LoadGenerator(pool).Generate(seed);
            var newEngine = createEngine();
            var session = newEngine.StartScenario(origin.Scenario, difficulty);

            engine = newEngine;
            sessionId = Guid.NewGuid().ToString("N");
            var opening = session.Turns[session.Turns.Count - 1].Text;
            CurrentState = new SalesSessionState(
                sessionId, opening, ToIndicators(session.CustomerState), session.Stage.ToString(), string.Empty, session.IsEnded);
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

            var result = await engine.ProcessTurnAsync(turn.Message, cancellationToken);
            var intent = result.CustomerReaction == null ? string.Empty : result.CustomerReaction.Intent.ToString();
            CurrentState = new SalesSessionState(
                sessionId, result.CustomerReply, ToIndicators(result.StateAfter), result.Stage.ToString(), intent, result.End != null);
            return CurrentState;
        }

        private static SalesIndicators ToIndicators(CustomerState state)
        {
            return new SalesIndicators(state.Trust, state.Openness, state.Interest, state.Patience);
        }

        private static (string Pool, int Seed) ParseScenarioId(string scenarioId)
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
