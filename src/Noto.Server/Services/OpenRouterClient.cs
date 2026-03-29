using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Noto.Server.Services;

public class OpenRouterClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly ILogger<OpenRouterClient> _log;

    public OpenRouterClient(IConfiguration config, IHttpClientFactory httpFactory, ILogger<OpenRouterClient> log)
    {
        _http = httpFactory.CreateClient("OpenRouter");
        _apiKey = config["OpenRouter:ApiKey"] ?? "";
        _defaultModel = config["OpenRouter:ChatModel"] ?? "anthropic/claude-sonnet-4-6";
        _log = log;
    }

    public record ChatMessage(string Role, string Content);

    public async IAsyncEnumerable<string> StreamChat(
        List<ChatMessage> messages,
        string? model = null,
        double temperature = 0.4,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var payload = new
        {
            model = model ?? _defaultModel,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            temperature,
            stream = true,
        };

        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage? response = null;
        string? errorMsg = null;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "OpenRouter request failed");
            errorMsg = ex.Message;
        }

        if (errorMsg != null)
        {
            yield return $"\n\n[Error: {errorMsg}]";
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var delta = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("delta");
                if (delta.TryGetProperty("content", out var content))
                    token = content.GetString();
            }
            catch { }

            if (token != null)
                yield return token;
        }
    }

    public async Task<string> Chat(List<ChatMessage> messages, string? model = null, double temperature = 0.4)
    {
        var sb = new StringBuilder();
        await foreach (var token in StreamChat(messages, model, temperature))
        {
            sb.Append(token);
        }
        return sb.ToString();
    }
}
