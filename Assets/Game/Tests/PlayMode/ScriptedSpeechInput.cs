using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SalesSim.Application;

namespace SalesSim.Tests.PlayMode
{
    /// <summary>
    /// <see cref="ISpeechInput"/> for UI tests: every recording waits until the test finishes, fails or interrupts it.
    /// No microphone, no API.
    /// </summary>
    internal sealed class ScriptedSpeechInput : ISpeechInput
    {
        public bool IsAvailable { get; set; } = true;

        public string UnavailableReason { get; set; } = string.Empty;

        /// <summary>Thrown by the next start, then cleared.</summary>
        public SpeechInputException StartError { get; set; }

        public List<ScriptedRecording> Recordings { get; } = new List<ScriptedRecording>();

        public ScriptedRecording Current => Recordings.Count == 0 ? null : Recordings[Recordings.Count - 1];

        public IProgress<string> Partials { get; private set; }

        public int StartRequests { get; private set; }

        public Task<ISpeechRecording> StartRecordingAsync(IProgress<string> partialTranscripts, CancellationToken cancellationToken)
        {
            StartRequests++;
            if (StartError != null)
            {
                var error = StartError;
                StartError = null;
                throw error;
            }

            Partials = partialTranscripts;
            var recording = new ScriptedRecording();
            Recordings.Add(recording);
            return Task.FromResult<ISpeechRecording>(recording);
        }
    }

    internal sealed class ScriptedRecording : ISpeechRecording
    {
        private readonly TaskCompletionSource<bool> interrupted = new TaskCompletionSource<bool>();
        private readonly TaskCompletionSource<string> transcript = new TaskCompletionSource<string>();

        public Task Interrupted => interrupted.Task;

        public bool FinishRequested { get; private set; }

        public bool Disposed { get; private set; }

        public Task<string> FinishAsync(CancellationToken cancellationToken)
        {
            FinishRequested = true;
            return transcript.Task;
        }

        public void Complete(string text) => transcript.TrySetResult(text);

        public void Fail(SpeechInputError error) => transcript.TrySetException(new SpeechInputException(error, "scripted"));

        public void Interrupt(SpeechInputError error) => interrupted.TrySetException(new SpeechInputException(error, "scripted"));

        public void Dispose() => Disposed = true;
    }
}
