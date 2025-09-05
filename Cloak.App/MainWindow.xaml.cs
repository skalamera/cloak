using System.Windows;
using Cloak.Services.Audio;
using Cloak.Services.Transcription;
using Cloak.Services.Assistant;
using System.Windows.Input;

namespace Cloak.App
{
    public partial class MainWindow : Window
    {
        private IAudioCaptureService _audioCaptureService;
        private readonly ITranscriptionService _transcriptionService;
        private readonly IAssistantService _assistantService;

        public MainWindow()
        {
            InitializeComponent();

            _audioCaptureService = new WasapiAudioCaptureService();
            var azureKey = System.Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
            var azureRegion = System.Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
            if (!string.IsNullOrWhiteSpace(azureKey) && !string.IsNullOrWhiteSpace(azureRegion))
            {
                _transcriptionService = new AzureSpeechTranscriptionService(azureKey!, azureRegion!);
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

            // Select capture source
            var mode = (CaptureMode.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
            if (mode == "System") _audioCaptureService = new WasapiLoopbackCaptureService();
            else if (mode == "Both") _audioCaptureService = new DualAudioCaptureService(new WasapiAudioCaptureService(), new WasapiLoopbackCaptureService());

            await _audioCaptureService.StartAsync(sample =>
            {
                _transcriptionService.PushAudio(sample);
            });
        }

        private async void OnStopClick(object sender, RoutedEventArgs e)
        {
            StopButton.IsEnabled = false;
            StartButton.IsEnabled = true;

            await _audioCaptureService.StopAsync();
            _transcriptionService.Flush();

            // Auto-summary using LLM (Gemini if configured)
            var geminiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "AIzaSyBwsUUYP4X25zDH_2qjGUXh89VgAFFfKlU";
            var llm = new Cloak.Services.LLM.GeminiLlmClient(geminiKey);
            var transcript = string.Join("\n", TranscriptItems.Items);
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                var summary = await llm.SummarizeAsync(transcript);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    TranscriptItems.Items.Add("--- Session Summary ---");
                    foreach (var line in summary.Split(new[] {"\r\n", "\n"}, System.StringSplitOptions.None))
                        TranscriptItems.Items.Add(line);
                }
            }
        }

        private void OnTranscriptReceived(object? sender, string text)
        {
            Dispatcher.Invoke(() =>
            {
                TranscriptItems.Items.Add(text);
                _assistantService.ProcessContext(text);
            });
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
            _assistantService.ForceSuggest();
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

