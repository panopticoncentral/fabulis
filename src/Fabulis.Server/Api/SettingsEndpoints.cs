using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class SettingsEndpoints
{
    private static readonly HashSet<string> LegalAutoLock =
        new(StringComparer.OrdinalIgnoreCase) { "1", "5", "15", "30", "60", "never" };

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/settings").RequireSession();

        group.MapGet("", async (FabulisDbContext db, KokoroService kokoro, CancellationToken ct) =>
        {
            var apiKey = await db.AppSettings.FindAsync(["OpenRouterApiKey"], ct);
            var assistantModel = await db.AppSettings.FindAsync(["AssistantModel"], ct);
            var autoLock = await db.AppSettings.FindAsync(["AutoLockMinutes"], ct);
            var kokoroUrl = await db.AppSettings.FindAsync(["KokoroBaseUrl"], ct);
            var narrationVoice = await db.AppSettings.FindAsync(["NarrationVoice"], ct);
            var narrationSpeed = await db.AppSettings.FindAsync(["NarrationSpeed"], ct);
            var summaryModel = await db.AppSettings.FindAsync(["SummaryModel"], ct);
            var summaryPrompt = await db.AppSettings.FindAsync(["SummaryPrompt"], ct);

            var dto = new SettingsDto(
                ApiKeyIsSet: apiKey is not null && !string.IsNullOrEmpty(apiKey.Value),
                AssistantModel: assistantModel?.Value,
                AutoLockSelection: NormalizeAutoLock(autoLock?.Value),
                KokoroBaseUrlIsSet: kokoroUrl is not null && !string.IsNullOrWhiteSpace(kokoroUrl.Value),
                NarrationVoice: narrationVoice?.Value,
                NarrationSpeed: NarrationValidation.NormalizeSpeed(null, narrationSpeed?.Value),
                NarrationAvailable: await kokoro.ProbeAsync(ct)
                    && !string.IsNullOrWhiteSpace(narrationVoice?.Value),
                SummaryModel: summaryModel?.Value,
                SummaryPrompt: string.IsNullOrWhiteSpace(summaryPrompt?.Value)
                    ? StorySummary.DefaultPrompt
                    : summaryPrompt!.Value);

            return Results.Ok(dto);
        });

        group.MapPut("", async (
            SettingsUpdateRequest body,
            FabulisDbContext db,
            VaultService vault,
            KokoroService kokoro) =>
        {
            if (body.ApiKey is { } apiKey && !string.IsNullOrWhiteSpace(apiKey))
                await UpsertAsync(db, "OpenRouterApiKey", apiKey.Trim());

            if (body.AssistantModel is { } model && !string.IsNullOrWhiteSpace(model))
                await UpsertAsync(db, "AssistantModel", model.Trim());

            if (body.AutoLockSelection is { } autoLock)
            {
                if (!LegalAutoLock.Contains(autoLock))
                    return Results.BadRequest(new { error = "autoLockSelection must be one of 1, 5, 15, 30, 60, or never" });

                await UpsertAsync(db, "AutoLockMinutes", autoLock);
                vault.ConfigureAutoLock(autoLock.Equals("never", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(autoLock));
            }

            if (body.KokoroBaseUrl is { } urlInput)
            {
                var trimmed = urlInput.Trim();
                if (trimmed.Length == 0)
                {
                    await UpsertAsync(db, "KokoroBaseUrl", "");
                }
                else
                {
                    if (!NarrationValidation.IsBaseUrlValid(trimmed))
                        return Results.BadRequest(new { error = "kokoroBaseUrl must be a valid http(s) URL" });
                    await UpsertAsync(db, "KokoroBaseUrl", NarrationValidation.NormalizeBaseUrl(trimmed));
                }
                kokoro.InvalidateCaches();
            }

            if (body.NarrationVoice is { } voice && !string.IsNullOrWhiteSpace(voice))
                await UpsertAsync(db, "NarrationVoice", voice.Trim());

            if (body.NarrationSpeed is { } speed)
            {
                if (!NarrationValidation.IsSpeedValid(speed))
                    return Results.BadRequest(new { error = $"narrationSpeed must be between {NarrationValidation.MinSpeed} and {NarrationValidation.MaxSpeed}" });
                await UpsertAsync(db, "NarrationSpeed", speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (body.SummaryModel is { } summaryModel && !string.IsNullOrWhiteSpace(summaryModel))
                await UpsertAsync(db, "SummaryModel", summaryModel.Trim());

            if (body.SummaryPrompt is { } summaryPrompt && !string.IsNullOrWhiteSpace(summaryPrompt))
                await UpsertAsync(db, "SummaryPrompt", summaryPrompt.Trim());

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }

    private static async Task UpsertAsync(FabulisDbContext db, string key, string value)
    {
        var existing = await db.AppSettings.FindAsync(key);
        if (existing is not null)
            existing.Value = value;
        else
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
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
