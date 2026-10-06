namespace SalesSim.Application
{
    /// <summary>Parameters for starting a session. The scenario id is opaque to Unity and interpreted by the engine.</summary>
    public sealed class SessionStartRequest
    {
        public string ScenarioId { get; }

        public SessionStartRequest(string scenarioId)
        {
            ScenarioId = scenarioId ?? string.Empty;
        }
    }
}
