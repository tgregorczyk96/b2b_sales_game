namespace SalesSim.Application
{
    /// <summary>
    /// Customer indicators exactly as reported by the engine. Scale and meaning are defined by the engine;
    /// Unity must only display them, never compute or adjust them.
    /// </summary>
    public sealed class SalesIndicators
    {
        public static readonly SalesIndicators None = new SalesIndicators(0f, 0f, 0f, 0f);

        public float Trust { get; }
        public float Openness { get; }
        public float Engagement { get; }
        public float Patience { get; }

        public SalesIndicators(float trust, float openness, float engagement, float patience)
        {
            Trust = trust;
            Openness = openness;
            Engagement = engagement;
            Patience = patience;
        }
    }
}
