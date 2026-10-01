using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGroup("/search").RequireSession().MapGet("", async (
            string? q, int? limit, int? offset, FabulisDbContext db, HttpResponse response, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-store";
            if (q?.Length > SearchService.MaxQueryLength)
                return Results.BadRequest(new { error = "Search is limited to 500 characters." });
            return Results.Ok(await SearchService.SearchAsync(db, q ?? "", limit ?? 50, offset ?? 0, ct));
        });
        return routes;
    }
}
