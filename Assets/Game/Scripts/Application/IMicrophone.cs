using System;
using System.Threading.Tasks;

namespace SalesSim.Application
{
    /// <summary>
    /// The game's audio input. Separate from speech-to-text on purpose (like the engine console's microphone): it only
    /// delivers PCM chunks; the speech input feeds them into the transcription. Audio is only held in memory.
    /// </summary>
    public interface IMicrophone
    {
        /// <summary>
        /// Starts recording right away: mono, signed 16-bit little-endian PCM at <paramref name="sampleRate"/>.
        /// <paramref name="onAudio"/> receives the chunks in order, one at a time.
        /// </summary>
        /// <exception cref="SpeechInputException">
        /// <see cref="SpeechInputError.NoMicrophone"/>, <see cref="SpeechInputError.MicrophoneDenied"/> or
        /// <see cref="SpeechInputError.MicrophoneFailed"/>.
        /// </exception>
        IMicrophoneRecording Start(int sampleRate, Func<byte[], Task> onAudio);
    }

    /// <summary>A running recording. Disposing without <see cref="StopAsync"/> discards the rest of the audio.</summary>
    public interface IMicrophoneRecording : IDisposable
    {
        /// <summary>Never completes while recording works; faults when the microphone or <c>onAudio</c> fails.</summary>
        Task Failed { get; }

        /// <summary>Stops recording and delivers the remaining audio.</summary>
        /// <exception cref="Exception">The microphone failed, or whatever <c>onAudio</c> threw.</exception>
        Task StopAsync();
    }
}
