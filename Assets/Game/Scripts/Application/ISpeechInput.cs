using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalesSim.Application
{
    /// <summary>
    /// Push-to-talk speech input: records the player and turns the recording into text. Only produces text; whether it
    /// becomes a turn is the caller's decision (the conversation sends it like a typed line). Provider-independent: the
    /// engine's speech-to-text sits behind it in the adapter, the microphone behind <see cref="IMicrophone"/>.
    /// </summary>
    public interface ISpeechInput
    {
        /// <summary>False when speech input cannot be used at all (e.g. no API key); <see cref="UnavailableReason"/> says why.</summary>
        bool IsAvailable { get; }

        /// <summary>Short reason for the player when <see cref="IsAvailable"/> is false; empty otherwise.</summary>
        string UnavailableReason { get; }

        /// <summary>Opens the transcription and starts the microphone; once the task completes, the player is recorded.</summary>
        /// <param name="partialTranscripts">Interim transcripts for display while recording; they can still change.</param>
        /// <exception cref="SpeechInputException">Recording or transcription could not be started.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task<ISpeechRecording> StartRecordingAsync(IProgress<string> partialTranscripts, CancellationToken cancellationToken);
    }

    /// <summary>
    /// One running recording. <see cref="FinishAsync"/> ends it and returns the transcript; disposing without finishing
    /// discards it: no transcript is produced and nothing of it can become a turn.
    /// </summary>
    public interface ISpeechRecording : IDisposable
    {
        /// <summary>
        /// Never completes while the recording works; faults with a <see cref="SpeechInputException"/> when it breaks
        /// before <see cref="FinishAsync"/> (microphone stopped, connection lost).
        /// </summary>
        Task Interrupted { get; }

        /// <summary>Stops the microphone and waits for the final transcript.</summary>
        /// <returns>The final transcript, trimmed; empty when no speech was recognized.</returns>
        /// <exception cref="SpeechInputException">The recording or the transcription failed.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        Task<string> FinishAsync(CancellationToken cancellationToken);
    }

    /// <summary>Speech input that cannot be used, e.g. because it is not configured.</summary>
    public sealed class UnavailableSpeechInput : ISpeechInput
    {
        public UnavailableSpeechInput(string reason)
        {
            UnavailableReason = reason ?? string.Empty;
        }

        public bool IsAvailable => false;

        public string UnavailableReason { get; }

        public Task<ISpeechRecording> StartRecordingAsync(IProgress<string> partialTranscripts, CancellationToken cancellationToken)
        {
            throw new SpeechInputException(SpeechInputError.NotConfigured, UnavailableReason);
        }
    }
}
