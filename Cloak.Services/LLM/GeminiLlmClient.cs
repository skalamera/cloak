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
                    new { parts = new[] { new { text =
                        $"You are assisting me live in a job interview.\n" +
                        "Write the exact first-person answer I should speak next, as the candidate (use I/me). " +
                        "Base the answer on my Profile Context and the most recent question in the Conversation Snippet. " +
                        "Make it well‑structured, specific, and complete (4–7 sentences). " +
                        "Directly reference my experience, projects, metrics, and tools from the Profile Context. " +
                        "Prefer quantified impact and concrete examples. " +
                        "ABSOLUTELY DO NOT give advice, meta commentary, coaching, or bullets like 'focus on...'; output ONLY the actual answer to speak, no quotes or prefixes.\n\n" +
                        $"{context}\n\nAnswer:" } } }
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

        public async Task<string> SummarizeAsync(string transcript)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={_apiKey}";
            var payload = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = $"Summarize this interview conversation into:\n- 3-5 bullet highlights\n- 3 action items\n- Risks/objections to prepare for\n\nTranscript:\n{transcript}" } } }
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


