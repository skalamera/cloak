using System;
using System.Threading;
using System.Threading.Tasks;
using Cloak.Services.LLM;
using Cloak.Services.Profile;

namespace Cloak.Services.Assistant
{
    public sealed class LlmAssistantService : IAssistantService
    {
        public event EventHandler<string>? SuggestionReceived;
        private readonly ILlmClient _llmClient;
        private readonly IProfileService? _profileService;

        private string _buffer = string.Empty;
        private string _lastQuestionSnippet = string.Empty;
        private int _charsSinceLast = 0;
        private DateTime _lastSuggestionAt = DateTime.MinValue;
        private string _lastSuggestion = string.Empty;
        private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(7);

        public LlmAssistantService(ILlmClient llmClient, IProfileService? profileService = null)
        {
            _llmClient = llmClient;
            _profileService = profileService;
        }

        public async void ProcessContext(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _buffer += text + "\n";
            _charsSinceLast += text.Length;
            if (_charsSinceLast < 120) return;
            if (DateTime.UtcNow - _lastSuggestionAt < _minInterval) return;
            _charsSinceLast = 0;
            var snapshot = _buffer.Length > 3000 ? _buffer[^3000..] : _buffer;
            var context = snapshot;
            if (_profileService != null)
            {
                var profile = await _profileService.GetProfileContextAsync();
                context = $"Profile Context (ground truth):\n{profile}\n\nConversation Snippet (latest turns):\n{snapshot}";
            }
            _lastQuestionSnippet = snapshot; // cache used for SuggestNow
            var suggestion = (await _llmClient.GetSuggestionAsync(context)).Trim();
            if (string.IsNullOrWhiteSpace(suggestion)) return;
            if (string.Equals(suggestion, _lastSuggestion, StringComparison.OrdinalIgnoreCase)) return;
            _lastSuggestion = suggestion;
            _lastSuggestionAt = DateTime.UtcNow;
            SuggestionReceived?.Invoke(this, suggestion);
        }

        public async void ForceSuggest(string? overrideContext = null)
        {
            var snapshot = !string.IsNullOrWhiteSpace(overrideContext) ? overrideContext! : (_lastQuestionSnippet.Length > 0 ? _lastQuestionSnippet : (_buffer.Length > 3000 ? _buffer[^3000..] : _buffer));
            var context = snapshot;
            if (_profileService != null)
            {
                var profile = await _profileService.GetProfileContextAsync();
                context = $"Profile Context (ground truth):\n{profile}\n\nConversation Snippet (latest turns):\n{snapshot}";
            }
            var suggestion = (await _llmClient.GetSuggestionAsync(context)).Trim();
            if (string.IsNullOrWhiteSpace(suggestion)) return;
            if (string.Equals(suggestion, _lastSuggestion, StringComparison.OrdinalIgnoreCase)) return;
            _lastSuggestion = suggestion;
            _lastSuggestionAt = DateTime.UtcNow;
            SuggestionReceived?.Invoke(this, suggestion);
        }
    }
}


