using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class AuthEndpoints
{
    private static int? ParseAutoLockMinutes(string? raw)
    {
        if (string.Equals(raw, "never", StringComparison.OrdinalIgnoreCase))
            return null;
        if (int.TryParse(raw, out var parsed) && parsed is 1 or 5 or 15 or 30 or 60)
            return parsed;
        return 15;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/auth");

        group.MapPost("/unlock", async (
            UnlockRequest body,
            VaultService vault,
            SessionTokenStore tokens,
            IServiceProvider services) =>
        {
            if (string.IsNullOrWhiteSpace(body.Password))
                return Results.BadRequest(new { error = "password is required" });

            vault.Unlock(body.Password);

            try
            {
                await using var scope = services.CreateAsyncScope();
                await using var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
                await db.Database.EnsureCreatedAsync();
                await db.EnsureSchemaUpdatedAsync();

                var setting = await db.AppSettings.FindAsync("AutoLockMinutes");
                vault.ConfigureAutoLock(ParseAutoLockMinutes(setting?.Value));
            }
            catch
            {
                vault.Lock();
                return Results.Unauthorized();
            }

            var info = tokens.Issue();
            return Results.Ok(new UnlockResponse(info.Token, info.IssuedAt));
        });

        group.MapPost("/lock", (VaultService vault) =>
        {
            vault.Lock();
            return Results.NoContent();
        }).RequireSession();

        group.MapGet("/status", (VaultService vault) =>
        {
            var minutes = vault.AutoLockTimeout is { } t ? (int?)t.TotalMinutes : null;
            return Results.Ok(new AuthStatusResponse(vault.IsUnlocked, minutes));
        }).RequireSession();

        return routes;
    }
}
