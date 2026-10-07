using System;

namespace SalesSim.Application
{
    /// <summary>Why a guidance request could not be served.</summary>
    public enum GuidanceUnavailableReason
    {
        /// <summary>No engine is connected.</summary>
        NotConnected,

        /// <summary>The run's difficulty does not allow this level.</summary>
        NotAllowedByDifficulty,

        /// <summary>Example sentences need the AI, and none is configured.</summary>
        RequiresAi,

        /// <summary>The conversation has ended; guidance is only for running conversations.</summary>
        ConversationOver,

        /// <summary>Another guidance request is still running.</summary>
        Busy,

        /// <summary>The example sentence could not be produced (AI error, timeout, unusable answer, conversation moved on).</summary>
        GenerationFailed,
    }

    /// <summary>
    /// A guidance request was refused or failed. Nothing changed in the conversation and nothing counts as used; the
    /// request can be repeated or the player simply goes on.
    /// </summary>
    public sealed class GuidanceUnavailableException : Exception
    {
        public GuidanceUnavailableReason Reason { get; }

        public GuidanceUnavailableException(GuidanceUnavailableReason reason, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Reason = reason;
        }
    }
}
