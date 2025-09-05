using System.Windows;
using Cloak.Services.Audio;
using Cloak.Services.Transcription;
using Cloak.Services.Assistant;
using System.Windows.Input;
using System.Linq;

namespace Cloak.App
{
    public partial class MainWindow : Window
    {
        private IAudioCaptureService _audioCaptureService;
        private readonly ITranscriptionService _transcriptionService;
        private ITranscriptionService? _micTranscriptionService;
        private ITranscriptionService? _loopTranscriptionService;
        private readonly IAssistantService _assistantService;
        private readonly System.Collections.Generic.List<IAudioCaptureService> _activeCaptures = new();
        private string _loopQuestionCache = string.Empty;
        private System.DateTime _lastLoopSuggestAt = System.DateTime.MinValue;
        private readonly System.TimeSpan _loopSuggestMinInterval = System.TimeSpan.FromSeconds(6);
        // Echo control disabled (was active-speaker gating)

        public MainWindow()
        {
            InitializeComponent();

            _audioCaptureService = new WasapiAudioCaptureService();
            var azureKey = System.Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
            var azureRegion = System.Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
            var biasTerms = new[]
            {
                "Sigma", "SQL", "Power BI", "Freshdesk", "Zendesk", "RingCentral", "Jedana", "CSAT",
                "API integrations", "benchmark", "Support Operations Hub", "Python", "Snowflake", "Redshift"
            };
            if (!string.IsNullOrWhiteSpace(azureKey) && !string.IsNullOrWhiteSpace(azureRegion))
            {
                _transcriptionService = new AzureSpeechTranscriptionService(azureKey!, azureRegion!, biasTerms);
            }
            else
            {
                _transcriptionService = new PlaceholderTranscriptionService();
            }
            var geminiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrWhiteSpace(geminiKey))
            {
                geminiKey = "AIzaSyBwsUUYP4X25zDH_2qjGUXh89VgAFFfKlU"; // TEMP: hardwired for local testing
            }

            if (!string.IsNullOrWhiteSpace(geminiKey))
            {
                var llm = new Cloak.Services.LLM.GeminiLlmClient(geminiKey!);
                var profile = new Cloak.Services.Profile.FileProfileService(System.IO.Directory.GetCurrentDirectory());
                _assistantService = new LlmAssistantService(llm, profile);
            }
            else
            {
                _assistantService = new PlaceholderAssistantService();
            }

            _transcriptionService.TranscriptReceived += OnTranscriptReceived;
            _assistantService.SuggestionReceived += OnSuggestionReceived;

            // Hotkey: Ctrl+Alt+A for Suggest Now
            var gesture = new KeyGesture(Key.A, ModifierKeys.Control | ModifierKeys.Alt);
            var cmd = new RoutedCommand();
            cmd.InputGestures.Add(gesture);
            CommandBindings.Add(new CommandBinding(cmd, (_, __) => _assistantService.ForceSuggest()));
        }

        private async void OnStartClick(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;

            // Select capture sources
            var mode = (CaptureMode.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
            _activeCaptures.Clear();
            if (mode == "System")
            {
                _activeCaptures.Add(new WasapiLoopbackCaptureService());
                _micTranscriptionService = null; // avoid concurrent Azure sessions
                _loopTranscriptionService = null; // rely on UI transcript for question detection
            }
            else if (mode == "Both")
            {
                _activeCaptures.Add(new WasapiAudioCaptureService());
                _activeCaptures.Add(new WasapiLoopbackCaptureService());

                // Separate mic-only transcriber for suggestions
                // Avoid creating additional Azure recognizers (F0 tier allows limited concurrency)
                _micTranscriptionService = null;
                _loopTranscriptionService = null;
            }
            else // Microphone
            {
                _activeCaptures.Add(new WasapiAudioCaptureService());
                // Reuse display transcriber for suggestions
                _micTranscriptionService = _transcriptionService;
                _micTranscriptionService.TranscriptReceived += OnMicTranscriptReceived;
                _loopTranscriptionService = null;
            }

            foreach (var cap in _activeCaptures)
            {
                if (cap is WasapiAudioCaptureService)
                {
                    await cap.StartAsync(sample =>
                    {
                        _transcriptionService.PushAudio(sample);
                        if (!object.ReferenceEquals(_micTranscriptionService, _transcriptionService) && _micTranscriptionService != null)
                            _micTranscriptionService.PushAudio(sample);
                    });
                }
                else // loopback
                {
                    await cap.StartAsync(sample =>
                    {
                        _transcriptionService.PushAudio(sample);
                    });
                }
            }
        }

        private async void OnStopClick(object sender, RoutedEventArgs e)
        {
            StopButton.IsEnabled = false;
            StartButton.IsEnabled = true;

            foreach (var cap in _activeCaptures)
                await cap.StopAsync();
            _transcriptionService.Flush();
            if (_micTranscriptionService != null && !object.ReferenceEquals(_micTranscriptionService, _transcriptionService))
                _micTranscriptionService.Flush();
            if (_loopTranscriptionService != null && !object.ReferenceEquals(_loopTranscriptionService, _transcriptionService))
                _loopTranscriptionService.Flush();

            // Auto-summary using LLM (Gemini if configured)
            var geminiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "AIzaSyBwsUUYP4X25zDH_2qjGUXh89VgAFFfKlU";
            var llm = new Cloak.Services.LLM.GeminiLlmClient(geminiKey);
            var transcript = string.Join("\n", TranscriptItems.Items.Cast<object>().Select(o => o?.ToString() ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(transcript) && transcript.Length > 120)
            {
                TranscriptItems.Items.Add("--- Session Summary (generating)... ---");
                try
                {
                    var summary = await llm.SummarizeAsync(transcript);
                    TranscriptItems.Items.Add("--- Session Summary ---");
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        foreach (var line in summary.Split(new[] {"\r\n", "\n"}, System.StringSplitOptions.None))
                            TranscriptItems.Items.Add(line);
                    }
                    else
                    {
                        TranscriptItems.Items.Add("(No summary returned)");
                    }
                }
                catch (System.Exception ex)
                {
                    TranscriptItems.Items.Add($"(Summary error: {ex.Message})");
                }
            }
        }

        // Previously had RMS-based gating here; removed to improve recognition quality

        private void OnTranscriptReceived(object? sender, string text)
        {
            Dispatcher.Invoke(() =>
            {
                TranscriptItems.Items.Add(text);
            });

            // Heuristic question detection from combined UI transcript (works with single Azure recognizer)
            bool looksLikeQuestion = text.Contains("?") || text.EndsWith("?", System.StringComparison.OrdinalIgnoreCase) ||
                                      text.IndexOf("can you", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      text.IndexOf("tell me", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      text.IndexOf("how do", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (looksLikeQuestion)
            {
                if (System.DateTime.UtcNow - _lastLoopSuggestAt >= _loopSuggestMinInterval)
                {
                    _lastLoopSuggestAt = System.DateTime.UtcNow;
                    var tail = System.Linq.Enumerable.Reverse(TranscriptItems.Items.Cast<object>()).Take(12).Select(o => o?.ToString() ?? string.Empty);
                    var ctx = string.Join("\n", tail);
                    _assistantService.ForceSuggest(ctx);
                }
            }
        }

        private void OnMicTranscriptReceived(object? sender, string text)
        {
            _assistantService.ProcessContext(text);
        }

        private void OnLoopTranscriptReceived(object? sender, string text)
        {
            // Heuristic: trigger on questions, throttle to avoid spam
            if (string.IsNullOrWhiteSpace(text)) return;
            _loopQuestionCache += text + "\n";
            bool looksLikeQuestion = text.Contains("?") || text.EndsWith("?", System.StringComparison.OrdinalIgnoreCase) ||
                                      text.IndexOf("can you", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      text.IndexOf("tell me", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      text.IndexOf("how do", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!looksLikeQuestion) return;
            if (System.DateTime.UtcNow - _lastLoopSuggestAt < _loopSuggestMinInterval) return;
            _lastLoopSuggestAt = System.DateTime.UtcNow;

            // Build short recent context from loopback cache and UI transcript tail
            var tail = System.Linq.Enumerable.Reverse(TranscriptItems.Items.Cast<object>()).Take(10).Select(o => o?.ToString() ?? string.Empty);
            var ctx = string.Join("\n", tail);
            if (string.IsNullOrWhiteSpace(ctx)) ctx = _loopQuestionCache;
            _assistantService.ForceSuggest(ctx);
            _loopQuestionCache = string.Empty;
        }

        private void OnSuggestionReceived(object? sender, string suggestion)
        {
            Dispatcher.Invoke(() =>
            {
                SuggestionItems.Items.Add(suggestion);
            });
        }

        private void OnSuggestNowClick(object sender, RoutedEventArgs e)
        {
            // Use most recent UI transcript lines (which include interviewer) for the question context
            var lastLines = System.Linq.Enumerable.Reverse(TranscriptItems.Items.Cast<object>())
                .Take(12)
                .Select(o => o?.ToString() ?? string.Empty);
            var overrideContext = string.Join("\n", lastLines);
            _assistantService.ForceSuggest(overrideContext);
        }

        private void OnCopySuggestionClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string text)
            {
                Clipboard.SetText(text);
            }
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            SessionExporter.ExportToMarkdown(TranscriptItems, SuggestionItems);
        }
    }
}

