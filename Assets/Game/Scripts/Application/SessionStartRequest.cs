namespace SalesSim.Application
{
    /// <summary>Parameters for starting a session. The scenario id is opaque to Unity and interpreted by the engine.</summary>
    public sealed class SessionStartRequest
    {
        public string ScenarioId { get; }

        /// <summary>The player's own name and company, if known; <c>null</c> lets the engine phrase guidance without them.</summary>
        public PlayerProfile Player { get; }

        /// <summary>One of <see cref="IScenarioSource.Difficulties"/>; empty uses the session's default difficulty.</summary>
        public string Difficulty { get; }

        public SessionStartRequest(string scenarioId, PlayerProfile player = null, string difficulty = null)
        {
            ScenarioId = scenarioId ?? string.Empty;
            Player = player;
            Difficulty = difficulty ?? string.Empty;
        }
    }
}
