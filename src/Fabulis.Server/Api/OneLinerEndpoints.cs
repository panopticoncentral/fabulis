using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class OneLinerEndpoints
{
    public static IEndpointRouteBuilder MapOneLinerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/one-liners").RequireSession();

        group.MapPost("", async (CreateOneLinerRequest body, OneLinerService oneLiners) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text is required" });
            if (!await oneLiners.CategoryExistsAsync(body.CategoryId))
                return Results.BadRequest(new { error = "category does not exist" });
            var created = await oneLiners.CreateOneLinerAsync(body.CategoryId, body.Text);
            var full = await oneLiners.GetOneLinerAsync(created.Id);
            return Results.Ok(ToDto(full!));
        });

        group.MapPut("/{id:int}", async (int id, UpdateOneLinerRequest body, OneLinerService oneLiners) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text is required" });
            if (!await oneLiners.CategoryExistsAsync(body.CategoryId))
                return Results.BadRequest(new { error = "category does not exist" });
            var updated = await oneLiners.UpdateOneLinerAsync(id, body.Text, body.CategoryId);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        });

        group.MapDelete("/{id:int}", async (int id, OneLinerService oneLiners) =>
        {
            return await oneLiners.DeleteOneLinerAsync(id) ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }

    private static OneLinerDto ToDto(OneLiner o) => new(
        o.Id,
        o.CategoryId,
        o.Category?.Name ?? "",
        o.Text,
        o.CreatedAt,
        o.UpdatedAt);
}
