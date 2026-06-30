using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AiMemory.Services
{
    public sealed class OllamaService(string baseUrl, string model)
    {
        private readonly HttpClient _http = new(new SocketsHttpHandler { UseCookies = false })
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
        };

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            var response = await _http.PostAsJsonAsync("api/embeddings", new { model, prompt = text }, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Ollama embedding request failed with {(int)response.StatusCode} {response.ReasonPhrase} " +
                    $"for model '{model}' and input length {text.Length}. Response: {body}");
            }
            var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);
            return payload?.Embedding ?? throw new InvalidOperationException("Ollama did not return an embedding.");
        }

        public async Task<string> GenerateJsonAsync(string prompt, CancellationToken ct = default)
        {
            var request = new
            {
                model,
                prompt,
                stream = false,
                format = "json",
                options = new
                {
                    temperature = 0
                }
            };
            var response = await _http.PostAsJsonAsync("api/generate", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Ollama generate request failed with {(int)response.StatusCode} {response.ReasonPhrase} " +
                    $"for model '{model}' and prompt length {prompt.Length}. Response: {body}");
            }

            var payload = await response.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken: ct);
            var generated = payload?.Response;
            if (string.IsNullOrWhiteSpace(generated))
            {
                throw new InvalidOperationException("Ollama did not return generated JSON.");
            }

            return generated;
        }

        private sealed record EmbeddingResponse([property: JsonPropertyName("embedding")] float[] Embedding);

        private sealed record GenerateResponse([property: JsonPropertyName("response")] string Response);
    }
}
