using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class SettingsEndpoints
{
    private static readonly HashSet<string> LegalAutoLock =
        new(StringComparer.OrdinalIgnoreCase) { "1", "5", "15", "30", "60", "never" };

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/settings").RequireSession();

        group.MapGet("", async (IVaultStore store, KokoroService kokoro, CancellationToken ct) =>
        {
            var settings = await store.GetSettingsAsync(ct);
            var apiKey = settings.GetValueOrDefault("OpenRouterApiKey");
            var autoLock = settings.GetValueOrDefault("AutoLockMinutes");
            var kokoroUrl = settings.GetValueOrDefault("KokoroBaseUrl");
            var narrationVoice = settings.GetValueOrDefault("NarrationVoice");
            var narrationSpeed = settings.GetValueOrDefault("NarrationSpeed");
            var summaryModel = settings.GetValueOrDefault("SummaryModel");
            var summaryPrompt = settings.GetValueOrDefault("SummaryPrompt");

            var dto = new SettingsDto(
                ApiKeyIsSet: !string.IsNullOrEmpty(apiKey),
                AutoLockSelection: NormalizeAutoLock(autoLock),
                KokoroBaseUrlIsSet: !string.IsNullOrWhiteSpace(kokoroUrl),
                NarrationVoice: narrationVoice,
                NarrationSpeed: NarrationValidation.NormalizeSpeed(null, narrationSpeed),
                NarrationAvailable: await kokoro.ProbeAsync(ct)
                    && !string.IsNullOrWhiteSpace(narrationVoice),
                SummaryModel: summaryModel,
                SummaryPrompt: string.IsNullOrWhiteSpace(summaryPrompt)
                    ? StorySummary.DefaultPrompt
                    : summaryPrompt);

            return Results.Ok(dto);
        });

        group.MapPut("", async (
            SettingsUpdateRequest body,
            IVaultStore store,
            VaultService vault,
            KokoroService kokoro,
            CancellationToken ct) =>
        {
            var changes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (body.ApiKey is { } apiKey && !string.IsNullOrWhiteSpace(apiKey))
                changes["OpenRouterApiKey"] = apiKey.Trim();

            if (body.AutoLockSelection is { } autoLock)
            {
                if (!LegalAutoLock.Contains(autoLock))
                    return Results.BadRequest(new { error = "autoLockSelection must be one of 1, 5, 15, 30, 60, or never" });

                changes["AutoLockMinutes"] = autoLock;
            }

            if (body.KokoroBaseUrl is { } urlInput)
            {
                var trimmed = urlInput.Trim();
                if (trimmed.Length == 0)
                {
                    changes["KokoroBaseUrl"] = "";
                }
                else
                {
                    if (!NarrationValidation.IsBaseUrlValid(trimmed))
                        return Results.BadRequest(new { error = "kokoroBaseUrl must be a valid http(s) URL" });
                    changes["KokoroBaseUrl"] = NarrationValidation.NormalizeBaseUrl(trimmed);
                }
            }

            if (body.NarrationVoice is { } voice && !string.IsNullOrWhiteSpace(voice))
                changes["NarrationVoice"] = voice.Trim();

            if (body.NarrationSpeed is { } speed)
            {
                if (!NarrationValidation.IsSpeedValid(speed))
                    return Results.BadRequest(new { error = $"narrationSpeed must be between {NarrationValidation.MinSpeed} and {NarrationValidation.MaxSpeed}" });
                changes["NarrationSpeed"] = speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (body.SummaryModel is { } summaryModel && !string.IsNullOrWhiteSpace(summaryModel))
                changes["SummaryModel"] = summaryModel.Trim();

            if (body.SummaryPrompt is { } summaryPrompt && !string.IsNullOrWhiteSpace(summaryPrompt))
                changes["SummaryPrompt"] = summaryPrompt.Trim();

            await store.UpdateSettingsAsync(changes, ct);
            // Apply runtime effects only after the whole validated update commits.
            if (changes.TryGetValue("AutoLockMinutes", out var savedAutoLock))
                vault.ConfigureAutoLock(savedAutoLock.Equals("never", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(savedAutoLock));
            if (changes.ContainsKey("KokoroBaseUrl")) kokoro.InvalidateCaches();
            return Results.NoContent();
        });

        return routes;
    }

    private static string NormalizeAutoLock(string? raw)
    {
        if (string.Equals(raw, "never", StringComparison.OrdinalIgnoreCase))
            return "never";
        if (int.TryParse(raw, out var parsed) && LegalAutoLock.Contains(parsed.ToString()))
            return parsed.ToString();
        return "15";
    }
}
