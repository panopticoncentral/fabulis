using Fabulis.Server.Data;

namespace Fabulis.Server.Auth;

public static class SessionTokenAuthExtensions
{
    private const string BearerPrefix = "Bearer ";

    public static TBuilder RequireSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var tokens = ctx.HttpContext.RequestServices.GetRequiredService<SessionTokenStore>();
            var vault = ctx.HttpContext.RequestServices.GetRequiredService<VaultService>();

            if (!vault.IsUnlocked)
                return Results.Unauthorized();

            var header = ctx.HttpContext.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
                return Results.Unauthorized();

            var token = header[BearerPrefix.Length..].Trim();
            if (!tokens.IsValid(token))
                return Results.Unauthorized();

            return await next(ctx);
        });
        return builder;
    }
}
