using System;

namespace SalesSim.Application
{
    /// <summary>Why the customer could not answer a turn. Decides what the player is told; never changes the conversation.</summary>
    public enum SalesTurnFailure
    {
        /// <summary>The AI provider rejected the API key (expired, revoked, wrong).</summary>
        Authentication,

        /// <summary>The API key is valid but not scoped to a workspace, and no workspace id is configured.</summary>
        WorkspaceRequired,

        /// <summary>The provider rejected the request in another way (e.g. unknown model).</summary>
        BadRequest,

        /// <summary>Too many requests; trying again in a moment may work.</summary>
        RateLimited,

        /// <summary>The account has no remaining credit.</summary>
        QuotaExceeded,

        /// <summary>The provider did not answer in time.</summary>
        Timeout,

        /// <summary>The provider could not be reached.</summary>
        Network,

        /// <summary>The provider had an internal problem.</summary>
        ServiceUnavailable,

        /// <summary>The answer could not be used (unreadable, cut off, refused).</summary>
        InvalidAnswer,

        /// <summary>Anything else.</summary>
        Unknown,
    }

    /// <summary>
    /// The session did not take the turn: no customer answer exists, nothing was counted, the state is unchanged and the
    /// same line can be sent again. Messages never contain API keys or conversation content.
    /// </summary>
    public sealed class SalesTurnFailedException : Exception
    {
        public SalesTurnFailedException(SalesTurnFailure failure, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Failure = failure;
        }

        public SalesTurnFailure Failure { get; }

        /// <summary>Whether sending the same turn again can help without changing the configuration.</summary>
        public bool IsRetryable =>
            Failure == SalesTurnFailure.RateLimited || Failure == SalesTurnFailure.Timeout || Failure == SalesTurnFailure.Network
            || Failure == SalesTurnFailure.ServiceUnavailable || Failure == SalesTurnFailure.InvalidAnswer || Failure == SalesTurnFailure.Unknown;
    }
}
