using Fabulis.Server.Auth;
using Fabulis.Server.Components.Pages;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class AuthEndpoints
{
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
                vault.ConfigureAutoLock(Unlock.ParseAutoLockMinutes(setting?.Value));
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
