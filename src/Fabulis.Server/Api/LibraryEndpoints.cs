using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("").RequireSession();

        group.MapGet("/library", async (FabulisDbContext db) =>
        {
            var categories = await db.Categories
                .Include(c => c.Stories)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var dto = new LibraryResponse(categories
                .Select(c => new CategorySummaryDto(
                    c.Id,
                    c.Name,
                    c.CreatedAt,
                    c.Stories.Count,
                    c.Stories.OrderByDescending(s => s.CreatedAt).FirstOrDefault()?.Title))
                .ToList());

            return Results.Ok(dto);
        });

        group.MapGet("/categories/{id:int}", async (int id, FabulisDbContext db) =>
        {
            var category = await db.Categories
                .Include(c => c.Stories)
                    .ThenInclude(s => s.Versions)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category is null)
                return Results.NotFound();

            var dto = new CategoryDto(
                category.Id,
                category.Name,
                category.CreatedAt,
                category.Stories
                    .OrderBy(s => s.Title)
                    .Select(s => new StorySummaryDto(s.Id, s.Title, s.CreatedAt, s.Versions.Count))
                    .ToList());

            return Results.Ok(dto);
        });

        return routes;
    }
}
