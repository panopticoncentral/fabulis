using System.Net.Http.Headers;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class OpenRouterService(IHttpClientFactory httpClientFactory, IServiceProvider services)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<string> ChatAsync(string model, string systemPrompt, string userMessage,
        double temperature = 0.7, double? topP = null, int? maxTokens = null)
    {
        var apiKey = await GetSettingAsync("OpenRouterApiKey")
            ?? throw new InvalidOperationException("OpenRouter API key is not configured. Set it in Settings.");

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            ["temperature"] = temperature
        };

        if (topP.HasValue)
            requestBody["top_p"] = topP.Value;
        if (maxTokens.HasValue)
            requestBody["max_tokens"] = maxTokens.Value;

        var response = await client.PostAsJsonAsync(
            "https://openrouter.ai/api/v1/chat/completions",
            requestBody,
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(string model, string systemPrompt,
        List<DraftMessage> messages, double temperature = 0.7, double? topP = null, int? maxTokens = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = await GetSettingAsync("OpenRouterApiKey")
            ?? throw new InvalidOperationException("OpenRouter API key is not configured. Set it in Settings.");

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var apiMessages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        foreach (var msg in messages)
        {
            apiMessages.Add(new
            {
                role = msg.Role == MessageRole.Prompt ? "user" : "assistant",
                content = msg.Content
            });
        }

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = apiMessages,
            ["temperature"] = temperature,
            ["stream"] = true
        };

        if (topP.HasValue)
            requestBody["top_p"] = topP.Value;
        if (maxTokens.HasValue)
            requestBody["max_tokens"] = maxTokens.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions)
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            string? chunk = null;
            try
            {
                var json = JsonDocument.Parse(data);
                var delta = json.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("delta");

                if (delta.TryGetProperty("content", out var contentElement))
                    chunk = contentElement.GetString();
            }
            catch (JsonException)
            {
                // Skip malformed chunks
            }

            if (chunk is not null)
                yield return chunk;
        }
    }

    public async Task<List<ModelInfo>> GetModelsAsync()
    {
        var client = httpClientFactory.CreateClient();
        var json = await client.GetFromJsonAsync<JsonElement>("https://openrouter.ai/api/v1/models");
        var models = new List<ModelInfo>();

        foreach (var item in json.GetProperty("data").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString();
            var name = item.GetProperty("name").GetString();
            if (id is not null && name is not null)
                models.Add(new ModelInfo { Id = id, Name = name });
        }

        return models.OrderBy(m => m.Id).ToList();
    }

    public async Task<string?> GetSettingAsync(string key)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
        var setting = await db.AppSettings.FindAsync(key);
        return setting?.Value;
    }
}

public class ModelInfo
{
    public required string Id { get; set; }
    public required string Name { get; set; }
}
