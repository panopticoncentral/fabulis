using System.Net.Http.Headers;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class OpenRouterService(IHttpClientFactory httpClientFactory, IServiceProvider services, VaultService vault, ILogger<OpenRouterService> log)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Maps a reasoning effort onto OpenRouter's <c>reasoning</c> field, or
    /// null to leave the field off the request entirely and let the model use
    /// its own default.
    /// </summary>
    private static object? BuildReasoningPayload(ReasoningEffort? reasoning) => reasoning switch
    {
        null => null,
        ReasoningEffort.Off => new { enabled = false },
        { } effort => new { effort = effort.ToString().ToLowerInvariant() }
    };

    /// <summary>
    /// Renders the sampling knobs actually being sent, skipping the ones left
    /// unset, so the log line shows the request as OpenRouter will see it.
    /// </summary>
    private static string DescribeSettings(double temperature, double? topP, int? maxTokens,
        double? minP, int? topK, double? topA, ReasoningEffort? reasoning)
    {
        // Invariant throughout: a comma-decimal locale must not make the log
        // disagree with the JSON actually on the wire.
        var parts = new List<string> { FormattableString.Invariant($"temperature={temperature}") };
        if (topP.HasValue) parts.Add(FormattableString.Invariant($"top_p={topP.Value}"));
        if (maxTokens.HasValue) parts.Add(FormattableString.Invariant($"max_tokens={maxTokens.Value}"));
        if (minP.HasValue) parts.Add(FormattableString.Invariant($"min_p={minP.Value}"));
        if (topK.HasValue) parts.Add(FormattableString.Invariant($"top_k={topK.Value}"));
        if (topA.HasValue) parts.Add(FormattableString.Invariant($"top_a={topA.Value}"));
        parts.Add($"reasoning={(reasoning.HasValue ? reasoning.Value.ToString().ToLowerInvariant() : "default")}");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Reports the model OpenRouter actually served, which can differ from the
    /// one asked for when the request used an auto-routed or ":floor" variant.
    /// A match is only worth a debug line, since the entry log already named it.
    /// </summary>
    private bool LogResolvedModel(string requested, JsonElement root)
    {
        if (!root.TryGetProperty("model", out var served) || served.ValueKind != JsonValueKind.String)
            return false;

        var resolved = served.GetString();
        if (string.IsNullOrEmpty(resolved))
            return false;

        if (string.Equals(resolved, requested, StringComparison.Ordinal))
            log.LogDebug("OpenRouter served model={Model}, as requested.", resolved);
        else
            log.LogInformation("OpenRouter routed {Requested} to model={Model}.", requested, resolved);

        return true;
    }

    public async Task<string> ChatAsync(string model, string systemPrompt, string userMessage,
        double temperature = 0.7, double? topP = null, int? maxTokens = null,
        double? minP = null, int? topK = null, double? topA = null,
        ReasoningEffort? reasoning = null)
    {
        log.LogInformation("OpenRouter completion: model={Model} ({Settings})",
            model, DescribeSettings(temperature, topP, maxTokens, minP, topK, topA, reasoning));

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
        if (minP.HasValue)
            requestBody["min_p"] = minP.Value;
        if (topK.HasValue)
            requestBody["top_k"] = topK.Value;
        if (topA.HasValue)
            requestBody["top_a"] = topA.Value;
        if (BuildReasoningPayload(reasoning) is { } reasoningPayload)
            requestBody["reasoning"] = reasoningPayload;

        var response = await client.PostAsJsonAsync(
            "https://openrouter.ai/api/v1/chat/completions",
            requestBody,
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        LogResolvedModel(model, json);

        return json.GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    public async IAsyncEnumerable<StreamChunk> ChatStreamAsync(string model, string systemPrompt,
        List<DraftMessage> messages, double temperature = 0.7, double? topP = null, int? maxTokens = null,
        double? minP = null, int? topK = null, double? topA = null,
        ReasoningEffort? reasoning = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        log.LogInformation("OpenRouter stream: model={Model}, messages={MessageCount} ({Settings})",
            model, messages.Count, DescribeSettings(temperature, topP, maxTokens, minP, topK, topA, reasoning));

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
        if (minP.HasValue)
            requestBody["min_p"] = minP.Value;
        if (topK.HasValue)
            requestBody["top_k"] = topK.Value;
        if (topA.HasValue)
            requestBody["top_a"] = topA.Value;
        if (BuildReasoningPayload(reasoning) is { } reasoningPayload)
            requestBody["reasoning"] = reasoningPayload;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions)
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var resolvedLogged = false;

        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            string? content = null;
            string? reasoningText = null;
            try
            {
                var json = JsonDocument.Parse(data);

                // Ahead of the delta, which some chunks lack entirely.
                if (!resolvedLogged)
                    resolvedLogged = LogResolvedModel(model, json.RootElement);

                var delta = json.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("delta");

                if (delta.TryGetProperty("content", out var contentElement))
                    content = contentElement.GetString();

                if (delta.TryGetProperty("reasoning", out var reasoningElement) &&
                    reasoningElement.ValueKind == JsonValueKind.String)
                {
                    reasoningText = reasoningElement.GetString();
                }
                else if (delta.TryGetProperty("reasoning_content", out var rcElement) &&
                         rcElement.ValueKind == JsonValueKind.String)
                {
                    reasoningText = rcElement.GetString();
                }
            }
            catch (JsonException)
            {
                // Skip malformed chunks
            }

            if (!string.IsNullOrEmpty(reasoningText))
            {
                vault.RecordActivity();
                yield return new StreamChunk(StreamChunkKind.Reasoning, reasoningText);
            }
            if (!string.IsNullOrEmpty(content))
            {
                vault.RecordActivity();
                yield return new StreamChunk(StreamChunkKind.Content, content);
            }
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

public enum StreamChunkKind { Content, Reasoning }

public readonly record struct StreamChunk(StreamChunkKind Kind, string Text);
