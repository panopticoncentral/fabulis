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

        group.MapGet("", async (FabulisDbContext db) =>
        {
            var apiKey = await db.AppSettings.FindAsync("OpenRouterApiKey");
            var assistantModel = await db.AppSettings.FindAsync("AssistantModel");
            var autoLock = await db.AppSettings.FindAsync("AutoLockMinutes");

            var dto = new SettingsDto(
                ApiKeyIsSet: apiKey is not null && !string.IsNullOrEmpty(apiKey.Value),
                AssistantModel: assistantModel?.Value,
                AutoLockSelection: NormalizeAutoLock(autoLock?.Value));

            return Results.Ok(dto);
        });

        group.MapPut("", async (
            SettingsUpdateRequest body,
            FabulisDbContext db,
            VaultService vault) =>
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
