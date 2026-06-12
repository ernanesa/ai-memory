using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AiMemory.Services;

public sealed class OllamaService(string baseUrl, string model)
{
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseCookies = false })
    {
        BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
    };

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/embeddings", new { model, prompt = text }, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);
        return payload?.Embedding ?? throw new InvalidOperationException("Ollama did not return an embedding.");
    }

    private sealed record EmbeddingResponse([property: JsonPropertyName("embedding")] float[] Embedding);
}
