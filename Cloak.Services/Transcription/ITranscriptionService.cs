using System;

namespace Cloak.Services.Transcription
{
    public interface ITranscriptionService
    {
        event EventHandler<string>? TranscriptReceived;
        void PushAudio(ReadOnlyMemory<float> samples);
        void Flush();
    }
}

