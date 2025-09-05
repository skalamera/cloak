using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cloak.Services.LLM
{
    public sealed class GeminiLlmClient : ILlmClient
    {
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly HttpClient _http = new HttpClient();

        public GeminiLlmClient(string apiKey, string modelName = "gemini-1.5-flash")
        {
            _apiKey = apiKey;
            _modelName = modelName;
        }

        public async Task<string> GetSuggestionAsync(string context)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={_apiKey}";
            var payload = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = $"Given this live meeting snippet, propose one concise suggestion or answer.\nContext: {context}\nResponse:" } } }
                }
            };
            using var resp = await _http.PostAsJsonAsync(url, payload);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var content = candidates[0].GetProperty("content");
                var parts = content.GetProperty("parts");
                if (parts.GetArrayLength() > 0 && parts[0].TryGetProperty("text", out var text))
                    return text.GetString() ?? string.Empty;
            }
            return string.Empty;
        }
    }
}


