using System;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace Cloak.Services.Transcription
{
    public sealed class AzureSpeechTranscriptionService : ITranscriptionService, IAsyncDisposable
    {
        public event EventHandler<string>? TranscriptReceived;

        private readonly SpeechConfig _config;
        private readonly PushAudioInputStream _audioStream;
        private readonly AudioConfig _audioConfig;
        private readonly SpeechRecognizer _recognizer;

        public AzureSpeechTranscriptionService(string subscriptionKey, string region)
        {
            _config = SpeechConfig.FromSubscription(subscriptionKey, region);
            _config.SpeechRecognitionLanguage = "en-US";
            _audioStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
            _audioConfig = AudioConfig.FromStreamInput(_audioStream);
            _recognizer = new SpeechRecognizer(_config, _audioConfig);

            // Emit final results only to reduce UI spam
            _recognizer.Recognizing += (_, _) => { };

            _recognizer.Recognized += (_, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                    TranscriptReceived?.Invoke(this, e.Result.Text);
            };

            _recognizer.Canceled += (_, e) =>
            {
                TranscriptReceived?.Invoke(this, $"[asr canceled] {e.Reason}: {e.ErrorDetails}");
            };

            _recognizer.SessionStopped += (_, _) =>
            {
                TranscriptReceived?.Invoke(this, "[asr stopped]");
            };

            _ = _recognizer.StartContinuousRecognitionAsync();
        }

        public void PushAudio(ReadOnlyMemory<float> samples)
        {
            var buffer = new byte[samples.Length * 2];
            int outIndex = 0;
            foreach (var s in samples.Span)
            {
                var clamped = Math.Clamp(s, -1f, 1f);
                short sample16 = (short)(clamped * 32767);
                buffer[outIndex++] = (byte)(sample16 & 0xFF);
                buffer[outIndex++] = (byte)((sample16 >> 8) & 0xFF);
            }
            _audioStream.Write(buffer);
        }

        public void Flush() { }

        public async ValueTask DisposeAsync()
        {
            await _recognizer.StopContinuousRecognitionAsync();
            _recognizer.Dispose();
            _audioConfig.Dispose();
            _audioStream.Dispose();
        }
    }
}


