using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SalesSim.Application;
using UnityEngine;

namespace SalesSim.Infrastructure
{
    /// <summary>
    /// <see cref="IMicrophone"/> on Unity's <see cref="Microphone"/>: the system's default input device records into a
    /// looping clip, which is read every <see cref="ChunkInterval"/> and handed on as mono 16-bit PCM. Runs on the main
    /// thread (Unity's synchronization context continues the awaits there); audio is only held in memory.
    /// </summary>
    public sealed class UnityMicrophone : IMicrophone
    {
        /// <summary>Length of the looping clip; only what was recorded since the last read is sent.</summary>
        private const int ClipSeconds = 10;

        private static readonly TimeSpan ChunkInterval = TimeSpan.FromMilliseconds(100);

        /// <summary>A device that delivers nothing this long is treated as blocked (e.g. Windows' microphone privacy setting).</summary>
        private static readonly TimeSpan SilentDeviceTimeout = TimeSpan.FromSeconds(2);

        public IMicrophoneRecording Start(int sampleRate, Func<byte[], Task> onAudio)
        {
            if (onAudio == null)
            {
                throw new ArgumentNullException(nameof(onAudio));
            }

            if (Microphone.devices.Length == 0)
            {
                throw new SpeechInputException(SpeechInputError.NoMicrophone, "No microphone was found.");
            }

            if (!UnityEngine.Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                throw new SpeechInputException(SpeechInputError.MicrophoneDenied, "The game may not use the microphone.");
            }

            // null = the system's default input device.
            var clip = Microphone.Start(null, true, ClipSeconds, sampleRate);
            if (clip == null || !Microphone.IsRecording(null))
            {
                Microphone.End(null);
                throw new SpeechInputException(SpeechInputError.MicrophoneFailed, "The microphone could not be started.");
            }

            return new Recording(clip, sampleRate, onAudio);
        }

        /// <summary>
        /// Unity's float samples (interleaved, -1..1) as mono signed 16-bit little-endian PCM at <paramref name="targetRate"/>:
        /// the first channel only, linearly resampled when the clip runs at another rate.
        /// </summary>
        public static byte[] ToPcm16(float[] samples, int channels, int sourceRate, int targetRate)
        {
            var frames = samples.Length / Math.Max(1, channels);
            var outputFrames = sourceRate == targetRate ? frames : (int)((long)frames * targetRate / sourceRate);
            var pcm = new byte[outputFrames * 2];
            for (var i = 0; i < outputFrames; i++)
            {
                float value;
                if (sourceRate == targetRate)
                {
                    value = samples[i * channels];
                }
                else
                {
                    var position = (double)i * sourceRate / targetRate;
                    var index = (int)position;
                    var next = Math.Min(index + 1, frames - 1);
                    var fraction = (float)(position - index);
                    value = samples[index * channels] * (1f - fraction) + samples[next * channels] * fraction;
                }

                var sample = (short)Mathf.Clamp(Mathf.RoundToInt(value * short.MaxValue), short.MinValue, short.MaxValue);
                pcm[2 * i] = (byte)sample;
                pcm[2 * i + 1] = (byte)(sample >> 8);
            }

            return pcm;
        }

        private sealed class Recording : IMicrophoneRecording
        {
            private readonly AudioClip clip;
            private readonly int sampleRate;
            private readonly Func<byte[], Task> onAudio;
            private readonly Stopwatch sinceStart = Stopwatch.StartNew();
            private readonly Task loop;
            private int readPosition;
            private bool receivedAudio;
            private bool stopRequested;
            private bool discarded;
            private bool ended;

            public Recording(AudioClip clip, int sampleRate, Func<byte[], Task> onAudio)
            {
                this.clip = clip;
                this.sampleRate = sampleRate;
                this.onAudio = onAudio;
                loop = RunAsync();
            }

            public Task Failed => loop;

            public async Task StopAsync()
            {
                stopRequested = true;
                try
                {
                    await loop;
                }
                finally
                {
                    End();
                }
            }

            public void Dispose()
            {
                discarded = true;
                End();
            }

            private async Task RunAsync()
            {
                while (!stopRequested)
                {
                    await Task.Delay(ChunkInterval);
                    if (discarded)
                    {
                        return;
                    }

                    await DeliverRecordedAsync();
                    if (!Microphone.IsRecording(null))
                    {
                        throw new SpeechInputException(SpeechInputError.MicrophoneFailed, "The microphone stopped recording.");
                    }

                    if (!receivedAudio && sinceStart.Elapsed > SilentDeviceTimeout)
                    {
                        throw new SpeechInputException(SpeechInputError.MicrophoneDenied, "The microphone delivers no audio.");
                    }
                }

                // The rest since the last read: what was said right before Enter.
                if (!discarded)
                {
                    await DeliverRecordedAsync();
                }
            }

            private async Task DeliverRecordedAsync()
            {
                var position = Microphone.GetPosition(null);
                var frames = (position - readPosition + clip.samples) % clip.samples;
                if (frames == 0)
                {
                    return;
                }

                var samples = new float[frames * clip.channels];
                clip.GetData(samples, readPosition); // wraps around the end of the looping clip
                readPosition = position;
                receivedAudio = true;
                await onAudio(ToPcm16(samples, clip.channels, clip.frequency, sampleRate));
            }

            private void End()
            {
                if (ended)
                {
                    return;
                }

                ended = true;
                Microphone.End(null);
                if (UnityEngine.Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(clip);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                }
            }
        }
    }
}
