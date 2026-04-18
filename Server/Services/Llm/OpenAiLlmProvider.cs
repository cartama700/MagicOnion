using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.Services.Llm;

// OpenAI Chat Completions with SSE streaming.
// API 키가 설정돼 있어야 동작 — 기본값은 MockLlmProvider.
public sealed class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly LlmOptions _opt;

    public OpenAiLlmProvider(IHttpClientFactory factory, LlmOptions opt)
    {
        _opt = opt;
        _http = factory.CreateClient("openai");
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
    }

    public string Name => "openai";

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
            throw new InvalidOperationException(
                "Llm:ApiKey 가 비어 있습니다. appsettings 의 Llm:Provider 를 mock 으로 두거나 ApiKey 를 설정하세요.");

        var payload = new ChatRequest
        {
            Model = _opt.Model,
            Stream = true,
            MaxTokens = _opt.MaxTokens,
            Temperature = _opt.Temperature,
            Messages =
            [
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt),
            ],
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (line.Length == 0 || !line.StartsWith("data:")) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;

            ChatChunk? chunk;
            try { chunk = JsonSerializer.Deserialize<ChatChunk>(data); }
            catch (JsonException) { continue; }

            var token = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(token)) yield return token;
        }
    }

    private sealed record ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; init; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; init; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; init; }
        [JsonPropertyName("temperature")] public double Temperature { get; init; }
        [JsonPropertyName("messages")] public ChatMessage[] Messages { get; init; } = [];
    }
    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
    private sealed record ChatChunk(
        [property: JsonPropertyName("choices")] ChunkChoice[]? Choices);
    private sealed record ChunkChoice(
        [property: JsonPropertyName("delta")] ChunkDelta? Delta);
    private sealed record ChunkDelta(
        [property: JsonPropertyName("content")] string? Content);
}
