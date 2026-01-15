using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace qlthucung.Services.chat
{
    // Robust OpenAI embedding service suitable for singleton registration.
    // Requires configuration: "OpenAI:ApiKey" (and optional "OpenAI:BaseUrl").
    public class OpenAIEmbeddingService : IEmbeddingService, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly ILogger<OpenAIEmbeddingService> _logger;
        private bool _disposed;

        public OpenAIEmbeddingService(IConfiguration _configuration, ILogger<OpenAIEmbeddingService> logger = null)
        {
            _logger = logger;
            _apiKey = _configuration["ChatAI:ApiKey"] ?? throw new InvalidOperationException("Missing OpenAI:ApiKey configuration");
            _baseUrl = _configuration["App:BaseApiUrl"] ?? "http://localhost:5172";

            _http = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            // keep default headers minimal and add Authorization per-request for clarity
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // Returns float[] embedding or null on failure.
        public async Task<float[]> GetEmbeddingAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // payload uses the recommended small embedding model; change if needed via config
            var payload = new
            {
                model = "text-embedding-3-small",
                input = text
            };

            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            // simple retry for transient server/ratelimit errors
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var resp = await _http.SendAsync(req);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        _logger?.LogWarning("OpenAI embeddings request failed (status {Status}) attempt {Attempt}: {Body}", (int)resp.StatusCode, attempt, body);

                        // retry on 429 or 5xx
                        if ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500)
                        {
                            await Task.Delay(250 * attempt);
                            continue;
                        }

                        return null;
                    }

                    using var stream = await resp.Content.ReadAsStreamAsync();
                    using var doc = await JsonDocument.ParseAsync(stream);

                    if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.GetArrayLength() == 0)
                        return null;

                    var first = dataEl[0];
                    if (!first.TryGetProperty("embedding", out var embEl))
                        return null;

                    var len = embEl.GetArrayLength();
                    var result = new float[len];
                    int i = 0;
                    foreach (var v in embEl.EnumerateArray())
                    {
                        // embedding numbers may be double; convert safely
                        result[i++] = v.GetSingle();
                    }

                    return result;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    _logger?.LogWarning(ex, "OpenAI embedding attempt {Attempt} failed, retrying...", attempt);
                    await Task.Delay(250 * attempt);
                    continue;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "OpenAI embedding failed");
                    return null;
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _http?.Dispose();
            _disposed = true;
        }
    }
}