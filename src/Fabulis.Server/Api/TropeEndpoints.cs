using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class TropeEndpoints
{
    public static IEndpointRouteBuilder MapTropeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/tropes").RequireSession();

        group.MapPost("", async (CreateTropeRequest body, TropeService tropes) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text is required" });
            if (!await tropes.CategoryExistsAsync(body.CategoryId))
                return Results.BadRequest(new { error = "category does not exist" });
            var created = await tropes.CreateTropeAsync(body.CategoryId, body.Text);
            var full = await tropes.GetTropeAsync(created.Id);
            return Results.Ok(ToDto(full!));
        });

        group.MapPut("/{id:int}", async (int id, UpdateTropeRequest body, TropeService tropes) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text is required" });
            if (!await tropes.CategoryExistsAsync(body.CategoryId))
                return Results.BadRequest(new { error = "category does not exist" });
            var updated = await tropes.UpdateTropeAsync(id, body.Text, body.CategoryId);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        });

        group.MapDelete("/{id:int}", async (int id, TropeService tropes) =>
        {
            return await tropes.DeleteTropeAsync(id) ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }

    private static TropeDto ToDto(Trope t) => new(
        t.Id,
        t.CategoryId,
        t.Category?.Name ?? "",
        t.Text,
        t.CreatedAt,
        t.UpdatedAt);
}
