using System.Collections.Generic;
using SalesSim.Presentation;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>Records what the "Kopieren" buttons copy, without touching the operating system clipboard.</summary>
    internal sealed class FakeClipboard : IClipboard
    {
        public List<string> Copied { get; } = new List<string>();

        public string Text => Copied.Count == 0 ? null : Copied[Copied.Count - 1];

        public void SetText(string text)
        {
            Copied.Add(text);
        }
    }
}
