using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Cloak.Services.Audio
{
    public sealed class DualAudioCaptureService : IAudioCaptureService
    {
        private readonly IAudioCaptureService _mic;
        private readonly IAudioCaptureService _loopback;
        private readonly ConcurrentQueue<float> _micQueue = new();
        private readonly ConcurrentQueue<float> _loopQueue = new();
        private CancellationTokenSource? _cts;
        private Task? _mixerTask;

        public DualAudioCaptureService(IAudioCaptureService mic, IAudioCaptureService loopback)
        {
            _mic = mic;
            _loopback = loopback;
        }

        public async Task StartAsync(Action<ReadOnlyMemory<float>> onSamples)
        {
            // Feed queues from both sources
            await _mic.StartAsync(samples => Enqueue(_micQueue, samples.Span));
            await _loopback.StartAsync(samples => Enqueue(_loopQueue, samples.Span));

            // Mixer at 20ms frames (320 samples @ 16 kHz)
            _cts = new CancellationTokenSource();
            _mixerTask = Task.Run(async () =>
            {
                const int frame = 160; // 10ms frames @16kHz for smoother mixing
                var mixBuffer = new float[frame];
                var micBuffer = new float[frame];
                var loopBuffer = new float[frame];
                var token = _cts.Token;
                while (!token.IsCancellationRequested)
                {
                    int micCount = Dequeue(_micQueue, micBuffer);
                    int loopCount = Dequeue(_loopQueue, loopBuffer);

                    int count = Math.Max(micCount, loopCount);
                    if (count == 0)
                    {
                        await Task.Delay(5, token);
                        continue;
                    }

                    // Duck loopback when mic present
                    float attenuation = micCount > 0 ? 0.7f : 1f; // less aggressive ducking
                    for (int i = 0; i < count; i++)
                    {
                        float m = i < micCount ? micBuffer[i] : 0f;
                        float l = i < loopCount ? loopBuffer[i] * attenuation : 0f;
                        float v = m + l;
                        // prevent clipping
                        if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                        mixBuffer[i] = v;
                    }

                    onSamples(new ReadOnlyMemory<float>(mixBuffer, 0, count));
                }
            }, _cts.Token);
        }

        public async Task StopAsync()
        {
            await _mic.StopAsync();
            await _loopback.StopAsync();
            if (_cts != null)
            {
                _cts.Cancel();
                try { if (_mixerTask != null) await _mixerTask; } catch (OperationCanceledException) { }
                _cts.Dispose();
                _cts = null;
                _mixerTask = null;
            }
        }

        private static void Enqueue(ConcurrentQueue<float> queue, ReadOnlySpan<float> samples)
        {
            for (int i = 0; i < samples.Length; i++) queue.Enqueue(samples[i]);
        }

        private static int Dequeue(ConcurrentQueue<float> queue, float[] buffer)
        {
            int count = 0;
            while (count < buffer.Length && queue.TryDequeue(out var v))
            {
                buffer[count++] = v;
            }
            // Zero remaining
            for (int i = count; i < buffer.Length; i++) buffer[i] = 0f;
            return count;
        }
    }
}


