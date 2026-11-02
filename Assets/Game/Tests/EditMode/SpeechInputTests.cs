using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SalesEngine.Voice;
using SalesEngine.Voice.ElevenLabs;
using SalesSim.Application;
using SalesSim.EngineAdapter;
using SalesSim.Infrastructure;

namespace SalesSim.Tests.EditMode
{
    /// <summary>
    /// <see cref="ElevenLabsSpeechInput"/> on the engine's speech-to-text contract, with a fake provider and microphone:
    /// recording flow, discard without commit, empty transcripts, error mapping and configuration. No network, no device.
    /// The explicit tests at the end use the real ElevenLabs API from Unity's Mono runtime.
    /// </summary>
    public sealed class SpeechInputTests
    {
        private FakeProvider provider;
        private FakeMicrophone microphone;
        private ElevenLabsSpeechInput input;

        [SetUp]
        public void SetUp()
        {
            microphone = new FakeMicrophone();
            provider = new FakeProvider { Microphone = microphone };
            input = new ElevenLabsSpeechInput(provider, microphone, "de");
        }

        [Test]
        public async Task Recording_OpensTheTranscriptionFirst_ThenRecords16kHzPcm_InTheConfiguredLanguage()
        {
            using (var recording = await input.StartRecordingAsync(null, CancellationToken.None))
            {
                Assert.That(provider.Request.InputFormat, Is.EqualTo(AudioFormat.Pcm16(16000)));
                Assert.That(provider.Request.LanguageCode, Is.EqualTo("de"));
                Assert.That(microphone.StartedAt, Is.EqualTo(16000));
                Assert.That(provider.Session.OpenedBeforeMicrophone, Is.True, "A rejected key shows before anything is recorded.");

                await microphone.Deliver(new byte[] { 1, 2, 3, 4 });
                Assert.That(provider.Session.Audio.Single(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            }
        }

        [Test]
        public async Task Finish_StopsTheMicrophone_CommitsAndReturnsTheTrimmedTranscript()
        {
            provider.Transcript = "  Guten Tag, ich bin Tomasz.  ";
            var recording = await input.StartRecordingAsync(null, CancellationToken.None);

            var transcript = await recording.FinishAsync(CancellationToken.None);

            Assert.That(transcript, Is.EqualTo("Guten Tag, ich bin Tomasz."));
            Assert.That(microphone.Recording.Stopped, Is.True);
            Assert.That(provider.Session.Completed, Is.True);
            Assert.That(provider.Session.Disposed, Is.True);
            Assert.That(microphone.Recording.Disposed, Is.True);
        }

        [Test]
        public async Task Finish_WithoutSpeech_ReturnsAnEmptyTranscript()
        {
            provider.Transcript = "";
            var recording = await input.StartRecordingAsync(null, CancellationToken.None);

            Assert.That(await recording.FinishAsync(CancellationToken.None), Is.Empty);
        }

        [Test]
        public async Task Dispose_DiscardsTheRecording_WithoutCommit()
        {
            var recording = await input.StartRecordingAsync(null, CancellationToken.None);
            provider.Session.HangingSend = new TaskCompletionSource<bool>();
            var send = microphone.Deliver(new byte[] { 9, 9 });

            recording.Dispose();

            Assert.That(provider.Session.Completed, Is.False, "No commit: no final transcript can ever be read.");
            Assert.That(provider.Session.Disposed, Is.True);
            Assert.That(microphone.Recording.Disposed, Is.True);
            Assert.That(microphone.Recording.Stopped, Is.False);
            Assert.That(provider.Session.LastSendCancelled, Is.True, "Audio still in flight is not sent any more.");
            Assert.That(async () => await send, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(recording.Interrupted.IsCompleted, Is.False, "A discard is not a failure.");
        }

        [TestCase(TranscriptionErrorKind.Authentication, SpeechInputError.Authentication)]
        [TestCase(TranscriptionErrorKind.Network, SpeechInputError.Unreachable)]
        [TestCase(TranscriptionErrorKind.Timeout, SpeechInputError.Timeout)]
        [TestCase(TranscriptionErrorKind.RateLimited, SpeechInputError.Busy)]
        [TestCase(TranscriptionErrorKind.QuotaExceeded, SpeechInputError.QuotaExceeded)]
        [TestCase(TranscriptionErrorKind.ServerError, SpeechInputError.ServiceError)]
        [TestCase(TranscriptionErrorKind.InvalidResponse, SpeechInputError.ServiceError)]
        public void StartFailure_IsMappedToTheGamesError_AndNothingIsRecorded(TranscriptionErrorKind kind, SpeechInputError expected)
        {
            provider.StartError = new TranscriptionException(kind, "scripted");

            var error = Assert.ThrowsAsync<SpeechInputException>(() => input.StartRecordingAsync(null, CancellationToken.None));

            Assert.That(error.Error, Is.EqualTo(expected));
            Assert.That(microphone.StartedAt, Is.Null);
        }

        [Test]
        public void MissingMicrophone_ClosesTheTranscription_AndReportsNoMicrophone()
        {
            microphone.StartError = new SpeechInputException(SpeechInputError.NoMicrophone, "none");

            var error = Assert.ThrowsAsync<SpeechInputException>(() => input.StartRecordingAsync(null, CancellationToken.None));

            Assert.That(error.Error, Is.EqualTo(SpeechInputError.NoMicrophone));
            Assert.That(provider.Session.Disposed, Is.True);
        }

        [Test]
        public async Task FinishFailure_IsMapped_AndReleasesEverything()
        {
            provider.CompleteError = new TranscriptionException(TranscriptionErrorKind.Timeout, "scripted");
            var recording = await input.StartRecordingAsync(null, CancellationToken.None);

            var error = Assert.ThrowsAsync<SpeechInputException>(() => recording.FinishAsync(CancellationToken.None));

            Assert.That(error.Error, Is.EqualTo(SpeechInputError.Timeout));
            Assert.That(provider.Session.Disposed, Is.True);
            Assert.That(microphone.Recording.Disposed, Is.True);
        }

        [Test]
        public async Task LostConnectionWhileRecording_InterruptsTheRecording()
        {
            var recording = await input.StartRecordingAsync(null, CancellationToken.None);

            microphone.Recording.Fail(new TranscriptionException(TranscriptionErrorKind.Network, "lost"));

            // Awaited directly: Unity's NUnit blocks the main thread in Assert.ThrowsAsync, and this continuation
            // (completed on the thread pool) would have to come back to it.
            SpeechInputException error = null;
            try
            {
                await recording.Interrupted;
            }
            catch (SpeechInputException exception)
            {
                error = exception;
            }

            Assert.That(error, Is.Not.Null);
            Assert.That(error.Error, Is.EqualTo(SpeechInputError.Unreachable));
            recording.Dispose();
        }

        [Test]
        public void CancelledStart_RecordsNothing()
        {
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Assert.That(async () => await input.StartRecordingAsync(null, cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
            }

            Assert.That(microphone.StartedAt, Is.Null);
        }

        [Test]
        public async Task PartialTranscripts_ReachTheCaller()
        {
            var partials = new List<string>();
            using (await input.StartRecordingAsync(new SyncProgress(partials.Add), CancellationToken.None))
            {
                provider.Partials.Report("Guten");
                provider.Partials.Report("Guten Tag");
            }

            Assert.That(partials, Is.EqualTo(new[] { "Guten", "Guten Tag" }));
        }

        [Test]
        public void Create_WithoutKey_IsUnavailable_WithAReason_AndLogsWhy()
        {
            var logs = new List<string>();

            var created = ElevenLabsSpeechInput.Create(_ => null, microphone, logs.Add);

            Assert.That(created.IsAvailable, Is.False);
            Assert.That(created.UnavailableReason, Does.Contain(ElevenLabsOptions.ApiKeyVariable));
            Assert.That(logs.Single(), Does.StartWith(ElevenLabsSpeechInput.LogPrefix + "Speech input off"));
            var error = Assert.ThrowsAsync<SpeechInputException>(() => created.StartRecordingAsync(null, CancellationToken.None));
            Assert.That(error.Error, Is.EqualTo(SpeechInputError.NotConfigured));
        }

        [Test]
        public void Create_WithKey_UsesScribeV2Realtime_AndNeverLogsTheKey()
        {
            const string key = "sk_unity_test_key_123456";
            var logs = new List<string>();
            var settings = new Dictionary<string, string> { [ElevenLabsOptions.ApiKeyVariable] = key };

            var created = ElevenLabsSpeechInput.Create(name => settings.TryGetValue(name, out var v) ? v : null, microphone, logs.Add);

            Assert.That(created.IsAvailable, Is.True);
            Assert.That(logs.Single(), Does.Contain("scribe_v2_realtime").And.Contain("language de"));
            Assert.That(string.Join("\n", logs), Does.Not.Contain(key));
        }

        [Test]
        public void Create_WithInvalidSetting_IsUnavailable()
        {
            var settings = new Dictionary<string, string>
            {
                [ElevenLabsOptions.ApiKeyVariable] = "sk_x",
                [ElevenLabsSpeechToTextOptions.LanguageVariable] = "deutsch",
            };

            var created = ElevenLabsSpeechInput.Create(name => settings.TryGetValue(name, out var v) ? v : null, microphone, null);

            Assert.That(created.IsAvailable, Is.False);
        }

        [Test]
        public void UnityMicrophonePcm_IsMono16BitLittleEndian()
        {
            var pcm = UnityMicrophone.ToPcm16(new[] { 0f, 1f, -1f, 0.5f }, 1, 16000, 16000);

            Assert.That(pcm.Length, Is.EqualTo(8));
            Assert.That(BitConverter.ToInt16(pcm, 0), Is.EqualTo(0));
            Assert.That(BitConverter.ToInt16(pcm, 2), Is.EqualTo(short.MaxValue));
            Assert.That(BitConverter.ToInt16(pcm, 4), Is.EqualTo(-short.MaxValue));
            Assert.That(BitConverter.ToInt16(pcm, 6), Is.EqualTo(16384));
        }

        [Test]
        public void UnityMicrophonePcm_TakesTheFirstChannel_AndResamples()
        {
            var stereo = UnityMicrophone.ToPcm16(new[] { 0.5f, -1f, 0.5f, -1f }, 2, 16000, 16000);
            Assert.That(stereo.Length, Is.EqualTo(4));
            Assert.That(BitConverter.ToInt16(stereo, 0), Is.EqualTo(16384));

            var resampled = UnityMicrophone.ToPcm16(new float[48000], 1, 48000, 16000);
            Assert.That(resampled.Length, Is.EqualTo(16000 * 2));
        }

        // ---- Real ElevenLabs from Unity's Mono runtime (costs a few credits; run explicitly). ----

        [Test, Explicit("Calls the real ElevenLabs API.")]
        public async Task Live_RejectedKey_IsReportedAsAuthentication_NotAsNetwork()
        {
            // Mono's WebSocket has no HttpStatusCode: the engine reads the status from the handshake failure instead.
            var live = new ElevenLabsSpeechInput(
                new ElevenLabsSpeechToTextProvider(new ElevenLabsSpeechToTextOptions("sk_invalid_key_for_unity_test")), microphone, "de");

            SpeechInputException error = null;
            try
            {
                (await live.StartRecordingAsync(null, CancellationToken.None)).Dispose();
            }
            catch (SpeechInputException exception)
            {
                error = exception;
            }

            Assert.That(error, Is.Not.Null, "An invalid key must not open a session.");
            TestContext.WriteLine($"{error.Error}: {error.Message}");
            Assert.That(error.Error, Is.EqualTo(SpeechInputError.Authentication));
        }

        [Test, Explicit("Calls the real ElevenLabs API (text-to-speech, then speech-to-text).")]
        public async Task Live_SpokenGerman_IsTranscribed_ThroughTheUnityAdapter()
        {
            var settings = LocalSettings.Load(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), ".env"));
            var tts = new ElevenLabsTextToSpeechProvider(new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan },
                ElevenLabsOptions.FromEnvironment(settings.Get));
            var speech = await tts.SynthesizeAsync(new SpeechSynthesisRequest(
                "Guten Tag, ich möchte Ihnen unsere Webseite vorstellen.", settings.Get(ElevenLabsOptions.CustomerVoiceVariable),
                AudioFormat.Pcm16(ElevenLabsSpeechInput.SampleRate)), CancellationToken.None);
            var live = (ElevenLabsSpeechInput)ElevenLabsSpeechInput.Create(settings.Get, microphone, TestContext.WriteLine);

            var recording = await live.StartRecordingAsync(null, CancellationToken.None);
            for (var offset = 0; offset < speech.Audio.Length; offset += 3200)
            {
                await microphone.Deliver(speech.Audio.Skip(offset).Take(3200).ToArray());
            }

            var transcript = await recording.FinishAsync(CancellationToken.None);

            TestContext.WriteLine($"Transcript: \"{transcript}\"");
            Assert.That(transcript.ToLowerInvariant(), Does.Contain("guten tag").And.Contain("webseite"));
        }

        [Test, Explicit("Records from the real default microphone for two seconds.")]
        public async Task Live_UnityMicrophone_DeliversPcmFromTheDefaultDevice()
        {
            TestContext.WriteLine($"Devices: {string.Join(", ", UnityEngine.Microphone.devices)}");
            var bytes = 0;
            var chunks = 0;
            var recording = new UnityMicrophone().Start(ElevenLabsSpeechInput.SampleRate, chunk =>
            {
                bytes += chunk.Length;
                chunks++;
                return Task.CompletedTask;
            });

            await Task.Delay(TimeSpan.FromSeconds(2));
            await recording.StopAsync();

            TestContext.WriteLine($"{chunks} chunks, {bytes} bytes (~{bytes / 2.0 / ElevenLabsSpeechInput.SampleRate:0.0} s)");
            Assert.That(bytes, Is.GreaterThan(ElevenLabsSpeechInput.SampleRate * 2), "More than one second of 16 kHz PCM.");
        }

        private sealed class SyncProgress : IProgress<string>
        {
            private readonly Action<string> report;

            public SyncProgress(Action<string> report)
            {
                this.report = report;
            }

            public void Report(string value) => report(value);
        }

        private sealed class FakeProvider : ISpeechToTextProvider
        {
            public Exception StartError;
            public Exception CompleteError;
            public string Transcript = "Hallo";
            public TranscriptionRequest Request;
            public FakeSession Session;
            public IProgress<string> Partials;
            public FakeMicrophone Microphone;

            public Task<ITranscriptionSession> StartAsync(
                TranscriptionRequest request, IProgress<string> partialTranscripts, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (StartError != null)
                {
                    throw StartError;
                }

                Request = request;
                Partials = partialTranscripts;
                Session = new FakeSession(this) { OpenedBeforeMicrophone = Microphone.StartedAt == null };
                return Task.FromResult<ITranscriptionSession>(Session);
            }
        }

        private sealed class FakeSession : ITranscriptionSession
        {
            private readonly FakeProvider provider;

            public FakeSession(FakeProvider provider)
            {
                this.provider = provider;
            }

            public List<byte[]> Audio { get; } = new List<byte[]>();

            public bool OpenedBeforeMicrophone { get; set; }

            public bool Completed { get; private set; }

            public bool Disposed { get; private set; }

            public bool LastSendCancelled { get; private set; }

            public TaskCompletionSource<bool> HangingSend { get; set; }

            public async Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken)
            {
                if (HangingSend != null)
                {
                    using (cancellationToken.Register(() =>
                           {
                               LastSendCancelled = true;
                               HangingSend.TrySetCanceled();
                           }))
                    {
                        // Not back to Unity's main thread: the test waits for this send there.
                        await HangingSend.Task.ConfigureAwait(false);
                    }
                }

                Audio.Add(audio.ToArray());
            }

            public Task<string> CompleteAsync(CancellationToken cancellationToken)
            {
                Completed = true;
                if (provider.CompleteError != null)
                {
                    throw provider.CompleteError;
                }

                return Task.FromResult(provider.Transcript);
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return default;
            }
        }

        private sealed class FakeMicrophone : IMicrophone
        {
            private Func<byte[], Task> onAudio;

            public int? StartedAt;
            public Exception StartError;
            public FakeRecording Recording;

            public IMicrophoneRecording Start(int sampleRate, Func<byte[], Task> audio)
            {
                if (StartError != null)
                {
                    throw StartError;
                }

                StartedAt = sampleRate;
                onAudio = audio;
                Recording = new FakeRecording();
                return Recording;
            }

            /// <summary>Hands a chunk on like the real microphone.</summary>
            public Task Deliver(byte[] chunk)
            {
                return onAudio(chunk);
            }
        }

        private sealed class FakeRecording : IMicrophoneRecording
        {
            private readonly TaskCompletionSource<bool> failed = new TaskCompletionSource<bool>();

            public Task Failed => failed.Task;

            public bool Stopped { get; private set; }

            public bool Disposed { get; private set; }

            public Task StopAsync()
            {
                Stopped = true;
                return Task.CompletedTask;
            }

            public void Fail(Exception error) => failed.TrySetException(error);

            public void Dispose() => Disposed = true;
        }
    }
}
