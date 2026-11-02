using System;

namespace SalesSim.Application
{
    public enum SpeechInputError
    {
        /// <summary>Speech input is not set up (e.g. no API key).</summary>
        NotConfigured,

        /// <summary>No recording device exists.</summary>
        NoMicrophone,

        /// <summary>The operating system does not let the game use the microphone, or it delivers no signal.</summary>
        MicrophoneDenied,

        /// <summary>The microphone exists but could not be started or stopped working.</summary>
        MicrophoneFailed,

        /// <summary>The speech service rejected the API key.</summary>
        Authentication,

        /// <summary>The speech service could not be reached or the connection was lost.</summary>
        Unreachable,

        /// <summary>The speech service did not answer in time.</summary>
        Timeout,

        /// <summary>The speech service is busy or rate limited; trying again later may work.</summary>
        Busy,

        /// <summary>The account has no remaining quota.</summary>
        QuotaExceeded,

        /// <summary>The speech service failed in another way.</summary>
        ServiceError,
    }

    /// <summary>Speech input failed. Messages never contain API keys, audio or transcripts.</summary>
    public sealed class SpeechInputException : Exception
    {
        public SpeechInputException(SpeechInputError error, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Error = error;
        }

        public SpeechInputError Error { get; }
    }
}
