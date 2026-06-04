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
                .Include(c => c.Prompts)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var dto = new LibraryResponse(categories
                .Select(c => new CategorySummaryDto(
                    c.Id,
                    c.Name,
                    c.CreatedAt,
                    c.Stories.Count,
                    c.Stories.OrderByDescending(s => s.CreatedAt).FirstOrDefault()?.Title,
                    c.Prompts.Count,
                    c.Prompts.OrderByDescending(p => p.CreatedAt).FirstOrDefault()?.Title))
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

        group.MapGet("/categories/{id:int}/prompts", async (int id, PromptService prompts) =>
        {
            var category = await prompts.GetCategoryWithPromptsAsync(id);
            if (category is null)
                return Results.NotFound();

            var dto = new PromptCategoryDto(
                category.Id,
                category.Name,
                category.CreatedAt,
                category.Prompts
                    .OrderBy(p => p.Title)
                    .Select(p => new PromptSummaryDto(p.Id, p.Title, p.CreatedAt, p.Messages.Count))
                    .ToList());

            return Results.Ok(dto);
        });

        group.MapPost("/categories", async (CreateCategoryRequest body, FabulisDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest(new { error = "name is required" });
            var cat = new Category { Name = body.Name.Trim(), CreatedAt = DateTime.UtcNow };
            db.Categories.Add(cat);
            await db.SaveChangesAsync();
            return Results.Ok(new CategorySummaryDto(cat.Id, cat.Name, cat.CreatedAt, 0, null, 0, null));
        });

        group.MapPut("/categories/{id:int}", async (int id, RenameCategoryRequest body, FabulisDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest(new { error = "name is required" });
            var cat = await db.Categories.FindAsync(id);
            if (cat is null) return Results.NotFound();
            cat.Name = body.Name.Trim();
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapDelete("/categories/{id:int}", async (int id, FabulisDbContext db) =>
        {
            var cat = await db.Categories.Include(c => c.Stories).FirstOrDefaultAsync(c => c.Id == id);
            if (cat is null) return Results.NotFound();
            db.Categories.Remove(cat);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }
}
