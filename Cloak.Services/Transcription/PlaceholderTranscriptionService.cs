using System;

namespace Cloak.Services.Transcription
{
    public sealed class PlaceholderTranscriptionService : ITranscriptionService
    {
        public event EventHandler<string>? TranscriptReceived;

        private int _counter;

        public void PushAudio(ReadOnlyMemory<float> samples)
        {
            _counter++;
            if (_counter % 10 == 0)
            {
                TranscriptReceived?.Invoke(this, $"[mock] Heard {_counter * samples.Length} samples...");
            }
        }

        public void Flush() {}
    }
}

