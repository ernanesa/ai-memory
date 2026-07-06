using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Polly;
using Polly.Retry;

namespace AiMemory.Services
{
    public sealed class OllamaService(string baseUrl, string model)
    {
        private readonly HttpClient _http = new(new SocketsHttpHandler { UseCookies = false })
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
        };

        private static readonly ResiliencePipeline RetryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();

        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            return await RetryPipeline.ExecuteAsync(async token =>
            {
                var response = await _http.PostAsJsonAsync("api/embeddings", new { model, prompt = text }, token);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(token);
                    throw new HttpRequestException(
                        $"Ollama embedding request failed with {(int)response.StatusCode} {response.ReasonPhrase} " +
                        $"for model '{model}' and input length {text.Length}. Response: {body}");
                }
                var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: token);
                return payload?.Embedding ?? throw new InvalidOperationException("Ollama did not return an embedding.");
            }, ct);
        }

        public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
        {
            if (inputs.Count == 0)
            {
                return Array.Empty<float[]>();
            }

            if (inputs.Count == 1)
            {
                return new[] { await EmbedAsync(inputs[0], ct) };
            }

            try
            {
                return await RetryPipeline.ExecuteAsync(async token =>
                {
                    var response = await _http.PostAsJsonAsync("api/embed", new { model, input = inputs }, token);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        throw new EmbedBatchNotSupportedException();
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(token);
                        throw new HttpRequestException(
                            $"Ollama embed batch request failed with {(int)response.StatusCode} {response.ReasonPhrase} " +
                            $"for model '{model}' and batch size {inputs.Count}. Response: {body}");
                    }
                    var payload = await response.Content.ReadFromJsonAsync<EmbedBatchResponse>(cancellationToken: token);
                    return (IReadOnlyList<float[]>?)payload?.Embeddings
                        ?? throw new InvalidOperationException("Ollama did not return embeddings for the batch.");
                }, ct);
            }
            catch (EmbedBatchNotSupportedException)
            {
                var tasks = inputs.Select(i => EmbedAsync(i, ct)).ToArray();
                return await Task.WhenAll(tasks);
            }
        }

        public async Task<string> GenerateJsonAsync(string prompt, CancellationToken ct = default)
        {
            return await RetryPipeline.ExecuteAsync(async token =>
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
                var response = await _http.PostAsJsonAsync("api/generate", request, token);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(token);
                    throw new HttpRequestException(
                        $"Ollama generate request failed with {(int)response.StatusCode} {response.ReasonPhrase} " +
                        $"for model '{model}' and prompt length {prompt.Length}. Response: {body}");
                }

                var payload = await response.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken: token);
                var generated = payload?.Response;
                if (string.IsNullOrWhiteSpace(generated))
                {
                    throw new InvalidOperationException("Ollama did not return generated JSON.");
                }

                return generated;
            }, ct);
        }

        private sealed record EmbeddingResponse([property: JsonPropertyName("embedding")] float[] Embedding);

        private sealed record EmbedBatchResponse([property: JsonPropertyName("embeddings")] float[][] Embeddings);

        private sealed record GenerateResponse([property: JsonPropertyName("response")] string Response);

        private sealed class EmbedBatchNotSupportedException : Exception { }
    }
}