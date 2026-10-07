using UnityEngine;

namespace SalesSim.Presentation
{
    /// <summary>Where copied text goes. The system clipboard in the game; a fake in tests.</summary>
    public interface IClipboard
    {
        void SetText(string text);
    }

    /// <summary>The operating system clipboard (the same one TMP's Ctrl+C writes to).</summary>
    public sealed class SystemClipboard : IClipboard
    {
        public void SetText(string text)
        {
            GUIUtility.systemCopyBuffer = text ?? string.Empty;
        }
    }

    /// <summary>
    /// The one place the "Kopieren" buttons write to. Tests replace <see cref="Current"/> with a fake so they never
    /// depend on the Windows clipboard; Ctrl+C on selected text keeps using TMP's own path to the system clipboard.
    /// </summary>
    public static class TextClipboard
    {
        public static IClipboard Current { get; set; } = new SystemClipboard();
    }
}
