namespace SalesSim.Application
{
    /// <summary>
    /// Who the player is in the conversation: their own name and the company they sell for. Passed to the engine, which
    /// uses it to phrase guidance (e.g. the introduction in an example sentence). Unity never fills it into texts itself.
    /// </summary>
    public sealed class PlayerProfile
    {
        public string Name { get; }

        public string Company { get; }

        public PlayerProfile(string name, string company)
        {
            Name = name ?? string.Empty;
            Company = company ?? string.Empty;
        }

        /// <summary>True when both name and company are set; otherwise the engine phrases guidance without them.</summary>
        public bool IsComplete => Name.Trim().Length > 0 && Company.Trim().Length > 0;
    }
}
