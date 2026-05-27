using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public sealed record KokoroVoice(string Id, string DisplayName, string Language);

public sealed class KokoroUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class KokoroUpstreamException(string message, int upstreamStatus)
    : Exception(message)
{
    public int UpstreamStatus { get; } = upstreamStatus;
}

public class KokoroService
{
    private static readonly TimeSpan VoicesCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1.5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<CancellationToken, Task<string?>> _baseUrlLookup;

    private readonly object _voicesGate = new();
    private DateTimeOffset _voicesCachedAt = DateTimeOffset.MinValue;
    private IReadOnlyList<KokoroVoice>? _voicesCache;

    private readonly object _probeGate = new();
    private DateTimeOffset _probeCachedAt = DateTimeOffset.MinValue;
    private bool _probeCache;

    /// <summary>Production ctor. Wires the URL lookup to AppSettings.</summary>
    public KokoroService(
        IHttpClientFactory httpClientFactory,
        IServiceProvider services)
        : this(httpClientFactory, ct => GetBaseUrlFromDbAsync(services, ct)) { }

    /// <summary>Testing ctor. Injects the URL lookup directly.</summary>
    public KokoroService(
        IHttpClientFactory httpClientFactory,
        Func<CancellationToken, Task<string?>> baseUrlLookup)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrlLookup = baseUrlLookup;
    }

    private static async Task<string?> GetBaseUrlFromDbAsync(
        IServiceProvider services, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
        var setting = await db.AppSettings.FindAsync(["KokoroBaseUrl"], ct);
        var value = setting?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
    }

    /// <summary>
    /// Called by SettingsEndpoints when the user updates the Kokoro URL,
    /// so the next probe / voices fetch goes to the new server.
    /// </summary>
    public void InvalidateCaches()
    {
        lock (_voicesGate) { _voicesCache = null; _voicesCachedAt = DateTimeOffset.MinValue; }
        lock (_probeGate) { _probeCachedAt = DateTimeOffset.MinValue; }
    }

    /// <summary>
    /// Issues a streaming TTS request to Kokoro and returns the HTTP
    /// response with body still open. Caller must dispose. Use
    /// <see cref="HttpResponseMessage.Content"/>.ReadAsStreamAsync to
    /// read MP3 chunks as Kokoro produces them.
    /// </summary>
    public async Task<HttpResponseMessage> SynthesizeStreamAsync(
        string text, string voice, double speed, CancellationToken ct)
    {
        var baseUrl = await _baseUrlLookup(ct)
            ?? throw new KokoroUnavailableException("Kokoro base URL not configured");

        var client = _httpClientFactory.CreateClient("kokoro");
        var body = new
        {
            model = "kokoro",
            input = text,
            voice,
            response_format = "mp3",
            speed,
            stream = true
        };

        HttpResponseMessage? response = null;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/audio/speech")
            {
                Content = JsonContent.Create(body, options: JsonOptions)
            };
            // ResponseHeadersRead returns as soon as the response headers
            // arrive — the body streams in below as the caller reads it.
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode >= 400)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new KokoroUpstreamException($"Kokoro returned {status}", status);
            }
            return response;
        }
        catch (HttpRequestException ex)
        {
            response?.Dispose();
            throw new KokoroUnavailableException($"Could not reach Kokoro: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<KokoroVoice>> ListVoicesAsync(CancellationToken ct)
    {
        lock (_voicesGate)
        {
            if (_voicesCache is not null && DateTimeOffset.UtcNow - _voicesCachedAt < VoicesCacheTtl)
                return _voicesCache;
        }

        var baseUrl = await _baseUrlLookup(ct)
            ?? throw new KokoroUnavailableException("Kokoro base URL not configured");

        var client = _httpClientFactory.CreateClient("kokoro");
        List<KokoroVoice> normalised;
        try
        {
            var json = await client.GetFromJsonAsync<JsonElement>($"{baseUrl}/v1/audio/voices", ct);
            var ids = new List<string>();
            if (json.TryGetProperty("voices", out var voicesEl) && voicesEl.ValueKind == JsonValueKind.Array)
            {
                // kokoro-fastapi returns either ["af_bella", ...] or
                // [{"id":"af_bella","name":"af_bella"}, ...] depending on
                // version — accept both.
                foreach (var el in voicesEl.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s)
                        ids.Add(s);
                    else if (el.ValueKind == JsonValueKind.Object &&
                             el.TryGetProperty("id", out var idEl) &&
                             idEl.ValueKind == JsonValueKind.String &&
                             idEl.GetString() is { } objectId)
                        ids.Add(objectId);
                }
            }
            normalised = ids.Select(Normalise).ToList();
        }
        catch (HttpRequestException ex)
        {
            throw new KokoroUnavailableException($"Could not reach Kokoro: {ex.Message}", ex);
        }

        lock (_voicesGate)
        {
            _voicesCache = normalised;
            _voicesCachedAt = DateTimeOffset.UtcNow;
            return _voicesCache;
        }
    }

    /// <summary>
    /// Kokoro voice ids follow the convention {language-region prefix}{f|m}_{name},
    /// e.g. "af_bella" = American Female Bella. We unpack that into a friendlier
    /// display label and a coarse language tag.
    /// </summary>
    internal static KokoroVoice Normalise(string id)
    {
        var underscore = id.IndexOf('_');
        if (underscore < 2 || underscore == id.Length - 1)
            return new KokoroVoice(id, id, "unknown");

        var prefix = id[..underscore];
        var name = id[(underscore + 1)..];
        var display = char.ToUpperInvariant(name[0]) + name[1..];

        var region = prefix[..^1] switch
        {
            "a" => "en-us",
            "b" => "en-gb",
            "j" => "ja",
            "z" => "zh",
            "e" => "es",
            "f" => "fr",
            "h" => "hi",
            "i" => "it",
            "p" => "pt-br",
            _ => "unknown"
        };
        var sex = prefix[^1] switch
        {
            'f' => "female",
            'm' => "male",
            _ => "unknown"
        };
        return new KokoroVoice(id, display, $"{region}-{sex}");
    }

    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        lock (_probeGate)
        {
            if (DateTimeOffset.UtcNow - _probeCachedAt < ProbeCacheTtl)
                return _probeCache;
        }

        var result = await ProbeUncachedAsync(ct);
        lock (_probeGate)
        {
            _probeCache = result;
            _probeCachedAt = DateTimeOffset.UtcNow;
            return result;
        }
    }

    private async Task<bool> ProbeUncachedAsync(CancellationToken ct)
    {
        string? baseUrl;
        try { baseUrl = await _baseUrlLookup(ct); }
        catch { return false; }
        if (baseUrl is null) return false;

        var client = _httpClientFactory.CreateClient("kokoro");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);

        try
        {
            using var response = await client.GetAsync($"{baseUrl}/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
