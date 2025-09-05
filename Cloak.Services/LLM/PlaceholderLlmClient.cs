using System.Threading.Tasks;

namespace Cloak.Services.LLM
{
    public sealed class PlaceholderLlmClient : ILlmClient
    {
        public Task<string> GetSuggestionAsync(string context)
        {
            return Task.FromResult($"[mock LLM] Based on: {context}");
        }

        public Task<string> SummarizeAsync(string transcript)
        {
            return Task.FromResult("[mock summary] Highlights, action items, and risks.");
        }
    }
}

