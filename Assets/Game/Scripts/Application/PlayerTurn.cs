namespace SalesSim.Application
{
    /// <summary>A single statement made by the player.</summary>
    public sealed class PlayerTurn
    {
        public string Message { get; }

        public PlayerTurn(string message)
        {
            Message = message ?? string.Empty;
        }
    }
}
