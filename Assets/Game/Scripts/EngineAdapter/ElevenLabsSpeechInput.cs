using System;
using System.Threading;
using System.Threading.Tasks;
using SalesEngine.Voice;
using SalesEngine.Voice.ElevenLabs;
using SalesSim.Application;

namespace SalesSim.EngineAdapter
{
    /// <summary>
    /// <see cref="ISpeechInput"/> on the engine's speech-to-text (<see cref="ISpeechToTextProvider"/>, by default
    /// <see cref="ElevenLabsSpeechToTextProvider"/> with Scribe v2 Realtime). Same flow as the engine console's
    /// push-to-talk: open the transcription first (a rejected key shows before anything is recorded), stream the
    /// microphone into it, then commit for the final transcript, or discard without commit. Only translates; the engine
    /// adapter decides models, protocol and errors.
    /// </summary>
    public sealed class ElevenLabsSpeechInput : ISpeechInput
    {
        public const string LogPrefix = "[SalesVoice] ";

        /// <summary>What the microphone records and the provider is sent: mono 16-bit PCM at 16 kHz, like the console.</summary>
        public const int SampleRate = 16000;

        private readonly ISpeechToTextProvider provider;
        private readonly IMicrophone microphone;
        private readonly string languageCode;

        public ElevenLabsSpeechInput(ISpeechToTextProvider provider, IMicrophone microphone, string languageCode)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.microphone = microphone ?? throw new ArgumentNullException(nameof(microphone));
            this.languageCode = languageCode;
        }

        public bool IsAvailable => true;

        public string UnavailableReason => string.Empty;

        /// <summary>
        /// ElevenLabs speech input when <c>SALESSIM_ELEVENLABS_API_KEY</c> is set (environment or .env), read through the
        /// engine's <see cref="ElevenLabsSpeechToTextOptions"/> (key, <c>SALESSIM_ELEVENLABS_STT_MODEL</c>,
        /// <c>SALESSIM_ELEVENLABS_STT_LANGUAGE</c>, timeout); otherwise an unavailable one, with the reason logged.
        /// </summary>
        public static ISpeechInput Create(Func<string, string> getSetting, IMicrophone microphone, Action<string> log)
        {
            if (getSetting == null)
            {
                throw new ArgumentNullException(nameof(getSetting));
            }

            ElevenLabsSpeechToTextOptions options;
            try
            {
                options = ElevenLabsSpeechToTextOptions.FromEnvironment(getSetting);
            }
            catch (VoiceConfigurationException exception)
            {
                log?.Invoke($"{LogPrefix}Speech input off: {exception.Message}");
                return new UnavailableSpeechInput(string.IsNullOrWhiteSpace(getSetting(ElevenLabsOptions.ApiKeyVariable))
                    ? $"Spracheingabe nicht eingerichtet ({ElevenLabsOptions.ApiKeyVariable} fehlt)."
                    : "Spracheingabe falsch eingerichtet (siehe Console).");
            }

            log?.Invoke($"{LogPrefix}Speech input: ElevenLabs {options.Model}, language {options.LanguageCode ?? "auto-detect"}, "
                        + $"timeout {options.Timeout.TotalSeconds:0}s");
            return new ElevenLabsSpeechInput(new ElevenLabsSpeechToTextProvider(options), microphone, options.LanguageCode);
        }

        public async Task<ISpeechRecording> StartRecordingAsync(IProgress<string> partialTranscripts, CancellationToken cancellationToken)
        {
            ITranscriptionSession session;
            try
            {
                session = await provider.StartAsync(
                    new TranscriptionRequest(AudioFormat.Pcm16(SampleRate), languageCode), partialTranscripts, cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                throw Map(exception);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await DisposeQuietly(session);
                throw new OperationCanceledException(cancellationToken);
            }

            // Once discarded, audio still in flight is not sent any more.
            var discarded = new CancellationTokenSource();
            IMicrophoneRecording recording;
            try
            {
                recording = microphone.Start(SampleRate, chunk => session.SendAudioAsync(chunk, discarded.Token));
            }
            catch (Exception exception)
            {
                await DisposeQuietly(session);
                throw Map(exception);
            }

            return new Recording(session, recording, discarded);
        }

        /// <summary>The engine's and the microphone's errors as the game's <see cref="SpeechInputError"/>.</summary>
        public static SpeechInputException Map(Exception exception)
        {
            switch (exception)
            {
                case SpeechInputException speech:
                    return speech;
                case TranscriptionException transcription:
                    return new SpeechInputException(Map(transcription.Kind), transcription.Message, transcription);
                case VoiceConfigurationException configuration:
                    return new SpeechInputException(SpeechInputError.NotConfigured, configuration.Message, configuration);
                default:
                    // A provider or driver bug must not end the conversation either. Only the type: the message is not ours to vouch for.
                    return new SpeechInputException(
                        SpeechInputError.ServiceError, $"speech input failed ({exception.GetType().Name})", exception);
            }
        }

        private static SpeechInputError Map(TranscriptionErrorKind kind)
        {
            switch (kind)
            {
                case TranscriptionErrorKind.Authentication: return SpeechInputError.Authentication;
                case TranscriptionErrorKind.RateLimited: return SpeechInputError.Busy;
                case TranscriptionErrorKind.QuotaExceeded: return SpeechInputError.QuotaExceeded;
                case TranscriptionErrorKind.Network: return SpeechInputError.Unreachable;
                case TranscriptionErrorKind.Timeout: return SpeechInputError.Timeout;
                default: return SpeechInputError.ServiceError;
            }
        }

        private static async Task DisposeQuietly(ITranscriptionSession session)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception)
            {
                // Closing is a courtesy; nothing of this session is used any more.
            }
        }

        private sealed class Recording : ISpeechRecording
        {
            private readonly ITranscriptionSession session;
            private readonly IMicrophoneRecording microphone;
            private readonly CancellationTokenSource discarded;
            private readonly TaskCompletionSource<bool> interrupted = new TaskCompletionSource<bool>();
            private bool finishing;
            private bool released;

            public Recording(ITranscriptionSession session, IMicrophoneRecording microphone, CancellationTokenSource discarded)
            {
                this.session = session;
                this.microphone = microphone;
                this.discarded = discarded;
                microphone.Failed.ContinueWith(
                    failed =>
                    {
                        // Read in any case, so a failure after the end is observed and never reported as unobserved.
                        var error = failed.Exception?.GetBaseException();
                        if (error != null && !finishing && !released)
                        {
                            interrupted.TrySetException(Map(error));
                        }
                    },
                    TaskScheduler.Default);
                interrupted.Task.ContinueWith(
                    failed => _ = failed.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }

            public Task Interrupted => interrupted.Task;

            public async Task<string> FinishAsync(CancellationToken cancellationToken)
            {
                if (finishing || released)
                {
                    throw new InvalidOperationException("The recording is already finished or discarded.");
                }

                finishing = true;
                try
                {
                    await microphone.StopAsync();
                    var transcript = await session.CompleteAsync(cancellationToken);
                    return (transcript ?? string.Empty).Trim();
                }
                catch (Exception exception) when (!(exception is OperationCanceledException))
                {
                    throw Map(exception);
                }
                finally
                {
                    Release();
                }
            }

            /// <summary>
            /// Discards: no commit and no final transcript is ever read. Cancelling first keeps a send stuck on the network
            /// from blocking; disposing the microphone and the session then drops all audio and any late transcript.
            /// </summary>
            public void Dispose()
            {
                if (released)
                {
                    return;
                }

                discarded.Cancel();
                Release();
            }

            private void Release()
            {
                if (released)
                {
                    return;
                }

                released = true;
                microphone.Dispose();
                _ = DisposeQuietly(session);
            }
        }
    }
}
