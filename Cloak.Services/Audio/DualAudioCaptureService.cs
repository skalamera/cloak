using System;
using System.Buffers;
using System.Threading.Tasks;

namespace Cloak.Services.Audio
{
    public sealed class DualAudioCaptureService : IAudioCaptureService
    {
        private readonly IAudioCaptureService _mic;
        private readonly IAudioCaptureService _loopback;

        public DualAudioCaptureService(IAudioCaptureService mic, IAudioCaptureService loopback)
        {
            _mic = mic;
            _loopback = loopback;
        }

        public async Task StartAsync(Action<ReadOnlyMemory<float>> onSamples)
        {
            // Simple ducking: reduce loopback amplitude when mic energy is high
            float attenuation = 0.35f;
            float micRms = 0f;

            void OnMic(ReadOnlyMemory<float> s)
            {
                micRms = ComputeRms(s.Span);
                onSamples(s);
            }

            void OnLoop(ReadOnlyMemory<float> s)
            {
                if (micRms > 0.02f)
                {
                    var buf = s.ToArray();
                    for (int i = 0; i < buf.Length; i++) buf[i] *= attenuation;
                    onSamples(buf);
                }
                else onSamples(s);
            }

            await _mic.StartAsync(OnMic);
            await _loopback.StartAsync(OnLoop);
        }

        public async Task StopAsync()
        {
            await _mic.StopAsync();
            await _loopback.StopAsync();
        }

        private static float ComputeRms(ReadOnlySpan<float> samples)
        {
            double sum = 0;
            for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
            return (float)Math.Sqrt(sum / Math.Max(1, samples.Length));
        }
    }
}


